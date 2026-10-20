using System.Diagnostics;
using Flower.Agents.Agents;
using Flower.Agents.Configuration;
using Flower.Agents.Knowledge;
using Flower.Agents.Runs;
using Flower.Agents.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flower.Agents.Orchestration;

/// <summary>
/// docs/03 akışı. Agent sırası koddadır; LLM yalnızca adımın içindeki işi yapar.
/// Her soru için (Enabled=false hariç) bir <see cref="AgentRun"/> yazılır.
/// </summary>
public sealed class AgentOrchestrator(
    IIntentRouter router,
    IQueryPlanner planner,
    HybridRetriever retriever,
    ISufficiencyJudge judge,
    IAnswerSynthesizer synthesizer,
    IAgentRunStore runs,
    IOptions<AgentOptions> options,
    TimeProvider time,
    ILogger<AgentOrchestrator> log) : IAgentOrchestrator
{
    public const string OutOfScopeMessage =
        "Bu konu kurum içi bilgi kaynaklarımın kapsamı dışında. Kurumla ilgili (sistem, süreç, ekip, doküman vb.) sorularda yardımcı olabilirim.";

    public const string SourcesUnavailableMessage =
        "Bilgi kaynaklarına şu an erişemiyorum. Lütfen biraz sonra tekrar deneyin.";

    public const string ModelUnavailableMessage =
        "Şu an cevap üretemiyorum: model servisine erişilemedi. Lütfen biraz sonra tekrar deneyin.";

    public const string NotFoundMessage = "Kaynaklarda bu konuda bilgi bulamadım.";

    public async Task<AgentOutcome> HandleAsync(AgentRequest request, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.Enabled)
        {
            return new AgentOutcome { Kind = AgentOutcomeKind.PassThrough };
        }

        using var activity = AgentTelemetry.Source.StartActivity("agent.handle");
        var total = Stopwatch.StartNew();
        var steps = new List<AgentStep>();
        var run = new AgentRun
        {
            Id = Guid.NewGuid(),
            RequestId = request.RequestId,
            UserEmail = request.UserEmail.Trim().ToLowerInvariant(),
            Question = request.Question,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };

        AgentOutcome? outcome = null;
        try
        {
            outcome = await RunFlowAsync(request, run, steps, ct).ConfigureAwait(false);
            return outcome;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Kind = AgentOutcomeKind.Failed;
            run.Error = "İptal edildi";
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Agent akışı hata verdi. RequestId={RequestId}", request.RequestId);
            run.Kind = AgentOutcomeKind.Failed;
            run.Error = ex.GetType().Name + ": " + ex.Message;
            outcome = new AgentOutcome { Kind = AgentOutcomeKind.Failed, Message = ModelUnavailableMessage, RunId = run.Id };
            return outcome;
        }
        finally
        {
            run.CompletedAt = time.GetUtcNow().UtcDateTime;
            run.SetSteps(steps);
            await SaveRunAsync(run).ConfigureAwait(false);

            activity?.SetTag("flower.agent.outcome", run.Kind.ToString());
            log.LogInformation(
                "TIMING agent {Kind} {ElapsedMs}ms RequestId={RequestId} Verdict={Verdict} Confidence={Confidence}",
                run.Kind, total.ElapsedMilliseconds, request.RequestId, run.Verdict, run.Confidence);
        }
    }

    private async Task<AgentOutcome> RunFlowAsync(AgentRequest request, AgentRun run, List<AgentStep> steps, CancellationToken ct)
    {
        var o = options.Value;

        // 1. Router
        var sw = Stopwatch.StartNew();
        var intent = await router.ClassifyAsync(request.Question, request.ConversationContext, ct).ConfigureAwait(false);
        steps.Add(new AgentStep(AgentNames.Router, sw.ElapsedMilliseconds, intent.Tokens, intent.IsFallback, intent.Value.Intent.ToString()));
        run.Intent = intent.Value.Intent;

        switch (intent.Value.Intent)
        {
            case IntentKind.SmallTalk:
                return Finish(run, new AgentOutcome { Kind = AgentOutcomeKind.PassThrough });
            case IntentKind.OutOfScope:
                return Finish(run, new AgentOutcome { Kind = AgentOutcomeKind.OutOfScope, Message = OutOfScopeMessage });
        }

        // 2. Planner
        sw.Restart();
        var plan = await planner.PlanAsync(request.Question, ct).ConfigureAwait(false);
        steps.Add(new AgentStep(AgentNames.Planner, sw.ElapsedMilliseconds, plan.Tokens, plan.IsFallback));

        // 3. Retrieval
        sw.Restart();
        var keywords = plan.Value.Keywords.Concat(plan.Value.EnglishKeywords)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var query = new KnowledgeQuery(request.Question, plan.Value.SubQuestions, keywords, o.Retrieval.PerSourceTopK, request.AllowedSpaces);
        var retrieval = await retriever.RetrieveAsync(query, ct).ConfigureAwait(false);
        steps.Add(new AgentStep(
            "Retrieve", sw.ElapsedMilliseconds, 0, false,
            $"{retrieval.Hits.Count} sonuç; hata: {string.Join(",", retrieval.FailedSources)}"));

        if (retrieval.AllSourcesFailed)
        {
            run.Error = "Tüm kaynaklar hata verdi: " + string.Join(", ", retrieval.FailedSources);
            return Finish(run, new AgentOutcome { Kind = AgentOutcomeKind.Failed, Message = SourcesUnavailableMessage });
        }

        if (retrieval.Hits.Count == 0)
        {
            run.Verdict = SufficiencyLevel.Insufficient;
            return Finish(run, NotFound(keywords, null));
        }

        // 4. Judge
        sw.Restart();
        var judged = await judge.JudgeAsync(request.Question, retrieval.Hits, ct).ConfigureAwait(false);
        var verdict = judged.Value;
        steps.Add(new AgentStep(AgentNames.Judge, sw.ElapsedMilliseconds, judged.Tokens, judged.IsFallback, verdict.Verdict.ToString()));

        // Sufficient ama güven düşükse Partial muamelesi.
        if (verdict.Verdict == SufficiencyLevel.Sufficient && verdict.Confidence < o.Retrieval.MinAnswerConfidence)
        {
            verdict = new SufficiencyVerdict
            {
                Verdict = SufficiencyLevel.Partial,
                Confidence = verdict.Confidence,
                UsedSources = verdict.UsedSources,
                Missing = verdict.Missing,
                Reason = verdict.Reason,
            };
        }

        run.Verdict = verdict.Verdict;
        run.Confidence = verdict.Confidence;

        if (verdict.Verdict == SufficiencyLevel.Insufficient)
        {
            return Finish(run, NotFound(keywords, verdict));
        }

        // 5. Synthesizer
        sw.Restart();
        var draft = await synthesizer.SynthesizeAsync(
            request.Question, retrieval.Hits, verdict, request.UserDisplayName, request.ConversationContext, ct).ConfigureAwait(false);
        steps.Add(new AgentStep(AgentNames.Synthesizer, sw.ElapsedMilliseconds, draft.Tokens, false, $"atıf: {string.Join(",", draft.CitedSources)}"));

        var cited = draft.CitedSources
            .Order()
            .Select(n => new CitedSource(n, retrieval.Hits[n - 1]))
            .ToList();
        var body = AnswerFormatter.BuildBody(draft.Text, verdict);
        var message = AnswerFormatter.BuildMessage(
            body, cited, o.KnowledgeBase.FeedbackHint, time.GetUtcNow(), o.KnowledgeBase.ReviewAfterDays);

        run.AnswerText = body;
        run.SetCitedSources(cited);
        return Finish(run, new AgentOutcome
        {
            Kind = AgentOutcomeKind.Answered,
            Message = message,
            Sources = cited.Select(c => c.Hit).ToList(),
            Verdict = verdict,
        });
    }

    private static AgentOutcome NotFound(IReadOnlyList<string> keywords, SufficiencyVerdict? verdict)
    {
        var message = keywords.Count > 0 ? $"{NotFoundMessage} Aranan konu: {string.Join(", ", keywords)}" : NotFoundMessage;
        return new AgentOutcome { Kind = AgentOutcomeKind.NotFound, Message = message, Verdict = verdict };
    }

    private static AgentOutcome Finish(AgentRun run, AgentOutcome outcome)
    {
        run.Kind = outcome.Kind;
        return outcome with { RunId = run.Id };
    }

    private async Task SaveRunAsync(AgentRun run)
    {
        try
        {
            // İptal edilse bile kayıt yazılsın.
            await runs.AddAsync(run, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "AgentRun kaydedilemedi. RunId={RunId}", run.Id);
        }
    }
}

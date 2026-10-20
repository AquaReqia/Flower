using Flower.Agents.Abstractions;
using Flower.Agents.Configuration;
using Flower.Agents.Knowledge;
using Flower.Agents.Prompts;
using Flower.Agents.Structured;
using Microsoft.Extensions.Options;

namespace Flower.Agents.Agents;

/// <summary>Kaynakların soruyu cevaplamaya yetip yetmediğine karar verir.</summary>
public interface ISufficiencyJudge
{
    Task<StructuredResult<SufficiencyVerdict>> JudgeAsync(string question, IReadOnlyList<KnowledgeHit> hits, CancellationToken ct);
}

/// <summary>Judge agent'ı. Fallback: <see cref="SufficiencyLevel.Insufficient"/>, 0 güven.</summary>
public sealed class SufficiencyJudge(StructuredOutputInvoker invoker, PromptLibrary prompts, IOptions<AgentOptions> options)
    : ISufficiencyJudge
{
    public async Task<StructuredResult<SufficiencyVerdict>> JudgeAsync(string question, IReadOnlyList<KnowledgeHit> hits, CancellationToken ct)
    {
        var user = prompts.Render(PromptLibrary.JudgeUser, new Dictionary<string, string>
        {
            ["question"] = question,
            ["sources"] = ContextFormatter.FormatHits(hits, options.Value.Retrieval.MaxSnippetChars),
        });

        var result = await invoker.InvokeAsync(
            AgentNames.Judge,
            prompts.Get(PromptLibrary.Judge),
            user,
            () => new SufficiencyVerdict { Verdict = SufficiencyLevel.Insufficient, Confidence = 0, Reason = "fallback" },
            ModelPriority.Interactive,
            ct).ConfigureAwait(false);

        // Model aralık dışı değer verebilir: güveni 0–1'e, kaynak numaralarını mevcut kaynaklara sınırla.
        var v = result.Value;
        return result with
        {
            Value = new SufficiencyVerdict
            {
                Verdict = v.Verdict,
                Confidence = Math.Clamp(v.Confidence, 0, 1),
                UsedSources = [.. v.UsedSources.Where(n => n >= 1 && n <= hits.Count).Distinct()],
                Missing = v.Missing,
                Reason = v.Reason,
            },
        };
    }
}

using System.Text.Json;
using Flower.Agents.Abstractions;
using Flower.Agents.Agents;
using Flower.Agents.Configuration;
using Flower.Agents.Knowledge;
using Flower.Agents.Orchestration;
using Flower.Agents.Runs;
using Flower.Agents.Structured;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flower.Agents.KnowledgeBase;

/// <summary>Geri bildirim işleminin kullanıcıya gidecek sonucu.</summary>
public sealed record FeedbackResult(bool Success, string Message);

/// <summary>#Dogru ile bilgi deposuna yazma, #Yanlis ile askıya alma (docs/04 §4.4–4.5).</summary>
public sealed class KnowledgeCurator(
    IAgentRunStore runs,
    IKnowledgeStore store,
    IKnowledgeWriter writer,
    IKnowledgePublisher publisher,
    IOptions<AgentOptions> options,
    TimeProvider time,
    ILogger<KnowledgeCurator> log,
    IEmbeddingClient? embedding = null)
{
    public const string NothingToConfirm = "Onaylanacak bir cevap bulamadım. #Dogru, son {0} saat içindeki ve henüz geri bildirim verilmemiş bir cevaba uygulanabilir.";
    public const string NothingToDispute = "İtiraz edilecek bir cevap bulamadım. #Yanlis, son {0} saat içindeki bir cevaba uygulanabilir.";

    public async Task<FeedbackResult> CaptureAsync(string userEmail, string? note, CancellationToken ct)
    {
        var o = options.Value.KnowledgeBase;
        var email = Normalize(userEmail);
        var run = await FindLatestRunAsync(runs, email, o.ConfirmWindowHours, time, ct).ConfigureAwait(false);
        if (run is not { Kind: AgentOutcomeKind.Answered, Feedback: AgentFeedback.None } || string.IsNullOrWhiteSpace(run.AnswerText))
        {
            return new FeedbackResult(false, string.Format(NothingToConfirm, o.ConfirmWindowHours));
        }

        var pii = PiiGuard.Detect(run.AnswerText + "\n" + note);
        if (pii is not null)
        {
            await MarkAsync(run, AgentFeedback.Confirmed, note, ct).ConfigureAwait(false);
            return new FeedbackResult(true, $"Teşekkürler. Cevap kişisel veri ({pii}) içerdiği için bilgi deposuna eklemedim.");
        }

        var cited = run.GetCitedSources();
        var written = await writer.WriteAsync(run.Question, run.AnswerText, note, cited.Select(c => c.Hit).ToList(), ct).ConfigureAwait(false);
        var kn = written.Value;
        pii = kn.ShouldStore ? PiiGuard.Detect(kn.Title + "\n" + kn.Answer) : null;
        if (!kn.ShouldStore || pii is not null || string.IsNullOrWhiteSpace(kn.Answer))
        {
            await MarkAsync(run, AgentFeedback.Confirmed, note, ct).ConfigureAwait(false);
            var reason = pii is not null ? $"kişisel veri ({pii}) içeriyor" : kn.SkipReason;
            return new FeedbackResult(true, $"Teşekkürler. Bu cevap kişisel/geçici göründüğü için bilgi deposuna eklemedim: {reason}");
        }

        var vector = await EmbedAsync(kn.CanonicalQuestion + "\n" + kn.Answer, ct).ConfigureAwait(false);
        var now = time.GetUtcNow().UtcDateTime;
        var citedEntryIds = cited
            .Where(c => c.Hit.Kind == KnowledgeSourceKind.KnowledgeBase && Guid.TryParse(c.Hit.SourceId, out _))
            .Select(c => Guid.Parse(c.Hit.SourceId))
            .ToHashSet();

        var duplicate = await FindDuplicateAsync(kn, vector, citedEntryIds, ct).ConfigureAwait(false);
        if (duplicate is not null && citedEntryIds.Contains(duplicate.Id))
        {
            // Yeniden onay: aynı kayda dayanan cevap tekrar doğrulandı.
            var confirmers = KnowledgeEntry.SplitList(duplicate.ConfirmedByEmails).ToList();
            if (!confirmers.Contains(email, StringComparer.OrdinalIgnoreCase))
            {
                confirmers.Add(email);
                duplicate.ConfirmedByEmails = KnowledgeEntry.JoinList(confirmers);
                duplicate.ConfirmationCount++;
                duplicate.Status = KnowledgeStatus.Verified;
                duplicate.Confidence = Math.Max(duplicate.Confidence, o.VerifiedConfidence);
            }

            duplicate.LastConfirmedAt = now;
            duplicate.UpdatedAt = now;
            await store.UpdateAsync(duplicate, ct).ConfigureAwait(false);
            await publisher.PublishAsync(duplicate, ct).ConfigureAwait(false);
            await MarkAsync(run, AgentFeedback.Confirmed, note, ct).ConfigureAwait(false);
            log.LogInformation("Bilgi kaydı yeniden onaylandı. EntryId={EntryId} Count={Count}", duplicate.Id, duplicate.ConfirmationCount);
            return new FeedbackResult(true, $"Teşekkürler, «{duplicate.Title}» bilgisini yeniden onayladım ({duplicate.ConfirmationCount} onay).");
        }

        var entry = new KnowledgeEntry
        {
            Id = Guid.NewGuid(),
            Title = kn.Title.Trim(),
            CanonicalQuestion = string.IsNullOrWhiteSpace(kn.CanonicalQuestion) ? run.Question : kn.CanonicalQuestion.Trim(),
            Answer = kn.Answer.Trim(),
            Keywords = KnowledgeEntry.JoinList(kn.Keywords),
            Aliases = KnowledgeEntry.JoinList(kn.Aliases),
            SourceRefs = JsonSerializer.Serialize(
                cited.Select(c => new KnowledgeSourceRef(c.Hit.Title, c.Hit.Url, c.Hit.SourceName, c.Hit.SpaceKey)),
                StructuredOutputInvoker.Json),
            SourceSpaces = SourceSpaces(cited),
            Embedding = vector is null ? null : Text.VectorMath.ToBytes(vector),
            EmbeddingModel = vector is null ? null : options.Value.Embedding.Model,
            Status = KnowledgeStatus.Confirmed,
            Confidence = o.ConfirmedConfidence,
            ConfirmationCount = 1,
            CreatedByEmail = email,
            ConfirmedByEmails = email,
            CreatedFromRunId = run.Id,
            CreatedAt = now,
            UpdatedAt = now,
            LastConfirmedAt = now,
        };
        await store.AddAsync(entry, ct).ConfigureAwait(false);

        if (duplicate is not null)
        {
            // Yenileme: aynı soru başka kaynaktan cevaplandı ve onaylandı.
            duplicate.Status = KnowledgeStatus.Superseded;
            duplicate.SupersededById = entry.Id;
            duplicate.UpdatedAt = now;
            await store.UpdateAsync(duplicate, ct).ConfigureAwait(false);
        }

        await publisher.PublishAsync(entry, ct).ConfigureAwait(false);
        await MarkAsync(run, AgentFeedback.Confirmed, note, ct).ConfigureAwait(false);
        log.LogInformation("Bilgi kaydı oluşturuldu. EntryId={EntryId} RunId={RunId} Superseded={Superseded}", entry.Id, run.Id, duplicate?.Id);
        return new FeedbackResult(true, $"Kaydettim: «{entry.Title}». Bundan sonra bu soruya buradan cevap vereceğim.");
    }

    public async Task<FeedbackResult> DisputeAsync(string userEmail, string? note, CancellationToken ct)
    {
        var o = options.Value.KnowledgeBase;
        var run = await FindLatestRunAsync(runs, Normalize(userEmail), o.ConfirmWindowHours, time, ct).ConfigureAwait(false);
        if (run is null)
        {
            return new FeedbackResult(false, string.Format(NothingToDispute, o.ConfirmWindowHours));
        }

        var now = time.GetUtcNow().UtcDateTime;
        var suspended = new List<string>();
        foreach (var c in run.GetCitedSources().Where(c => c.Hit.Kind == KnowledgeSourceKind.KnowledgeBase))
        {
            if (!Guid.TryParse(c.Hit.SourceId, out var id) || await store.GetAsync(id, ct).ConfigureAwait(false) is not { } entry)
            {
                continue;
            }

            entry.DisputeCount++;
            entry.LastDisputeNote = note;
            entry.Status = KnowledgeStatus.Disputed;
            entry.Confidence *= 0.5;
            entry.UpdatedAt = now;
            await store.UpdateAsync(entry, ct).ConfigureAwait(false);
            suspended.Add(entry.Title);
        }

        await MarkAsync(run, AgentFeedback.Disputed, note, ct).ConfigureAwait(false);
        var message = "Not aldım, bu cevabı hatalı olarak işaretledim.";
        if (suspended.Count > 0)
        {
            message += " " + string.Join(" ", suspended.Select(t => $"«{t}» bilgisini askıya aldım."));
        }

        return new FeedbackResult(true, message);
    }

    /// <summary>Kullanıcının pencere içindeki son (PassThrough olmayan) run'ı.</summary>
    internal static async Task<AgentRun?> FindLatestRunAsync(IAgentRunStore runs, string email, int windowHours, TimeProvider time, CancellationToken ct)
    {
        var since = windowHours > 0 ? time.GetUtcNow().UtcDateTime.AddHours(-windowHours) : DateTime.MinValue;
        var recent = await runs.GetRecentForUserAsync(email, since, 20, ct).ConfigureAwait(false);
        return recent.FirstOrDefault(r => r.Kind != AgentOutcomeKind.PassThrough);
    }

    internal static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private async Task<KnowledgeEntry?> FindDuplicateAsync(KnowledgeNote kn, float[]? vector, HashSet<Guid> citedEntryIds, CancellationToken ct)
    {
        var o = options.Value.KnowledgeBase;
        var keywords = kn.Keywords.Count > 0 ? kn.Keywords : Text.KeywordExtractor.Extract(kn.CanonicalQuestion);
        var candidates = await store.SearchAsync(
            new KnowledgeSearch(kn.CanonicalQuestion, keywords, vector, 5, o.DuplicateSimilarity, null), ct).ConfigureAwait(false);

        var dup = candidates
            .Select(c => c.Entry)
            .FirstOrDefault(e => KnowledgeScoring.IsDuplicate(e, kn.CanonicalQuestion, keywords, vector, o.DuplicateSimilarity));
        if (dup is not null)
        {
            return dup;
        }

        // Writer soruyu farklı ifade etmiş olabilir: cevap tek bir aktif kayda dayanıyorsa o kayıt aynı bilgidir.
        if (citedEntryIds.Count == 1 && await store.GetAsync(citedEntryIds.First(), ct).ConfigureAwait(false) is { IsActive: true } cited)
        {
            return cited;
        }

        return null;
    }

    private async Task<float[]?> EmbedAsync(string text, CancellationToken ct)
    {
        if (!options.Value.Embedding.Enabled || embedding is null)
        {
            return null;
        }

        return (await embedding.EmbedAsync([text], ct).ConfigureAwait(false))[0];
    }

    private async Task MarkAsync(AgentRun run, AgentFeedback feedback, string? note, CancellationToken ct)
    {
        run.Feedback = feedback;
        run.FeedbackNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        run.FeedbackAt = time.GetUtcNow().UtcDateTime;
        await runs.UpdateAsync(run, ct).ConfigureAwait(false);
    }

    private static string? SourceSpaces(IReadOnlyList<CitedSource> cited)
    {
        var spaces = KnowledgeEntry.JoinList(cited.Select(c => c.Hit.SpaceKey).OfType<string>());
        return spaces.Length == 0 ? null : spaces;
    }
}

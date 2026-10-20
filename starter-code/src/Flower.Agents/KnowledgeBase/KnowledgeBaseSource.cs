using Flower.Agents.Abstractions;
using Flower.Agents.Configuration;
using Flower.Agents.Knowledge;
using Microsoft.Extensions.Options;

namespace Flower.Agents.KnowledgeBase;

/// <summary>Doğrulanmış bilgi deposunu <see cref="IKnowledgeSource"/> olarak sunar. Embedding kapalıysa anahtar kelimeyle arar.</summary>
public sealed class KnowledgeBaseSource(
    IKnowledgeStore store,
    IOptions<AgentOptions> options,
    IEmbeddingClient? embedding = null) : IKnowledgeSource
{
    public const string SourceName = "knowledge-base";

    public string Name => SourceName;

    public double Weight => options.Value.KnowledgeBase.Weight;

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(KnowledgeQuery query, CancellationToken ct)
    {
        var o = options.Value;
        float[]? vector = null;
        if (o.Embedding.Enabled && embedding is not null)
        {
            vector = (await embedding.EmbedAsync([query.Question], ct).ConfigureAwait(false))[0];
        }

        var found = await store.SearchAsync(
            new KnowledgeSearch(query.Question, query.Keywords, vector, Math.Min(query.TopK, o.KnowledgeBase.TopK), o.KnowledgeBase.MinSimilarity, query.AllowedSpaces),
            ct).ConfigureAwait(false);

        return found.Select(x => ToHit(x.Entry, x.Score)).ToList();
    }

    public static KnowledgeHit ToHit(KnowledgeEntry e, double score) => new()
    {
        SourceName = SourceName,
        Kind = KnowledgeSourceKind.KnowledgeBase,
        SourceId = e.Id.ToString(),
        Title = e.Title,
        Snippet = e.Answer,
        LastModified = new DateTimeOffset(DateTime.SpecifyKind(e.LastConfirmedAt, DateTimeKind.Utc)),
        Score = score,
        Confidence = e.Confidence,
        StatusLabel = StatusLabel(e),
    };

    public static string StatusLabel(KnowledgeEntry e) => e.Status switch
    {
        KnowledgeStatus.Verified => $"doğrulanmış ({e.ConfirmationCount} onay)",
        KnowledgeStatus.Confirmed => e.ConfirmationCount > 1 ? $"onaylı ({e.ConfirmationCount} onay)" : "onaylı",
        KnowledgeStatus.Disputed => "itiraz edildi",
        KnowledgeStatus.Superseded => "yenilendi",
        _ => "geri çekildi",
    };
}

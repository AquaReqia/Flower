using System.Diagnostics;
using Flower.Agents.Configuration;
using Flower.Agents.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flower.Agents.Knowledge;

/// <summary>
/// Tüm kaynakları paralel arar, sonuçları ağırlıklı Reciprocal Rank Fusion ile birleştirir,
/// tekilleştirir ve izinli space'lere göre filtreler. Bir kaynağın hatası diğerlerini durdurmaz.
/// </summary>
public sealed class HybridRetriever(
    IEnumerable<IKnowledgeSource> sources,
    IOptions<AgentOptions> options,
    ILogger<HybridRetriever> log)
{
    private readonly IReadOnlyList<IKnowledgeSource> _sources = sources.ToList();

    public async Task<RetrievalResult> RetrieveAsync(KnowledgeQuery query, CancellationToken ct)
    {
        using var activity = AgentTelemetry.Source.StartActivity("retrieve");
        var o = options.Value.Retrieval;

        var perSource = query with { TopK = o.PerSourceTopK };
        var tasks = _sources.Select(s => SearchSafeAsync(s, perSource, ct)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var failed = results.Where(r => r.Failed).Select(r => r.Source.Name).ToList();
        var fused = Fuse(results.Where(r => !r.Failed).Select(r => (r.Source.Weight, r.Hits)), o.RrfK);
        var filtered = FilterSpaces(fused, query.AllowedSpaces).Take(o.FinalTopK).ToList();

        activity?.SetTag("flower.retrieval.sources", _sources.Count);
        activity?.SetTag("flower.retrieval.failed", failed.Count);
        activity?.SetTag("flower.retrieval.hits", filtered.Count);
        return new RetrievalResult(filtered, failed, _sources.Count);
    }

    internal static IReadOnlyList<KnowledgeHit> Fuse(IEnumerable<(double Weight, IReadOnlyList<KnowledgeHit> Hits)> lists, int k)
    {
        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var best = new Dictionary<string, KnowledgeHit>(StringComparer.OrdinalIgnoreCase);
        var firstSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var (weight, hits) in lists)
        {
            for (var rank = 0; rank < hits.Count; rank++)
            {
                var hit = hits[rank];
                var key = hit.Key;
                scores[key] = scores.GetValueOrDefault(key) + (weight / (k + rank + 1));

                if (!best.TryGetValue(key, out var existing) || hit.Snippet.Length > existing.Snippet.Length)
                {
                    best[key] = hit;
                }

                firstSeen.TryAdd(key, order++);
            }
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => firstSeen[kv.Key])
            .Select(kv => best[kv.Key] with { Score = kv.Value })
            .ToList();
    }

    internal static IEnumerable<KnowledgeHit> FilterSpaces(IEnumerable<KnowledgeHit> hits, IReadOnlyList<string>? allowed)
    {
        if (allowed is not { Count: > 0 })
        {
            return hits;
        }

        var set = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        return hits.Where(h => h.SpaceKey is null || set.Contains(h.SpaceKey));
    }

    private async Task<(IKnowledgeSource Source, IReadOnlyList<KnowledgeHit> Hits, bool Failed)> SearchSafeAsync(
        IKnowledgeSource source, KnowledgeQuery query, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var hits = await source.SearchAsync(query, ct).ConfigureAwait(false);
            log.LogInformation("TIMING retrieve {Source} {Count} sonuç {ElapsedMs}ms", source.Name, hits.Count, sw.ElapsedMilliseconds);
            return (source, hits, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Bilgi kaynağı {Source} hata verdi ({ElapsedMs}ms)", source.Name, sw.ElapsedMilliseconds);
            return (source, [], true);
        }
    }
}

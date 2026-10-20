using Flower.Agents.Text;

namespace Flower.Agents.KnowledgeBase;

/// <summary>InMemory ve EF deposunun ortak puanlaması (docs/04 §4.3).</summary>
public static class KnowledgeScoring
{
    /// <summary>Embedding yokken kelime puanı alt sınırı.</summary>
    public const double MinKeywordScore = 0.34;

    /// <summary>Embedding yokken tekrar sayılma için kelime örtüşmesi alt sınırı.</summary>
    public const double DuplicateKeywordOverlap = 0.8;

    /// <summary>Aktif kayıtları puanlar, eler, sıralar ve <see cref="KnowledgeSearch.TopK"/> kadar döndürür.</summary>
    public static IReadOnlyList<(KnowledgeEntry Entry, double Score)> Rank(IEnumerable<KnowledgeEntry> entries, KnowledgeSearch search)
    {
        var keywords = search.Keywords.Count > 0 ? search.Keywords : KeywordExtractor.Extract(search.Question);
        return entries
            .Where(e => e.IsActive && IsAllowed(e, search.AllowedSpaces))
            .Select(e => (Entry: e, Score: Score(e, keywords, search.Embedding, search.MinSimilarity)))
            .Where(x => x.Score is not null)
            .Select(x => (x.Entry, Score: x.Score!.Value))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.Confidence)
            .Take(search.TopK)
            .ToList();
    }

    /// <summary>Elenirse null. Embedding varsa 0.7·cosine + 0.3·kelime; yoksa kelime puanı.</summary>
    public static double? Score(KnowledgeEntry entry, IReadOnlyCollection<string> keywords, float[]? embedding, double minSimilarity)
    {
        var keywordScore = keywords.Count > 0 ? KeywordExtractor.Overlap(keywords, SearchText(entry)) : 0;
        if (embedding is not null && entry.Embedding is { Length: > 0 } stored)
        {
            var cosine = VectorMath.Cosine(embedding, VectorMath.FromBytes(stored));
            return cosine < minSimilarity ? null : (0.7 * cosine) + (0.3 * keywordScore);
        }

        return keywordScore < MinKeywordScore ? null : keywordScore;
    }

    /// <summary>Yeni not mevcut kayıtla aynı bilgi mi? Embedding varsa cosine, yoksa normalize soru eşitliği veya kelime örtüşmesi.</summary>
    public static bool IsDuplicate(
        KnowledgeEntry entry, string canonicalQuestion, IReadOnlyCollection<string> keywords, float[]? embedding, double duplicateSimilarity)
    {
        if (embedding is not null && entry.Embedding is { Length: > 0 } stored)
        {
            return VectorMath.Cosine(embedding, VectorMath.FromBytes(stored)) >= duplicateSimilarity;
        }

        if (KeywordExtractor.Normalize(entry.CanonicalQuestion) == KeywordExtractor.Normalize(canonicalQuestion))
        {
            return true;
        }

        return keywords.Count > 0 && KeywordExtractor.Overlap(keywords, SearchText(entry)) >= DuplicateKeywordOverlap;
    }

    /// <summary>Kaydın türediği space'lerin tamamı izinli kümedeyse true (kısıtlı space bilgisi sızmasın).</summary>
    public static bool IsAllowed(KnowledgeEntry entry, IReadOnlyList<string>? allowedSpaces)
    {
        if (allowedSpaces is not { Count: > 0 })
        {
            return true;
        }

        var allowed = new HashSet<string>(allowedSpaces, StringComparer.OrdinalIgnoreCase);
        return KnowledgeEntry.SplitList(entry.SourceSpaces).All(allowed.Contains);
    }

    public static string SearchText(KnowledgeEntry e) =>
        string.Join(' ', e.Title, e.CanonicalQuestion, e.Aliases.Replace(KnowledgeEntry.Separator, ' '), e.Keywords.Replace(KnowledgeEntry.Separator, ' '));
}

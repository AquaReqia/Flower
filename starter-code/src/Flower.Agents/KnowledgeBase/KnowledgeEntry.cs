namespace Flower.Agents.KnowledgeBase;

/// <summary>Bilgi deposu kaydının durumu.</summary>
public enum KnowledgeStatus
{
    Confirmed,
    Verified,
    Disputed,
    Superseded,
    Retracted,
}

/// <summary>Doğrulanmış bilgi notu (tablo: <c>agents.KnowledgeEntries</c>). Liste alanları ';' ile ayrılır. Tarihler UTC.</summary>
public sealed class KnowledgeEntry
{
    public const char Separator = ';';

    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string CanonicalQuestion { get; set; } = "";
    public string Answer { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string Aliases { get; set; } = "";

    /// <summary>JSON: [{title,url,sourceName,spaceKey}].</summary>
    public string SourceRefs { get; set; } = "[]";

    /// <summary>Kaydın türediği space'ler (yetki filtresi).</summary>
    public string? SourceSpaces { get; set; }

    /// <summary><see cref="Text.VectorMath.ToBytes"/>; embedding kapalıysa null.</summary>
    public byte[]? Embedding { get; set; }

    public string? EmbeddingModel { get; set; }
    public KnowledgeStatus Status { get; set; }
    public double Confidence { get; set; }
    public int ConfirmationCount { get; set; }
    public int DisputeCount { get; set; }
    public string CreatedByEmail { get; set; } = "";

    /// <summary>Onaylayan e-postalar, tekrarsız.</summary>
    public string ConfirmedByEmails { get; set; } = "";

    public Guid? CreatedFromRunId { get; set; }
    public Guid? SupersededById { get; set; }
    public string? LastDisputeNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime LastConfirmedAt { get; set; }

    public bool IsActive => Status is KnowledgeStatus.Confirmed or KnowledgeStatus.Verified;

    public static string JoinList(IEnumerable<string> items) =>
        string.Join(Separator, items.Select(i => i.Replace(Separator, ' ').Trim()).Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Kaydın dayandığı kaynak (SourceRefs JSON elemanı).</summary>
public sealed record KnowledgeSourceRef(string Title, string? Url, string SourceName, string? SpaceKey);

/// <summary>Bilgi deposu araması.</summary>
public sealed record KnowledgeSearch(
    string Question,
    IReadOnlyList<string> Keywords,
    float[]? Embedding,
    int TopK,
    double MinSimilarity,
    IReadOnlyList<string>? AllowedSpaces);

/// <summary>Doğrulanmış bilgi deposu.</summary>
public interface IKnowledgeStore
{
    Task<KnowledgeEntry?> GetAsync(Guid id, CancellationToken ct);
    Task AddAsync(KnowledgeEntry entry, CancellationToken ct);
    Task UpdateAsync(KnowledgeEntry entry, CancellationToken ct);

    /// <summary>Yalnızca Confirmed/Verified kayıtlar; puanlama <see cref="KnowledgeScoring"/>.</summary>
    Task<IReadOnlyList<(KnowledgeEntry Entry, double Score)>> SearchAsync(KnowledgeSearch search, CancellationToken ct);
}

/// <summary>Onaylı bilgiyi başka sisteme yayınlar (Faz F: Confluence taslak sayfası).</summary>
public interface IKnowledgePublisher
{
    Task PublishAsync(KnowledgeEntry entry, CancellationToken ct);
}

/// <summary>Bu fazdaki yayıncı: hiçbir şey yapmaz.</summary>
public sealed class NoopKnowledgePublisher : IKnowledgePublisher
{
    public Task PublishAsync(KnowledgeEntry entry, CancellationToken ct) => Task.CompletedTask;
}

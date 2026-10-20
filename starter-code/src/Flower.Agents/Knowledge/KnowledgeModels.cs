using Flower.Agents.Abstractions;

namespace Flower.Agents.Knowledge;

public enum KnowledgeSourceKind
{
    /// <summary>Flower'ın doğrulanmış bilgi deposu (RAG yazma tarafı).</summary>
    KnowledgeBase,

    /// <summary>MCP tool'u üzerinden gelen kaynak (örn. Confluence).</summary>
    Mcp,
}

/// <summary>Bir bilgi kaynağından dönen tek sonuç.</summary>
public sealed record KnowledgeHit
{
    public required string SourceName { get; init; }
    public required KnowledgeSourceKind Kind { get; init; }
    public required string SourceId { get; init; }
    public required string Title { get; init; }
    public required string Snippet { get; init; }
    public string? Url { get; init; }
    public string? SpaceKey { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public PersonRef? Author { get; init; }
    public PersonRef? LastEditor { get; init; }

    /// <summary>Kaynak içi skor (kaynağa göre anlamı değişir; birleştirmede sıra kullanılır).</summary>
    public double Score { get; init; }

    /// <summary>Bilgi deposu kayıtları için güven (0–1).</summary>
    public double? Confidence { get; init; }

    /// <summary>Bilgi deposu kayıtları için durum etiketi (örn. "Onaylı", "Doğrulanmış").</summary>
    public string? StatusLabel { get; init; }

    /// <summary>Tekilleştirme anahtarı.</summary>
    public string Key => !string.IsNullOrWhiteSpace(Url) ? Url! : $"{SourceName}:{SourceId}";
}

/// <summary>Kaynaklara gönderilen sorgu.</summary>
public sealed record KnowledgeQuery(
    string Question,
    IReadOnlyList<string> SubQuestions,
    IReadOnlyList<string> Keywords,
    int TopK,
    IReadOnlyList<string>? AllowedSpaces = null);

/// <summary>Birleştirilmiş arama sonucu.</summary>
public sealed record RetrievalResult(
    IReadOnlyList<KnowledgeHit> Hits,
    IReadOnlyList<string> FailedSources,
    int SourceCount)
{
    /// <summary>En az bir kaynak varken hepsi hata verdiyse true (bu durumda "bilgi yok" denmemeli).</summary>
    public bool AllSourcesFailed => SourceCount > 0 && FailedSources.Count == SourceCount;
}

/// <summary>Arama yapılabilen bilgi kaynağı.</summary>
public interface IKnowledgeSource
{
    string Name { get; }

    /// <summary>Birleştirmede (RRF) kaynak ağırlığı.</summary>
    double Weight { get; }

    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(KnowledgeQuery query, CancellationToken ct);
}

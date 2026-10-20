namespace Flower.Agents.Agents;

/// <summary>Router agent'ının sınıflandırma sonucu.</summary>
public enum IntentKind
{
    Knowledge,
    SmallTalk,
    OutOfScope,
}

/// <summary>Router çıktısı.</summary>
public sealed class IntentResult
{
    public IntentKind Intent { get; init; } = IntentKind.Knowledge;
    public string Reason { get; init; } = "";
}

/// <summary>Planner çıktısı: alt sorular ve arama anahtar kelimeleri.</summary>
public sealed class QueryPlan
{
    public List<string> SubQuestions { get; init; } = [];

    /// <summary>Türkçe, kök/kısa hâl.</summary>
    public List<string> Keywords { get; init; } = [];

    /// <summary>Teknik terimlerin İngilizcesi.</summary>
    public List<string> EnglishKeywords { get; init; } = [];
}

/// <summary>Judge kararı seviyesi.</summary>
public enum SufficiencyLevel
{
    Sufficient,
    Partial,
    Insufficient,
}

/// <summary>Judge çıktısı: kaynakların soruyu cevaplamaya yetip yetmediği.</summary>
public sealed class SufficiencyVerdict
{
    public SufficiencyLevel Verdict { get; init; } = SufficiencyLevel.Insufficient;

    /// <summary>0–1.</summary>
    public double Confidence { get; init; }

    /// <summary>Gerekli kaynak numaraları ([1], [2] …).</summary>
    public List<int> UsedSources { get; init; } = [];

    /// <summary>Eksik bilgi maddeleri.</summary>
    public List<string> Missing { get; init; } = [];

    public string Reason { get; init; } = "";
}

/// <summary>Synthesizer çıktısı: cevap metni ve metinde atıf yapılan kaynak numaraları.</summary>
public sealed record AnswerDraft(string Text, IReadOnlyList<int> CitedSources)
{
    /// <summary>Prompt + completion token (model bildirmezse 0).</summary>
    public int Tokens { get; init; }
}

/// <summary>KnowledgeWriter çıktısı: bilgi deposuna yazılacak bağımsız not.</summary>
public sealed class KnowledgeNote
{
    /// <summary>Genel ve kalıcı kurum bilgisi mi?</summary>
    public bool ShouldStore { get; init; }

    /// <summary><see cref="ShouldStore"/> false ise neden.</summary>
    public string SkipReason { get; init; } = "";

    public string Title { get; init; } = "";
    public string CanonicalQuestion { get; init; } = "";

    /// <summary>Bağımsız okunabilir, kaynak numarasız.</summary>
    public string Answer { get; init; } = "";

    public List<string> Keywords { get; init; } = [];

    /// <summary>Sorunun farklı soruluş biçimleri.</summary>
    public List<string> Aliases { get; init; } = [];
}

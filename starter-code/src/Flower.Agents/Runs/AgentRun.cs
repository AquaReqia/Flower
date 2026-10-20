using System.Text.Json;
using Flower.Agents.Agents;
using Flower.Agents.Knowledge;
using Flower.Agents.Orchestration;
using Flower.Agents.Structured;

namespace Flower.Agents.Runs;

/// <summary>Kullanıcının cevaba verdiği geri bildirim.</summary>
public enum AgentFeedback
{
    None,
    Confirmed,
    Disputed,
}

/// <summary>Tek bir sorunun agent akışı kaydı (tablo: <c>agents.AgentRuns</c>). Tarihler UTC.</summary>
public sealed class AgentRun
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }

    /// <summary>Küçük harfe çevrilmiş e-posta.</summary>
    public string UserEmail { get; set; } = "";

    public string Question { get; set; } = "";
    public AgentOutcomeKind Kind { get; set; }
    public IntentKind? Intent { get; set; }
    public SufficiencyLevel? Verdict { get; set; }
    public double? Confidence { get; set; }

    /// <summary>Kaynak listesi ve ipucu hariç cevap gövdesi.</summary>
    public string? AnswerText { get; set; }

    /// <summary>JSON: atıf yapılan <see cref="KnowledgeHit"/> listesi (numaralarıyla).</summary>
    public string CitedSourcesJson { get; set; } = "[]";

    /// <summary>JSON: <see cref="AgentStep"/> listesi.</summary>
    public string StepsJson { get; set; } = "[]";

    public string? Error { get; set; }
    public AgentFeedback Feedback { get; set; }
    public string? FeedbackNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FeedbackAt { get; set; }

    public IReadOnlyList<CitedSource> GetCitedSources() =>
        JsonSerializer.Deserialize<List<CitedSource>>(CitedSourcesJson, RunJson.Options) ?? [];

    public void SetCitedSources(IEnumerable<CitedSource> sources) =>
        CitedSourcesJson = JsonSerializer.Serialize(sources, RunJson.Options);

    public IReadOnlyList<AgentStep> GetSteps() =>
        JsonSerializer.Deserialize<List<AgentStep>>(StepsJson, RunJson.Options) ?? [];

    public void SetSteps(IEnumerable<AgentStep> steps) =>
        StepsJson = JsonSerializer.Serialize(steps, RunJson.Options);
}

/// <summary>Akıştaki tek adım: ad, süre, token, fallback kullanıldı mı.</summary>
public sealed record AgentStep(string Name, long ElapsedMs, int Tokens = 0, bool IsFallback = false, string? Detail = null);

/// <summary>Cevapta atıf yapılan kaynak ve cevaptaki numarası.</summary>
public sealed record CitedSource(int Number, KnowledgeHit Hit);

internal static class RunJson
{
    public static JsonSerializerOptions Options => StructuredOutputInvoker.Json;
}

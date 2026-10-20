using Flower.Agents.Agents;
using Flower.Agents.Knowledge;

namespace Flower.Agents.Orchestration;

/// <summary>Orkestratör sonucu türü.</summary>
public enum AgentOutcomeKind
{
    Answered,
    PassThrough,
    OutOfScope,
    NotFound,
    Failed,

    /// <summary>Faz D.</summary>
    NeedsExpert,
}

/// <summary>Orkestratöre gelen soru. <see cref="RequestId"/> = Flower JobId.</summary>
public sealed record AgentRequest(
    Guid RequestId,
    string UserEmail,
    string? UserDisplayName,
    string Question,
    string? ConversationContext = null,
    IReadOnlyList<string>? AllowedSpaces = null);

/// <summary>Orkestratör sonucu. Gönderimi Flower yapar.</summary>
public sealed record AgentOutcome
{
    public required AgentOutcomeKind Kind { get; init; }

    /// <summary>Kullanıcıya gidecek düz metin (Flower TextToHtml uygular). PassThrough'da null.</summary>
    public string? Message { get; init; }

    /// <summary>Cevapta atıf yapılan kaynaklar.</summary>
    public IReadOnlyList<KnowledgeHit> Sources { get; init; } = [];

    public SufficiencyVerdict? Verdict { get; init; }
    public Guid? RunId { get; init; }
}

/// <summary>Deterministik agent akışı: Router → Planner → Retriever → Judge → Synthesizer.</summary>
public interface IAgentOrchestrator
{
    Task<AgentOutcome> HandleAsync(AgentRequest request, CancellationToken ct);
}

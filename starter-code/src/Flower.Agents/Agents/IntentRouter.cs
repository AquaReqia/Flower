using Flower.Agents.Abstractions;
using Flower.Agents.Prompts;
using Flower.Agents.Structured;

namespace Flower.Agents.Agents;

/// <summary>Agent adları (profil anahtarı, telemetri, test yönlendirmesi).</summary>
public static class AgentNames
{
    public const string Router = "Router";
    public const string Planner = "Planner";
    public const string Judge = "Judge";
    public const string Synthesizer = "Synthesizer";
    public const string KnowledgeWriter = "KnowledgeWriter";
}

/// <summary>Mesajı knowledge / small_talk / out_of_scope olarak sınıflandırır.</summary>
public interface IIntentRouter
{
    Task<StructuredResult<IntentResult>> ClassifyAsync(string question, string? conversationContext, CancellationToken ct);
}

/// <summary>Router agent'ı. Fallback: <see cref="IntentKind.Knowledge"/>.</summary>
public sealed class IntentRouter(StructuredOutputInvoker invoker, PromptLibrary prompts) : IIntentRouter
{
    public Task<StructuredResult<IntentResult>> ClassifyAsync(string question, string? conversationContext, CancellationToken ct)
    {
        var system = prompts.Render(PromptLibrary.Router, new Dictionary<string, string>
        {
            ["context"] = conversationContext ?? "",
        });

        return invoker.InvokeAsync(
            AgentNames.Router,
            system,
            question,
            () => new IntentResult { Intent = IntentKind.Knowledge, Reason = "fallback" },
            ModelPriority.Interactive,
            ct);
    }
}

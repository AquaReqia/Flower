using Flower.Agents.Abstractions;
using Flower.Agents.Prompts;
using Flower.Agents.Structured;
using Flower.Agents.Text;

namespace Flower.Agents.Agents;

/// <summary>Soru için alt sorular ve arama anahtar kelimeleri üretir.</summary>
public interface IQueryPlanner
{
    Task<StructuredResult<QueryPlan>> PlanAsync(string question, CancellationToken ct);
}

/// <summary>Planner agent'ı. Fallback: <see cref="KeywordExtractor"/> çıktısı.</summary>
public sealed class QueryPlanner(StructuredOutputInvoker invoker, PromptLibrary prompts) : IQueryPlanner
{
    public async Task<StructuredResult<QueryPlan>> PlanAsync(string question, CancellationToken ct)
    {
        var result = await invoker.InvokeAsync(
            AgentNames.Planner,
            prompts.Get(PromptLibrary.Planner),
            question,
            () => Fallback(question),
            ModelPriority.Interactive,
            ct).ConfigureAwait(false);

        // Model boş anahtar kelime dönerse arama yine yapılabilsin.
        return result.Value.Keywords.Count > 0 ? result : result with { Value = Fallback(question, result.Value) };
    }

    internal static QueryPlan Fallback(string question, QueryPlan? partial = null) => new()
    {
        SubQuestions = partial?.SubQuestions is { Count: > 0 } s ? s : [question],
        Keywords = [.. KeywordExtractor.Extract(question, 6)],
        EnglishKeywords = partial?.EnglishKeywords ?? [],
    };
}

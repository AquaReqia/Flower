using Flower.Agents.Abstractions;
using Flower.Agents.Configuration;
using Flower.Agents.Knowledge;
using Flower.Agents.Prompts;
using Flower.Agents.Structured;
using Microsoft.Extensions.Options;

namespace Flower.Agents.Agents;

/// <summary>Onaylanan cevabı bilgi deposuna yazılacak bağımsız bir nota çevirir.</summary>
public interface IKnowledgeWriter
{
    Task<StructuredResult<KnowledgeNote>> WriteAsync(
        string question,
        string confirmedAnswer,
        string? userNote,
        IReadOnlyList<KnowledgeHit> sources,
        CancellationToken ct);
}

/// <summary>KnowledgeWriter agent'ı (<see cref="ModelPriority.Background"/>). Fallback: kaydetme.</summary>
public sealed class KnowledgeWriter(StructuredOutputInvoker invoker, PromptLibrary prompts, IOptions<AgentOptions> options)
    : IKnowledgeWriter
{
    public Task<StructuredResult<KnowledgeNote>> WriteAsync(
        string question,
        string confirmedAnswer,
        string? userNote,
        IReadOnlyList<KnowledgeHit> sources,
        CancellationToken ct)
    {
        var user = prompts.Render(PromptLibrary.KnowledgeWriterUser, new Dictionary<string, string>
        {
            ["question"] = question,
            ["answer"] = confirmedAnswer,
            ["note"] = userNote ?? "",
            ["sources"] = ContextFormatter.FormatHits(sources, options.Value.Retrieval.MaxSnippetChars),
        });

        return invoker.InvokeAsync(
            AgentNames.KnowledgeWriter,
            prompts.Get(PromptLibrary.KnowledgeWriter),
            user,
            () => new KnowledgeNote { ShouldStore = false, SkipReason = "Model geçerli bir bilgi notu üretemedi." },
            ModelPriority.Background,
            ct);
    }
}

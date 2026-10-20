using Flower.Agents.Abstractions;
using Flower.Agents.Configuration;
using Flower.Agents.Knowledge;
using Flower.Agents.Prompts;
using Microsoft.Extensions.Options;

namespace Flower.Agents.Agents;

/// <summary>Yalnızca verilen kaynaklardan, [n] atıflı serbest metin cevap üretir.</summary>
public interface IAnswerSynthesizer
{
    Task<AnswerDraft> SynthesizeAsync(
        string question,
        IReadOnlyList<KnowledgeHit> hits,
        SufficiencyVerdict verdict,
        string? userName,
        string? conversationContext,
        CancellationToken ct);
}

/// <summary>Synthesizer agent'ı. Fallback yoktur; model hatası yukarı fırlatılır.</summary>
public sealed class AnswerSynthesizer(IChatModelClient client, PromptLibrary prompts, IOptions<AgentOptions> options)
    : IAnswerSynthesizer
{
    public async Task<AnswerDraft> SynthesizeAsync(
        string question,
        IReadOnlyList<KnowledgeHit> hits,
        SufficiencyVerdict verdict,
        string? userName,
        string? conversationContext,
        CancellationToken ct)
    {
        var o = options.Value;
        var partial = verdict.Verdict == SufficiencyLevel.Partial
            ? "Kaynaklar soruyu kısmen cevaplıyor. Cevaplayabildiğin kısmı yaz, ardından \"Eksik kalan:\" başlığıyla şu maddeleri belirt: "
                + (verdict.Missing.Count > 0 ? string.Join("; ", verdict.Missing) : "kaynaklarda bulunmayan kısımlar")
            : "";

        var system = prompts.Render(PromptLibrary.Synthesizer, new Dictionary<string, string>
        {
            ["partialInstruction"] = partial,
            ["userName"] = userName ?? "",
            ["context"] = conversationContext ?? "",
        });
        var user = prompts.Render(PromptLibrary.SynthesizerUser, new Dictionary<string, string>
        {
            ["question"] = question,
            ["sources"] = ContextFormatter.FormatHits(hits, o.Retrieval.MaxSnippetChars),
        });

        var profile = o.GetProfile(AgentNames.Synthesizer);
        var response = await client.CompleteAsync(
            new ChatRequest
            {
                AgentName = AgentNames.Synthesizer,
                Model = profile.Model,
                Messages = [ChatMessage.FromSystem(system), ChatMessage.FromUser(user)],
                Temperature = profile.Temperature,
                MaxTokens = profile.MaxTokens,
                Priority = ModelPriority.Interactive,
            },
            ct).ConfigureAwait(false);

        var text = response.Content.Trim();
        return new AnswerDraft(text, ContextFormatter.ExtractCitations(text, hits.Count))
        {
            Tokens = (response.PromptTokens ?? 0) + (response.CompletionTokens ?? 0),
        };
    }
}

using System.Collections.Concurrent;
using System.Reflection;
using Flower.Agents.Configuration;
using Microsoft.Extensions.Options;

namespace Flower.Agents.Prompts;

/// <summary>
/// Prompt şablonları. Önce <see cref="AgentOptions.PromptOverrideDirectory"/> altındaki <c>{ad}.md</c>,
/// yoksa DLL'e gömülü şablon kullanılır. <c>{{anahtar}}</c> yer tutucuları <see cref="Render"/> ile doldurulur.
/// </summary>
public sealed class PromptLibrary(IOptions<AgentOptions> options)
{
    public const string Router = "router";
    public const string Planner = "planner";
    public const string Judge = "judge";
    public const string JudgeUser = "judge-user";
    public const string Synthesizer = "synthesizer";
    public const string SynthesizerUser = "synthesizer-user";
    public const string KnowledgeWriter = "knowledge-writer";
    public const string KnowledgeWriterUser = "knowledge-writer-user";

    private readonly ConcurrentDictionary<string, string> _embedded = new(StringComparer.OrdinalIgnoreCase);

    public string Get(string name)
    {
        var dir = options.Value.PromptOverrideDirectory;
        if (!string.IsNullOrWhiteSpace(dir))
        {
            var path = Path.Combine(dir, name + ".md");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return _embedded.GetOrAdd(name, LoadEmbedded);
    }

    public string Render(string name, IReadOnlyDictionary<string, string> values)
    {
        var text = Get(name);
        foreach (var (key, value) in values)
        {
            text = text.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        }

        return text;
    }

    private static string LoadEmbedded(string name)
    {
        var asm = typeof(PromptLibrary).Assembly;
        var resource = $"Flower.Agents.Prompts.{name}.md";
        using var stream = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Prompt şablonu bulunamadı: {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static IReadOnlyList<string> EmbeddedNames() =>
        typeof(PromptLibrary).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Flower.Agents.Prompts.", StringComparison.Ordinal))
            .ToList();

    internal static Assembly Assembly => typeof(PromptLibrary).Assembly;
}

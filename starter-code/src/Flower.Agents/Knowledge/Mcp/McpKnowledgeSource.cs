using Flower.Agents.Abstractions;
using Flower.Agents.Configuration;
using Flower.Agents.Text;

namespace Flower.Agents.Knowledge.Mcp;

/// <summary>
/// Bir MCP arama tool'unu bilgi kaynağı olarak kullanır (örn. Confluence MCP server'ınız).
/// Tool sonucu <see cref="McpResultParser"/> ile okunur; JSON dizi/nesne veya düz metin desteklenir.
/// </summary>
public sealed class McpKnowledgeSource(McpSourceOptions source, IMcpToolCaller caller, RetrievalOptions retrieval) : IKnowledgeSource
{
    public string Name => source.Name;

    public double Weight => source.Weight;

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(KnowledgeQuery query, CancellationToken ct)
    {
        var args = BuildArguments(query);
        if (args is null)
        {
            return [];
        }

        var raw = await caller.CallToolAsync(source.Server, source.ToolName, args, ct).ConfigureAwait(false);
        var hits = McpResultParser.Parse(raw, source.Name);

        return hits
            .Take(query.TopK)
            .Select(h => h with { Snippet = SnippetSelector.Select(h.Snippet, query.Keywords.ToList(), retrieval.MaxSnippetChars) })
            .ToList();
    }

    internal Dictionary<string, object?>? BuildArguments(KnowledgeQuery query)
    {
        var spaces = query.AllowedSpaces is { Count: > 0 } ? query.AllowedSpaces : source.Spaces;
        var keywords = query.Keywords.Count > 0 ? query.Keywords : KeywordExtractor.Extract(query.Question);

        string? q = source.QueryMode switch
        {
            McpQueryMode.Question => query.Question,
            McpQueryMode.Cql => CqlBuilder.Build(keywords, spaces),
            _ => keywords.Count > 0 ? string.Join(' ', keywords) : query.Question,
        };

        if (string.IsNullOrWhiteSpace(q))
        {
            return null;
        }

        var args = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in source.StaticArguments)
        {
            args[k] = v;
        }

        args[source.QueryArgument] = q;
        if (!string.IsNullOrWhiteSpace(source.LimitArgument))
        {
            args[source.LimitArgument] = query.TopK;
        }

        if (!string.IsNullOrWhiteSpace(source.SpacesArgument) && spaces is { Count: > 0 } && source.QueryMode != McpQueryMode.Cql)
        {
            args[source.SpacesArgument] = spaces.ToArray();
        }

        return args;
    }
}

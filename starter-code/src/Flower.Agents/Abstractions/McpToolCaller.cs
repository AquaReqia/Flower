namespace Flower.Agents.Abstractions;

/// <summary>
/// MCP tool çağrısı portu. Flower.Agents MCP server'ı bilmez; Flower.Core bu arayüzü
/// resmi C# MCP SDK'sı (ModelContextProtocol) ile implemente eder.
/// </summary>
public interface IMcpToolCaller
{
    /// <summary>
    /// Tool'u çağırır ve sonucun metin içeriğini döndürür (tüm text content parçaları birleşik).
    /// Tool hata döndürürse <see cref="McpToolException"/> fırlatılmalı.
    /// </summary>
    /// <param name="server">Birden fazla MCP server varsa anahtar; tek server için null.</param>
    Task<string> CallToolAsync(string? server, string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct);
}

/// <summary>MCP tool hata sonucu (<c>isError: true</c>) veya bağlantı hatası.</summary>
public sealed class McpToolException(string toolName, string message, Exception? inner = null)
    : Exception($"MCP tool '{toolName}': {message}", inner)
{
    public string ToolName { get; } = toolName;
}

/// <summary>Hiç MCP kaynağı yapılandırılmamışsa kullanılan varsayılan; çağrılırsa açık bir hata verir.</summary>
public sealed class MissingMcpToolCaller : IMcpToolCaller
{
    public Task<string> CallToolAsync(string? server, string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct) =>
        throw new McpToolException(toolName, "IMcpToolCaller kayıtlı değil. Flower.Core'da bir implementasyon register edin.");
}

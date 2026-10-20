using System.Text.Json.Nodes;

namespace Flower.Agents.Abstractions;

/// <summary>Sohbet mesajı rolü.</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
}

/// <summary>Model çağrısının önceliği. Reaktif (kullanıcı bekliyor) işler asla arka plan işlerini beklemez.</summary>
public enum ModelPriority
{
    Interactive,
    Background,
}

/// <summary>Tek bir sohbet mesajı.</summary>
public sealed record ChatMessage(ChatRole Role, string Content)
{
    public static ChatMessage FromSystem(string content) => new(ChatRole.System, content);
    public static ChatMessage FromUser(string content) => new(ChatRole.User, content);
    public static ChatMessage FromAssistant(string content) => new(ChatRole.Assistant, content);
}

/// <summary>OpenAI uyumlu <c>response_format: json_schema</c> tanımı.</summary>
public sealed record JsonSchemaFormat(string Name, JsonNode Schema);

/// <summary>Model çağrısı isteği. <see cref="AgentName"/> telemetri ve test yönlendirmesi için zorunludur.</summary>
public sealed record ChatRequest
{
    public required string AgentName { get; init; }
    public required string Model { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
    public JsonSchemaFormat? ResponseSchema { get; init; }
    public ModelPriority Priority { get; init; } = ModelPriority.Interactive;
}

/// <summary>Model çağrısı sonucu.</summary>
public sealed record ChatResponse(
    string Content,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    string? FinishReason = null);

/// <summary>OpenAI uyumlu chat completion istemcisi (vLLM / Bifrost).</summary>
public interface IChatModelClient
{
    Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct);
}

/// <summary>Embedding istemcisi (<c>/v1/embeddings</c>).</summary>
public interface IEmbeddingClient
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct);
}

/// <summary>Model API hata yanıtı.</summary>
public sealed class ModelApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

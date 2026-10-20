using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flower.Agents.Abstractions;
using Flower.Agents.Configuration;
using Flower.Agents.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flower.Agents.ModelApi;

/// <summary>
/// OpenAI uyumlu <c>/chat/completions</c> istemcisi (vLLM, Bifrost). Streaming kullanmaz.
/// HttpClient <c>BaseAddress</c> değeri <c>.../v1/</c> olmalı (DI kaydı bunu ayarlar).
/// </summary>
public sealed partial class OpenAiCompatibleChatClient(
    HttpClient http,
    IOptions<AgentOptions> options,
    ModelCallScheduler scheduler,
    ILogger<OpenAiCompatibleChatClient> log) : IChatModelClient
{
    private readonly ModelEndpointOptions _endpoint = options.Value.Model;

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct) =>
        scheduler.RunAsync(request.Priority, token => SendAsync(request, token), ct);

    private async Task<ChatResponse> SendAsync(ChatRequest request, CancellationToken ct)
    {
        using var activity = AgentTelemetry.Source.StartActivity($"llm {request.AgentName}", ActivityKind.Client);
        activity?.SetTag("gen_ai.operation.name", "chat");
        activity?.SetTag("gen_ai.request.model", request.Model);
        activity?.SetTag("flower.agent", request.AgentName);
        activity?.SetTag("flower.priority", request.Priority.ToString());

        var body = BuildBody(request, _endpoint.UseJsonSchemaResponseFormat);
        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        ApplyHeaders(message, _endpoint);

        var sw = Stopwatch.StartNew();
        using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
            log.LogWarning("TIMING llm {Agent} {Model} HTTP {Status} {ElapsedMs}ms", request.AgentName, request.Model, (int)response.StatusCode, sw.ElapsedMilliseconds);
            throw new ModelApiException((int)response.StatusCode, $"Model API {(int)response.StatusCode}: {Truncate(raw, 500)}");
        }

        var result = ParseResponse(raw, _endpoint.StripThinkBlocks);
        activity?.SetTag("gen_ai.usage.input_tokens", result.PromptTokens);
        activity?.SetTag("gen_ai.usage.output_tokens", result.CompletionTokens);
        log.LogInformation(
            "TIMING llm {Agent} {Model} {ElapsedMs}ms in={In} out={Out}",
            request.AgentName, request.Model, sw.ElapsedMilliseconds, result.PromptTokens, result.CompletionTokens);
        return result;
    }

    internal static JsonObject BuildBody(ChatRequest request, bool useJsonSchema)
    {
        var messages = new JsonArray();
        foreach (var m in request.Messages)
        {
            messages.Add(new JsonObject
            {
                ["role"] = m.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.Assistant => "assistant",
                    _ => "user",
                },
                ["content"] = m.Content,
            });
        }

        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = false,
        };

        if (request.Temperature is { } t)
        {
            body["temperature"] = t;
        }

        if (request.MaxTokens is { } max)
        {
            body["max_tokens"] = max;
        }

        if (useJsonSchema && request.ResponseSchema is { } schema)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = schema.Name,
                    ["schema"] = schema.Schema.DeepClone(),
                },
            };
        }

        return body;
    }

    internal static ChatResponse ParseResponse(string raw, bool stripThink)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        string content = "";
        string? finish = null;
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var c) &&
                c.ValueKind == JsonValueKind.String)
            {
                content = c.GetString() ?? "";
            }

            if (first.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String)
            {
                finish = f.GetString();
            }
        }

        int? prompt = null, completion = null;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv))
            {
                prompt = pv;
            }

            if (usage.TryGetProperty("completion_tokens", out var co) && co.TryGetInt32(out var cv))
            {
                completion = cv;
            }
        }

        if (stripThink)
        {
            content = StripThink(content);
        }

        return new ChatResponse(content.Trim(), prompt, completion, finish);
    }

    /// <summary>Reasoning bloklarını temizler. Açılış etiketi kesilmişse son kapanıştan sonrasını alır.</summary>
    internal static string StripThink(string content)
    {
        var cleaned = ThinkBlock().Replace(content, "");
        var close = cleaned.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        return close >= 0 ? cleaned[(close + "</think>".Length)..] : cleaned;
    }

    internal static void ApplyHeaders(HttpRequestMessage message, ModelEndpointOptions endpoint)
    {
        if (!string.IsNullOrWhiteSpace(endpoint.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
        }

        foreach (var (name, value) in endpoint.ExtraHeaders)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();
}

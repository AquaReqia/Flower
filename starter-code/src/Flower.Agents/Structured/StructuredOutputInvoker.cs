using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using Flower.Agents.Abstractions;
using Flower.Agents.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flower.Agents.Structured;

/// <summary>Yapılandırılmış çağrı sonucu. <see cref="IsFallback"/> true ise model geçerli JSON üretememiştir.</summary>
/// <see cref="Tokens"/>: tüm denemelerin toplam prompt + completion token sayısı (model bildirmezse 0).
public sealed record StructuredResult<T>(T Value, bool IsFallback, string? Raw, int Tokens = 0);

/// <summary>
/// Modeli JSON şemasına uygun çıktı üretmeye zorlar:
/// şema hem <c>response_format</c> ile hem system prompt'a yazılır, çıktı ayrıştırılır,
/// geçersizse bir kez düzeltme istenir, yine olmazsa güvenli varsayılan döner.
/// Ağ/HTTP hataları varsayılana çevrilmez, yukarı fırlatılır (model kapalıyken uzmanlara soru gitmesin).
/// </summary>
public sealed class StructuredOutputInvoker(
    IChatModelClient client,
    IOptions<AgentOptions> options,
    ILogger<StructuredOutputInvoker> log)
{
    internal static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private static readonly ConcurrentDictionary<Type, JsonNode> SchemaCache = new();

    public async Task<StructuredResult<T>> InvokeAsync<T>(
        string agentName,
        string systemPrompt,
        string userContent,
        Func<T> fallback,
        ModelPriority priority,
        CancellationToken ct)
        where T : class
    {
        var profile = options.Value.GetProfile(agentName);
        var schema = GetSchema<T>();
        var system = systemPrompt.TrimEnd()
            + "\n\n## Çıktı biçimi\nYalnızca aşağıdaki JSON şemasına uyan TEK bir JSON nesnesi döndür. Açıklama, markdown veya kod bloğu ekleme.\n"
            + schema.ToJsonString();

        var messages = new List<ChatMessage> { ChatMessage.FromSystem(system), ChatMessage.FromUser(userContent) };
        string? lastRaw = null;
        var tokens = 0;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await client.CompleteAsync(
                new ChatRequest
                {
                    AgentName = agentName,
                    Model = profile.Model,
                    Messages = messages,
                    Temperature = profile.Temperature,
                    MaxTokens = profile.MaxTokens,
                    ResponseSchema = new JsonSchemaFormat(typeof(T).Name, schema),
                    Priority = priority,
                },
                ct).ConfigureAwait(false);

            lastRaw = response.Content;
            tokens += (response.PromptTokens ?? 0) + (response.CompletionTokens ?? 0);
            if (TryParse<T>(response.Content, out var value, out var error))
            {
                return new StructuredResult<T>(value, false, lastRaw, tokens);
            }

            log.LogWarning("{Agent} geçersiz JSON üretti (deneme {Attempt}): {Error}", agentName, attempt, error);
            messages.Add(ChatMessage.FromAssistant(response.Content));
            messages.Add(ChatMessage.FromUser($"Çıktın şemaya uymuyor ({error}). Yalnızca şemaya uyan JSON nesnesini döndür."));
        }

        log.LogWarning("{Agent} için güvenli varsayılan kullanıldı.", agentName);
        return new StructuredResult<T>(fallback(), true, lastRaw, tokens);
    }

    internal static JsonNode GetSchema<T>() =>
        SchemaCache.GetOrAdd(typeof(T), t => Json.GetJsonSchemaAsNode(t, new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
        }));

    internal static bool TryParse<T>(string content, out T value, out string error)
        where T : class
    {
        value = null!;
        var json = ExtractJsonObject(content);
        if (json is null)
        {
            error = "JSON nesnesi bulunamadı";
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<T>(json, Json);
            if (parsed is null)
            {
                error = "boş sonuç";
                return false;
            }

            value = parsed;
            error = "";
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Kod bloğu veya ön/son metin içindeki ilk dengeli JSON nesnesini çıkarır.</summary>
    internal static string? ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < content.Length; i++)
        {
            var c = content[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return content[start..(i + 1)];
                    }

                    break;
            }
        }

        return null;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        o.MakeReadOnly(populateMissingResolver: true);
        return o;
    }
}

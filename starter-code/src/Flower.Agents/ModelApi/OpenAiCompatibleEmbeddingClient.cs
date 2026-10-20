using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flower.Agents.Abstractions;
using Flower.Agents.Configuration;
using Microsoft.Extensions.Options;

namespace Flower.Agents.ModelApi;

/// <summary>OpenAI uyumlu <c>/embeddings</c> istemcisi. Girdileri <see cref="EmbeddingOptions.BatchSize"/> kadar gruplar.</summary>
public sealed class OpenAiCompatibleEmbeddingClient(HttpClient http, IOptions<AgentOptions> options) : IEmbeddingClient
{
    private readonly AgentOptions _options = options.Value;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var model = _options.Embedding.Model
            ?? throw new InvalidOperationException($"{AgentOptions.SectionName}:Embedding:Model tanımlı değil.");

        var result = new List<float[]>(inputs.Count);
        var batch = Math.Max(1, _options.Embedding.BatchSize);
        for (var i = 0; i < inputs.Count; i += batch)
        {
            var slice = inputs.Skip(i).Take(batch).ToArray();
            result.AddRange(await EmbedBatchAsync(model, slice, ct).ConfigureAwait(false));
        }

        return result;
    }

    private async Task<float[][]> EmbedBatchAsync(string model, string[] inputs, CancellationToken ct)
    {
        var input = new JsonArray();
        foreach (var s in inputs)
        {
            input.Add(s);
        }

        var body = new JsonObject { ["model"] = model, ["input"] = input };
        using var message = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        OpenAiCompatibleChatClient.ApplyHeaders(message, _options.Model);

        using var response = await http.SendAsync(message, ct).ConfigureAwait(false);
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ModelApiException((int)response.StatusCode, $"Embedding API {(int)response.StatusCode}: {raw[..Math.Min(raw.Length, 500)]}");
        }

        return ParseResponse(raw, inputs.Length);
    }

    internal static float[][] ParseResponse(string raw, int expected)
    {
        using var doc = JsonDocument.Parse(raw);
        var vectors = new float[expected][];
        var position = 0;
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var index = item.TryGetProperty("index", out var ix) && ix.TryGetInt32(out var v) ? v : position;
            var emb = item.GetProperty("embedding");
            var vec = new float[emb.GetArrayLength()];
            var j = 0;
            foreach (var x in emb.EnumerateArray())
            {
                vec[j++] = x.GetSingle();
            }

            if (index >= 0 && index < expected)
            {
                vectors[index] = vec;
            }

            position++;
        }

        for (var i = 0; i < vectors.Length; i++)
        {
            if (vectors[i] is null)
            {
                throw new ModelApiException(200, $"Embedding yanıtında {i}. girdi eksik.");
            }
        }

        return vectors;
    }
}

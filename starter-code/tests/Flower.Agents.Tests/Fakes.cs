using System.Collections.Concurrent;
using Flower.Agents.Abstractions;

namespace Flower.Agents.Tests;

/// <summary>AgentName'e göre sıradaki cevabı döndürür; kuyruk tek eleman kaldıysa onu tekrar kullanır.</summary>
public sealed class FakeChatModelClient : IChatModelClient
{
    private readonly ConcurrentDictionary<string, Queue<Func<ChatRequest, string>>> _responses = new(StringComparer.OrdinalIgnoreCase);

    public List<ChatRequest> Calls { get; } = [];

    public FakeChatModelClient On(string agentName, params string[] responses)
    {
        var q = _responses.GetOrAdd(agentName, _ => new Queue<Func<ChatRequest, string>>());
        foreach (var r in responses)
        {
            q.Enqueue(_ => r);
        }

        return this;
    }

    public FakeChatModelClient Throw(string agentName, Exception ex)
    {
        _responses.GetOrAdd(agentName, _ => new Queue<Func<ChatRequest, string>>()).Enqueue(_ => throw ex);
        return this;
    }

    public void Reset(string agentName) => _responses.TryRemove(agentName, out _);

    public int CallCount(string agentName) => Calls.Count(c => c.AgentName == agentName);

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (Calls)
        {
            Calls.Add(request);
        }

        if (!_responses.TryGetValue(request.AgentName, out var q) || q.Count == 0)
        {
            throw new InvalidOperationException($"FakeChatModelClient: '{request.AgentName}' için cevap tanımlı değil.");
        }

        var next = q.Count > 1 ? q.Dequeue() : q.Peek();
        return Task.FromResult(new ChatResponse(next(request), 10, 5));
    }
}

/// <summary>Tool çağrılarını kaydeder; <see cref="Handler"/> ile cevap veya hata üretir.</summary>
public sealed class FakeMcpToolCaller : IMcpToolCaller
{
    public Func<string, IReadOnlyDictionary<string, object?>, string> Handler { get; set; } = (_, _) => """{"results":[]}""";

    public int Calls { get; private set; }

    public Task<string> CallToolAsync(string? server, string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(Handler(toolName, arguments));
    }
}

public sealed class FakeTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

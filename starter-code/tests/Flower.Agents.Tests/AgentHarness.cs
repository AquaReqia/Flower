using Flower.Agents.Abstractions;
using Flower.Agents.Agents;
using Flower.Agents.DependencyInjection;
using Flower.Agents.Feedback;
using Flower.Agents.KnowledgeBase;
using Flower.Agents.Orchestration;
using Flower.Agents.Runs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flower.Agents.Tests;

/// <summary>Gerçek DI kaydı (<see cref="ServiceCollectionExtensions.AddFlowerAgents"/>) + sahte model/MCP.</summary>
public sealed class AgentHarness : IDisposable
{
    public const string ConfluenceHit = """
        {"results":[{"id":"1","title":"Fatura Modülü Sorumluları","url":"https://conf/pages/1","spaceKey":"FIN",
          "excerpt":"Fatura modülünün sahibi Finans BT ekibidir.","lastModified":"2026-03-12T10:15:00Z"}]}
        """;

    public const string KnowledgeIntent = """{"intent":"knowledge","reason":""}""";
    public const string Plan = """{"subQuestions":["Fatura modülünün sahibi kim?"],"keywords":["fatura","sahip"],"englishKeywords":["invoice"]}""";
    public const string Sufficient = """{"verdict":"sufficient","confidence":0.9,"usedSources":[1],"missing":[],"reason":""}""";
    public const string Answer = "Fatura modülünün sahibi Finans BT ekibidir [1].";

    public const string Note = """
        {"shouldStore":true,"skipReason":"","title":"Fatura modülünün sahibi","canonicalQuestion":"Fatura modülünün sahibi kim?",
         "answer":"Fatura modülünün sahibi Finans BT ekibidir.","keywords":["fatura","modül","sahip"],"aliases":["Faturayı kim yönetiyor?"]}
        """;

    private readonly ServiceProvider _provider;

    public AgentHarness(Dictionary<string, string?>? overrides = null, Action<IServiceCollection>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Flower:Agents:Enabled"] = "true",
            ["Flower:Agents:DefaultProfile:Model"] = "test-model",
            ["Flower:Agents:McpSources:0:Name"] = "confluence",
            ["Flower:Agents:McpSources:0:ToolName"] = "confluence_search",
        };
        foreach (var (k, v) in overrides ?? [])
        {
            settings[k] = v;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<IMcpToolCaller>(Mcp);
        services.AddFlowerAgents(configuration);
        services.AddSingleton<IChatModelClient>(Model);
        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();

        Mcp.Handler = (_, _) => ConfluenceHit;
    }

    public FakeChatModelClient Model { get; } = new();

    public FakeMcpToolCaller Mcp { get; } = new();

    public FakeTime Time { get; } = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));

    public InMemoryAgentRunStore Runs => (InMemoryAgentRunStore)_provider.GetRequiredService<IAgentRunStore>();

    public InMemoryKnowledgeStore Knowledge => (InMemoryKnowledgeStore)_provider.GetRequiredService<IKnowledgeStore>();

    /// <summary>Tam başarılı akış için tüm agent cevaplarını ayarlar.</summary>
    public AgentHarness WithHappyPath()
    {
        Model.On(AgentNames.Router, KnowledgeIntent)
            .On(AgentNames.Planner, Plan)
            .On(AgentNames.Judge, Sufficient)
            .On(AgentNames.Synthesizer, Answer)
            .On(AgentNames.KnowledgeWriter, Note);
        return this;
    }

    public async Task<AgentOutcome> AskAsync(string question, string user = "ayse@kurum.com", CancellationToken ct = default)
    {
        using var scope = _provider.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IAgentOrchestrator>();
        return await orchestrator.HandleAsync(new AgentRequest(Guid.NewGuid(), user, "Ayşe", question), ct);
    }

    public async Task<FeedbackResult> FeedbackAsync(Func<IAgentFeedbackService, Task<FeedbackResult>> action)
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAgentFeedbackService>());
    }

    /// <summary>Yeni scope'ta servis üzerinde işlem (EF testleri için).</summary>
    public async Task<T> InScopeAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        using var scope = _provider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public IServiceProvider Services => _provider;

    public void Dispose() => _provider.Dispose();
}

using Flower.Agents.Abstractions;
using Flower.Agents.Agents;
using Flower.Agents.Configuration;
using Flower.Agents.Feedback;
using Flower.Agents.Knowledge;
using Flower.Agents.Knowledge.Mcp;
using Flower.Agents.KnowledgeBase;
using Flower.Agents.ModelApi;
using Flower.Agents.Orchestration;
using Flower.Agents.Persistence;
using Flower.Agents.Prompts;
using Flower.Agents.Runs;
using Flower.Agents.Structured;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flower.Agents.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Agent katmanını kaydeder. Varsayılanlar <c>TryAdd</c> ile eklenir; Flower.Core kendi
    /// <see cref="IMcpToolCaller"/>, <see cref="IOrgDirectory"/> ve EF store'larını önce veya sonra kaydedebilir.
    /// <paramref name="configureHttp"/> model/embedding HttpClient pipeline'ına (proxy, debug handler) eklenir.
    /// </summary>
    public static IServiceCollection AddFlowerAgents(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IHttpClientBuilder>? configureHttp = null)
    {
        var section = configuration.GetSection(AgentOptions.SectionName);
        services.AddOptions<AgentOptions>()
            .Bind(section)
            .PostConfigure(o => ApplyModelFallback(o, configuration));

        // Kaynak listesi kayıt anında bilinmeli (her MCP kaynağı ayrı IKnowledgeSource).
        var snapshot = section.Get<AgentOptions>() ?? new AgentOptions();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ModelCallScheduler>();
        services.TryAddSingleton<PromptLibrary>();
        services.TryAddSingleton<IAgentRunStore, InMemoryAgentRunStore>();
        services.TryAddSingleton<IKnowledgeStore, InMemoryKnowledgeStore>();
        services.TryAddSingleton<IOrgDirectory, NullOrgDirectory>();
        services.TryAddSingleton<IMcpToolCaller, MissingMcpToolCaller>();
        services.TryAddSingleton<IKnowledgePublisher, NoopKnowledgePublisher>();

        var chat = services.AddHttpClient<IChatModelClient, OpenAiCompatibleChatClient>((sp, http) =>
            ConfigureHttp(http, sp.GetRequiredService<IOptions<AgentOptions>>().Value.Model.BaseUrl, sp));
        configureHttp?.Invoke(chat);

        if (snapshot.Embedding.Enabled)
        {
            var embedding = services.AddHttpClient<IEmbeddingClient, OpenAiCompatibleEmbeddingClient>((sp, http) =>
            {
                var o = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
                ConfigureHttp(http, string.IsNullOrWhiteSpace(o.Embedding.BaseUrl) ? o.Model.BaseUrl : o.Embedding.BaseUrl, sp);
            });
            configureHttp?.Invoke(embedding);
        }

        services.TryAddScoped<StructuredOutputInvoker>();
        services.TryAddScoped<IIntentRouter, IntentRouter>();
        services.TryAddScoped<IQueryPlanner, QueryPlanner>();
        services.TryAddScoped<ISufficiencyJudge, SufficiencyJudge>();
        services.TryAddScoped<IAnswerSynthesizer, AnswerSynthesizer>();
        services.TryAddScoped<IKnowledgeWriter, KnowledgeWriter>();
        services.TryAddScoped<HybridRetriever>();
        services.TryAddScoped<IAgentOrchestrator, AgentOrchestrator>();
        services.TryAddScoped<KnowledgeCurator>();
        services.TryAddScoped<IAgentFeedbackService, AgentFeedbackService>();

        if (snapshot.KnowledgeBase.Enabled)
        {
            services.AddScoped<IKnowledgeSource, KnowledgeBaseSource>();
        }

        foreach (var source in snapshot.McpSources.Where(s => s.Enabled))
        {
            services.AddScoped<IKnowledgeSource>(sp => new McpKnowledgeSource(
                source,
                sp.GetRequiredService<IMcpToolCaller>(),
                sp.GetRequiredService<IOptions<AgentOptions>>().Value.Retrieval));
        }

        return services;
    }

    /// <summary>
    /// <see cref="AgentsDbContext"/> (PostgreSQL, <c>agents</c> şeması) ve EF store'larını kaydeder;
    /// InMemory store'ların yerine geçer (<see cref="AddFlowerAgents"/>'tan önce veya sonra çağrılabilir).
    /// </summary>
    public static IServiceCollection AddFlowerAgentsPersistence(this IServiceCollection services, string connectionString) =>
        services.AddFlowerAgentsPersistence(o => o.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AgentsDbContext.Schema)));

    /// <summary>Sağlayıcıyı çağıran seçer (testte SQLite).</summary>
    public static IServiceCollection AddFlowerAgentsPersistence(this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<AgentsDbContext>(configure);
        services.Replace(ServiceDescriptor.Scoped<IKnowledgeStore, EfKnowledgeStore>());
        services.Replace(ServiceDescriptor.Scoped<IAgentRunStore, EfAgentRunStore>());
        return services;
    }

    /// <summary><c>Flower:Agents:Model</c> boşsa <c>Flower:Model</c> değerlerine düşer (docs/06).</summary>
    internal static void ApplyModelFallback(AgentOptions o, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(o.Model.BaseUrl))
        {
            o.Model.BaseUrl = configuration["Flower:Model:BaseUrl"] ?? "";
        }

        if (string.IsNullOrWhiteSpace(o.Model.ApiKey))
        {
            o.Model.ApiKey = configuration["Flower:Model:ApiKey"];
        }
    }

    private static void ConfigureHttp(HttpClient http, string baseUrl, IServiceProvider sp)
    {
        var o = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        }

        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, o.Model.TimeoutSeconds));
    }
}

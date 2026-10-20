using Flower.Agents.DependencyInjection;
using Flower.Agents.Knowledge;
using Flower.Agents.KnowledgeBase;
using Flower.Agents.Orchestration;
using Flower.Agents.Persistence;
using Flower.Agents.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flower.Agents.Tests;

public sealed class PersistenceTests : IDisposable
{
    private const string Question = "Fatura modülünün sahibi kim?";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public PersistenceTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private AgentHarness CreateHarness()
    {
        var h = new AgentHarness(configure: s => s.AddFlowerAgentsPersistence(o => o.UseSqlite(_connection))).WithHappyPath();
        using var scope = h.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentsDbContext>().Database.EnsureCreated();
        return h;
    }

    [Fact]
    public async Task Ef_DogruSonrasiBilgiDeposuIlkKaynak_YanlisSonrasiCikmaz()
    {
        using var h = CreateHarness();
        Assert.IsType<EfAgentRunStore>(h.Services.CreateScope().ServiceProvider.GetRequiredService<IAgentRunStore>());

        await h.AskAsync(Question);
        var saved = await h.FeedbackAsync(f => f.ConfirmAsync("ayse@kurum.com", null, default));
        Assert.Contains("Kaydettim", saved.Message);

        h.Time.Advance(TimeSpan.FromMinutes(5));
        var second = await h.AskAsync(Question, "mehmet@kurum.com");
        Assert.Equal(KnowledgeSourceKind.KnowledgeBase, second.Sources[0].Kind);

        await h.FeedbackAsync(f => f.DisputeAsync("mehmet@kurum.com", "eski bilgi", default));

        var entries = await h.InScopeAsync<AgentsDbContext, List<KnowledgeEntry>>(db => db.KnowledgeEntries.ToListAsync());
        Assert.Equal(KnowledgeStatus.Disputed, Assert.Single(entries).Status);

        var runs = await h.InScopeAsync<AgentsDbContext, List<AgentRun>>(db => db.AgentRuns.OrderBy(r => r.CreatedAt).ToListAsync());
        Assert.Equal([AgentFeedback.Confirmed, AgentFeedback.Disputed], runs.Select(r => r.Feedback));
        Assert.All(runs, r => Assert.Equal(AgentOutcomeKind.Answered, r.Kind));
        Assert.Equal(5, runs[0].GetSteps().Count);
    }
}

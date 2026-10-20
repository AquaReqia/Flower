using Flower.Agents.Abstractions;
using Flower.Agents.Agents;
using Flower.Agents.Orchestration;

namespace Flower.Agents.Tests;

public sealed class OrchestratorTests
{
    [Fact]
    public async Task SmallTalk_PassThrough_McpCagrilmaz()
    {
        using var h = new AgentHarness();
        h.Model.On(AgentNames.Router, """{"intent":"small_talk","reason":"selam"}""");

        var outcome = await h.AskAsync("Merhaba!");

        Assert.Equal(AgentOutcomeKind.PassThrough, outcome.Kind);
        Assert.Null(outcome.Message);
        Assert.Equal(0, h.Mcp.Calls);
        Assert.Single(h.Runs.All);
    }

    [Fact]
    public async Task Disabled_PassThrough_ModelCagrilmaz()
    {
        using var h = new AgentHarness(new() { ["Flower:Agents:Enabled"] = "false" });

        var outcome = await h.AskAsync("Fatura modülünün sahibi kim?");

        Assert.Equal(AgentOutcomeKind.PassThrough, outcome.Kind);
        Assert.Empty(h.Model.Calls);
        Assert.Equal(0, h.Mcp.Calls);
    }

    [Fact]
    public async Task OutOfScope_KibarRet()
    {
        using var h = new AgentHarness();
        h.Model.On(AgentNames.Router, """{"intent":"out_of_scope","reason":"hava"}""");

        var outcome = await h.AskAsync("Yarın hava nasıl?");

        Assert.Equal(AgentOutcomeKind.OutOfScope, outcome.Kind);
        Assert.Equal(AgentOrchestrator.OutOfScopeMessage, outcome.Message);
    }

    [Fact]
    public async Task KaynakYok_JudgeCagrilmaz_NotFound()
    {
        using var h = new AgentHarness().WithHappyPath();
        h.Mcp.Handler = (_, _) => """{"results":[]}""";

        var outcome = await h.AskAsync("Fatura modülünün sahibi kim?");

        Assert.Equal(AgentOutcomeKind.NotFound, outcome.Kind);
        Assert.Contains("bulamadım", outcome.Message);
        Assert.Contains("fatura", outcome.Message);
        Assert.Equal(0, h.Model.CallCount(AgentNames.Judge));
        Assert.Single(h.Runs.All);
    }

    [Fact]
    public async Task Sufficient_YuksekGuven_AtifliCevap()
    {
        using var h = new AgentHarness().WithHappyPath();
        h.Mcp.Handler = (_, _) => """
            {"results":[
              {"id":"1","title":"Fatura Modülü Sorumluları","url":"https://conf/pages/1","spaceKey":"FIN","excerpt":"Fatura modülünün sahibi Finans BT ekibidir.","lastModified":"2026-03-12T10:15:00Z"},
              {"id":"2","title":"Alakasız Sayfa","url":"https://conf/pages/2","excerpt":"Yemekhane menüsü fatura"}]}
            """;

        var outcome = await h.AskAsync("Fatura modülünün sahibi kim?");

        Assert.Equal(AgentOutcomeKind.Answered, outcome.Kind);
        Assert.Contains("[1]", outcome.Message);
        Assert.Contains("Kaynaklar:", outcome.Message);
        Assert.Contains("[1] Fatura Modülü Sorumluları — https://conf/pages/1  (Confluence, 12.03.2026)", outcome.Message);
        Assert.DoesNotContain("Alakasız Sayfa", outcome.Message);
        Assert.Contains("#Dogru", outcome.Message);
        Assert.Single(outcome.Sources);

        var run = Assert.Single(h.Runs.All);
        Assert.Equal(AgentOutcomeKind.Answered, run.Kind);
        Assert.Equal(SufficiencyLevel.Sufficient, run.Verdict);
        Assert.Equal(["Router", "Planner", "Retrieve", "Judge", "Synthesizer"], run.GetSteps().Select(s => s.Name));
        Assert.Equal(15, run.GetSteps()[0].Tokens);
        Assert.Single(run.GetCitedSources());
    }

    [Fact]
    public async Task Sufficient_DusukGuven_PartialMuamelesi()
    {
        using var h = new AgentHarness().WithHappyPath();
        h.Model.Reset(AgentNames.Judge);
        h.Model.On(AgentNames.Judge, """{"verdict":"sufficient","confidence":0.3,"usedSources":[1],"missing":["Yedek sorumlu"],"reason":""}""");

        var outcome = await h.AskAsync("Fatura modülünün sahibi kim?");

        Assert.Equal(AgentOutcomeKind.Answered, outcome.Kind);
        Assert.Equal(SufficiencyLevel.Partial, outcome.Verdict!.Verdict);
        Assert.Contains("Eksik kalan: Yedek sorumlu", outcome.Message);
        var synthCall = h.Model.Calls.Single(c => c.AgentName == AgentNames.Synthesizer);
        Assert.Contains("Eksik kalan", synthCall.Messages[0].Content);
    }

    [Fact]
    public async Task Insufficient_NotFound()
    {
        using var h = new AgentHarness().WithHappyPath();
        h.Model.Reset(AgentNames.Judge);
        h.Model.On(AgentNames.Judge, """{"verdict":"insufficient","confidence":0.8,"usedSources":[],"missing":[],"reason":""}""");

        var outcome = await h.AskAsync("Fatura modülünün sahibi kim?");

        Assert.Equal(AgentOutcomeKind.NotFound, outcome.Kind);
        Assert.Equal(0, h.Model.CallCount(AgentNames.Synthesizer));
    }

    [Fact]
    public async Task TumKaynaklarHata_Failed_NotFoundDegil()
    {
        using var h = new AgentHarness(new() { ["Flower:Agents:KnowledgeBase:Enabled"] = "false" }).WithHappyPath();
        h.Mcp.Handler = (tool, _) => throw new McpToolException(tool, "bağlantı yok");

        var outcome = await h.AskAsync("Fatura modülünün sahibi kim?");

        Assert.Equal(AgentOutcomeKind.Failed, outcome.Kind);
        Assert.Equal(AgentOrchestrator.SourcesUnavailableMessage, outcome.Message);
        Assert.NotNull(Assert.Single(h.Runs.All).Error);
    }

    [Fact]
    public async Task ModelKapali_AcikHataMesaji()
    {
        using var h = new AgentHarness();
        h.Model.Throw(AgentNames.Router, new ModelApiException(503, "down"));

        var outcome = await h.AskAsync("Fatura modülünün sahibi kim?");

        Assert.Equal(AgentOutcomeKind.Failed, outcome.Kind);
        Assert.Equal(AgentOrchestrator.ModelUnavailableMessage, outcome.Message);
        Assert.Contains("ModelApiException", Assert.Single(h.Runs.All).Error);
    }

    [Fact]
    public async Task BozukJson_FallbackIleAkisDevamEder()
    {
        using var h = new AgentHarness().WithHappyPath();
        h.Model.Reset(AgentNames.Router);
        h.Model.Reset(AgentNames.Planner);
        h.Model.On(AgentNames.Router, "bozuk");
        h.Model.On(AgentNames.Planner, "yine bozuk");

        var outcome = await h.AskAsync("Fatura modülünün sahibi kim?");

        Assert.Equal(AgentOutcomeKind.Answered, outcome.Kind);
        var steps = Assert.Single(h.Runs.All).GetSteps();
        Assert.True(steps[0].IsFallback);
        Assert.True(steps[1].IsFallback);
    }

    [Fact]
    public async Task Iptal_OperationCanceledException_RunYazilir()
    {
        using var h = new AgentHarness().WithHappyPath();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.AskAsync("Fatura?", ct: cts.Token));
        Assert.Equal("İptal edildi", Assert.Single(h.Runs.All).Error);
    }

    [Fact]
    public async Task AtifsizCevap_UyariEklenir()
    {
        using var h = new AgentHarness().WithHappyPath();
        h.Model.Reset(AgentNames.Synthesizer);
        h.Model.On(AgentNames.Synthesizer, "Finans BT ekibi.");

        var outcome = await h.AskAsync("Fatura modülünün sahibi kim?");

        Assert.Contains(AnswerFormatter.NoCitationWarning, outcome.Message);
        Assert.DoesNotContain("Kaynaklar:", outcome.Message);
    }
}

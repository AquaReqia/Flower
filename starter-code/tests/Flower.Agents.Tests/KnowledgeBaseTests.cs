using Flower.Agents.Agents;
using Flower.Agents.Knowledge;
using Flower.Agents.KnowledgeBase;
using Flower.Agents.Orchestration;
using Flower.Agents.Runs;

namespace Flower.Agents.Tests;

public sealed class KnowledgeBaseTests
{
    private const string Question = "Fatura modülünün sahibi kim?";

    [Fact]
    public async Task Dogru_YeniKayit_SonrakiSorudaIlkKaynak()
    {
        using var h = new AgentHarness().WithHappyPath();
        await h.AskAsync(Question);

        var result = await h.FeedbackAsync(f => f.ConfirmAsync("Ayse@Kurum.com", null, default));

        Assert.True(result.Success);
        Assert.Contains("Kaydettim", result.Message);
        var entry = Assert.Single(h.Knowledge.All);
        Assert.Equal(KnowledgeStatus.Confirmed, entry.Status);
        Assert.Equal(0.7, entry.Confidence);
        Assert.Equal(1, entry.ConfirmationCount);
        Assert.Equal("FIN", entry.SourceSpaces);
        Assert.Equal(AgentFeedback.Confirmed, h.Runs.All[0].Feedback);
        Assert.Equal(Abstractions.ModelPriority.Background, ModelPriority(h));

        h.Time.Advance(TimeSpan.FromHours(1));
        var second = await h.AskAsync(Question, "mehmet@kurum.com");

        var judgeInput = h.Model.Calls.Last(c => c.AgentName == AgentNames.Judge).Messages[1].Content;
        Assert.Contains("[1] (Doğrulanmış bilgi · onaylı · güven 0.70", judgeInput);
        Assert.Contains("Doğrulanmış bilgi: \"Fatura modülünün sahibi\" — onaylı", second.Message);
        Assert.Equal(KnowledgeSourceKind.KnowledgeBase, second.Sources[0].Kind);
    }

    [Fact]
    public async Task AyniKayit_FarkliKisiOnayi_Verified()
    {
        using var h = new AgentHarness().WithHappyPath();
        await h.AskAsync(Question);
        await h.FeedbackAsync(f => f.ConfirmAsync("ayse@kurum.com", null, default));

        await h.AskAsync(Question, "mehmet@kurum.com");
        var result = await h.FeedbackAsync(f => f.ConfirmAsync("mehmet@kurum.com", null, default));

        var entry = Assert.Single(h.Knowledge.All);
        Assert.Contains("yeniden onayladım", result.Message);
        Assert.Equal(KnowledgeStatus.Verified, entry.Status);
        Assert.Equal(0.9, entry.Confidence);
        Assert.Equal(2, entry.ConfirmationCount);
        Assert.Equal("ayse@kurum.com;mehmet@kurum.com", entry.ConfirmedByEmails);
    }

    [Fact]
    public async Task AyniSoru_BaskaKaynak_EskiSuperseded()
    {
        using var h = new AgentHarness().WithHappyPath();
        await h.AskAsync(Question);
        await h.FeedbackAsync(f => f.ConfirmAsync("ayse@kurum.com", null, default));
        var old = Assert.Single(h.Knowledge.All);

        // İkinci soruda Synthesizer yalnızca Confluence kaynağına atıf yapar ([2]; [1] bilgi deposu).
        h.Model.Reset(AgentNames.Synthesizer);
        h.Model.On(AgentNames.Synthesizer, "Sahibi artık Finans BT ekibidir [2].");
        var second = await h.AskAsync(Question, "mehmet@kurum.com");
        Assert.Equal(KnowledgeSourceKind.Mcp, Assert.Single(second.Sources).Kind);

        await h.FeedbackAsync(f => f.ConfirmAsync("mehmet@kurum.com", null, default));

        Assert.Equal(2, h.Knowledge.All.Count);
        Assert.Equal(KnowledgeStatus.Superseded, old.Status);
        var fresh = h.Knowledge.All.Single(e => e.Id != old.Id);
        Assert.Equal(KnowledgeStatus.Confirmed, fresh.Status);
        Assert.Equal(fresh.Id, old.SupersededById);
    }

    [Fact]
    public async Task Writer_ShouldStoreFalse_KayitYok_NedenSoylenir()
    {
        using var h = new AgentHarness().WithHappyPath();
        h.Model.Reset(AgentNames.KnowledgeWriter);
        h.Model.On(AgentNames.KnowledgeWriter, """{"shouldStore":false,"skipReason":"tek seferlik toplantı bilgisi"}""");
        await h.AskAsync(Question);

        var result = await h.FeedbackAsync(f => f.ConfirmAsync("ayse@kurum.com", null, default));

        Assert.Empty(h.Knowledge.All);
        Assert.Contains("tek seferlik toplantı bilgisi", result.Message);
        Assert.Equal(AgentFeedback.Confirmed, h.Runs.All[0].Feedback);
    }

    [Fact]
    public async Task PiiIcerenCevap_KayitYok()
    {
        using var h = new AgentHarness().WithHappyPath();
        h.Model.Reset(AgentNames.Synthesizer);
        h.Model.On(AgentNames.Synthesizer, "Sorumlu kişiye 0532 123 45 67 numarasından ulaşabilirsiniz [1].");
        await h.AskAsync(Question);

        var result = await h.FeedbackAsync(f => f.ConfirmAsync("ayse@kurum.com", null, default));

        Assert.Empty(h.Knowledge.All);
        Assert.Contains("telefon", result.Message);
        Assert.Equal(0, h.Model.CallCount(AgentNames.KnowledgeWriter));
    }

    [Fact]
    public async Task Yanlis_KayitDisputed_AramadaCikmaz()
    {
        using var h = new AgentHarness().WithHappyPath();
        await h.AskAsync(Question);
        await h.FeedbackAsync(f => f.ConfirmAsync("ayse@kurum.com", null, default));
        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.AskAsync(Question, "mehmet@kurum.com");

        var result = await h.FeedbackAsync(f => f.DisputeAsync("mehmet@kurum.com", "Sahibi değişti", default));

        var entry = Assert.Single(h.Knowledge.All);
        Assert.Equal(KnowledgeStatus.Disputed, entry.Status);
        Assert.Equal(0.35, entry.Confidence, 3);
        Assert.Equal("Sahibi değişti", entry.LastDisputeNote);
        Assert.Contains("askıya aldım", result.Message);
        Assert.Equal(AgentFeedback.Disputed, h.Runs.All[^1].Feedback);

        var third = await h.AskAsync(Question, "ali@kurum.com");
        Assert.All(third.Sources, s => Assert.Equal(KnowledgeSourceKind.Mcp, s.Kind));
    }

    [Fact]
    public async Task OnayPenceresiDisi_VeyaRunYok_AnlasilirMesaj()
    {
        using var h = new AgentHarness().WithHappyPath();

        var none = await h.FeedbackAsync(f => f.ConfirmAsync("ayse@kurum.com", null, default));
        Assert.False(none.Success);
        Assert.Contains("Onaylanacak bir cevap bulamadım", none.Message);

        await h.AskAsync(Question);
        h.Time.Advance(TimeSpan.FromHours(25));
        var late = await h.FeedbackAsync(f => f.ConfirmAsync("ayse@kurum.com", null, default));
        Assert.False(late.Success);
        Assert.Empty(h.Knowledge.All);
    }

    [Fact]
    public async Task Dogru_SmallTalkSonrasi_SonCevabiOnaylar()
    {
        using var h = new AgentHarness().WithHappyPath();
        await h.AskAsync(Question);
        h.Model.Reset(AgentNames.Router);
        h.Model.On(AgentNames.Router, """{"intent":"small_talk","reason":""}""");
        await h.AskAsync("Teşekkürler!");

        var result = await h.FeedbackAsync(f => f.ConfirmAsync("ayse@kurum.com", null, default));

        Assert.True(result.Success);
        Assert.Single(h.Knowledge.All);
    }

    [Fact]
    public async Task Kaynak_DogruBicim()
    {
        using var h = new AgentHarness().WithHappyPath();
        await h.AskAsync(Question);

        var result = await h.FeedbackAsync(f => f.DescribeSourcesAsync("ayse@kurum.com", default));

        Assert.Equal(
            "Son cevabın kaynakları (karar: sufficient, güven 0.90):\n[1] Confluence · FIN · 12.03.2026 — Fatura Modülü Sorumluları — https://conf/pages/1",
            result.Message);
    }

    [Fact]
    public async Task EskimisKayit_EtiketEklenir()
    {
        using var h = new AgentHarness().WithHappyPath();
        await h.AskAsync(Question);
        await h.FeedbackAsync(f => f.ConfirmAsync("ayse@kurum.com", null, default));

        h.Time.Advance(TimeSpan.FromDays(200));
        var outcome = await h.AskAsync(Question, "mehmet@kurum.com");

        Assert.Contains("(eskimiş olabilir)", outcome.Message);
    }

    [Fact]
    public void Scoring_KisitliSpaceKaydi_IzinsizKullaniciyaCikmaz()
    {
        var entry = new KnowledgeEntry
        {
            Title = "Fatura modülünün sahibi",
            CanonicalQuestion = Question,
            Keywords = "fatura;sahip",
            SourceSpaces = "FIN;HR",
            Status = KnowledgeStatus.Confirmed,
        };

        var allowed = KnowledgeScoring.Rank([entry], new KnowledgeSearch(Question, ["fatura", "sahip"], null, 5, 0.5, ["FIN", "HR"]));
        var denied = KnowledgeScoring.Rank([entry], new KnowledgeSearch(Question, ["fatura", "sahip"], null, 5, 0.5, ["FIN"]));

        Assert.Single(allowed);
        Assert.Empty(denied);
    }

    [Fact]
    public void Scoring_EmbeddingVarsa_CosineEsigi()
    {
        var entry = new KnowledgeEntry
        {
            Title = "x",
            Status = KnowledgeStatus.Verified,
            Embedding = Text.VectorMath.ToBytes([1f, 0f]),
        };

        Assert.NotNull(KnowledgeScoring.Score(entry, [], [1f, 0f], 0.55));
        Assert.Null(KnowledgeScoring.Score(entry, [], [0f, 1f], 0.55));
    }

    private static Abstractions.ModelPriority ModelPriority(AgentHarness h) =>
        h.Model.Calls.Single(c => c.AgentName == AgentNames.KnowledgeWriter).Priority;
}

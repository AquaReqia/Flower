using System.Text.Json.Nodes;
using Flower.Agents.Abstractions;
using Flower.Agents.Agents;
using Flower.Agents.Configuration;
using Flower.Agents.Knowledge;
using Flower.Agents.Knowledge.Mcp;
using Flower.Agents.KnowledgeBase;
using Flower.Agents.ModelApi;
using Flower.Agents.Structured;
using Flower.Agents.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Flower.Agents.Tests;

public sealed class HelperTests
{
    private static KnowledgeHit Hit(string id, string snippet = "metin", string source = "confluence") => new()
    {
        SourceName = source,
        Kind = KnowledgeSourceKind.Mcp,
        SourceId = id,
        Title = "Sayfa " + id,
        Snippet = snippet,
    };

    private static StructuredOutputInvoker Invoker(FakeChatModelClient model) => new(
        model,
        Options.Create(new AgentOptions { DefaultProfile = new AgentProfileOptions { Model = "m" } }),
        NullLogger<StructuredOutputInvoker>.Instance);

    [Fact]
    public void ContextFormatter_NumaraliBaglam()
    {
        var hits = new[]
        {
            Hit("1") with { Url = "https://conf/1", SpaceKey = "FIN", LastModified = new DateTimeOffset(2026, 3, 12, 0, 0, 0, TimeSpan.Zero) },
            new KnowledgeHit
            {
                SourceName = KnowledgeBaseSource.SourceName, Kind = KnowledgeSourceKind.KnowledgeBase, SourceId = "x",
                Title = "Not", Snippet = "cevap", Confidence = 0.7, StatusLabel = "onaylı",
                LastModified = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            },
        };

        var text = ContextFormatter.FormatHits(hits, 1500);

        Assert.Equal(
            "[1] (Confluence · space: FIN · 12.03.2026) Sayfa 1\nhttps://conf/1\nmetin\n\n[2] (Doğrulanmış bilgi · onaylı · güven 0.70 · 05.10.2026) Not\ncevap",
            text);
    }

    [Fact]
    public void ContextFormatter_Atiflar_CokluVeAralikDisi()
    {
        Assert.Equal([2, 1, 3], ContextFormatter.ExtractCitations("a [2] b [1, 3] c [2] d [9]", 3));
    }

    [Fact]
    public async Task Invoker_KodBlogundakiJson()
    {
        var model = new FakeChatModelClient().On("Router", "```json\n{\"intent\":\"small_talk\",\"reason\":\"x\"}\n```");

        var r = await Invoker(model).InvokeAsync("Router", "s", "u", () => new IntentResult(), ModelPriority.Interactive, default);

        Assert.False(r.IsFallback);
        Assert.Equal(IntentKind.SmallTalk, r.Value.Intent);
    }

    [Fact]
    public async Task Invoker_IlkBozukIkinciDogru_Basari_TekSystemMesaji()
    {
        var model = new FakeChatModelClient().On("Router", "bozuk", """{"intent":"out_of_scope","reason":""}""");

        var r = await Invoker(model).InvokeAsync("Router", "s", "u", () => new IntentResult(), ModelPriority.Interactive, default);

        Assert.False(r.IsFallback);
        Assert.Equal(IntentKind.OutOfScope, r.Value.Intent);
        Assert.Equal(30, r.Tokens);
        Assert.Single(model.Calls[1].Messages, m => m.Role == ChatRole.System);
    }

    [Fact]
    public async Task Invoker_IkiKezBozuk_Fallback()
    {
        var model = new FakeChatModelClient().On("Router", "bozuk");

        var r = await Invoker(model).InvokeAsync(
            "Router", "s", "u", () => new IntentResult { Reason = "fb" }, ModelPriority.Interactive, default);

        Assert.True(r.IsFallback);
        Assert.Equal("fb", r.Value.Reason);
        Assert.Equal(2, model.Calls.Count);
    }

    [Fact]
    public async Task Invoker_ModelApiException_Yutulmaz()
    {
        var model = new FakeChatModelClient().Throw("Router", new ModelApiException(500, "x"));

        await Assert.ThrowsAsync<ModelApiException>(() =>
            Invoker(model).InvokeAsync("Router", "s", "u", () => new IntentResult(), ModelPriority.Interactive, default));
    }

    [Fact]
    public void ChatClient_JsonSchemaAlani_VeKapatma()
    {
        var request = new ChatRequest
        {
            AgentName = "Judge",
            Model = "m",
            Messages = [ChatMessage.FromUser("u")],
            ResponseSchema = new JsonSchemaFormat("X", new JsonObject { ["type"] = "object" }),
        };

        Assert.Equal("json_schema", OpenAiCompatibleChatClient.BuildBody(request, true)["response_format"]?["type"]?.GetValue<string>());
        Assert.Null(OpenAiCompatibleChatClient.BuildBody(request, false)["response_format"]);
    }

    [Fact]
    public void ChatClient_ThinkTemizligi_VeUsage()
    {
        const string raw = """
            {"choices":[{"message":{"content":"<think>düşünce</think>Cevap"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":12,"completion_tokens":3}}
            """;

        var r = OpenAiCompatibleChatClient.ParseResponse(raw, true);

        Assert.Equal("Cevap", r.Content.Trim());
        Assert.Equal(12, r.PromptTokens);
        Assert.Equal(3, r.CompletionTokens);
        Assert.Equal("son", OpenAiCompatibleChatClient.StripThink("kesik</think>son"));
    }

    [Fact]
    public void McpParser_ResultsDizisi_VeHtmlTemizligi()
    {
        const string raw = """
            {"results":[{"id":"7","title":"T","_links":{"webui":"https://conf/7"},"space":{"key":"IT"},
              "body":{"storage":{"value":"<p>Merhaba <b>dünya</b></p>"}},
              "lastEditor":{"email":"m@k.com","displayName":"Mehmet"}}]}
            """;

        var hit = Assert.Single(McpResultParser.Parse(raw, "confluence"));

        Assert.Equal("7", hit.SourceId);
        Assert.Equal("https://conf/7", hit.Url);
        Assert.Equal("IT", hit.SpaceKey);
        Assert.Contains("Merhaba dünya", hit.Snippet);
        Assert.Equal("m@k.com", hit.LastEditor?.Email);
    }

    [Fact]
    public void McpParser_DuzDizi_VeJsonOlmayanMetin()
    {
        Assert.Equal(2, McpResultParser.Parse("""[{"id":"1","title":"a","excerpt":"x"},{"id":"2","title":"b","excerpt":"y"}]""", "c").Count);
        Assert.Contains("düz metin", Assert.Single(McpResultParser.Parse("düz metin sonucu", "c")).Snippet);
    }

    [Fact]
    public void CqlBuilder_Temizlik_SpaceFiltresi_Bos()
    {
        var cql = CqlBuilder.Build(["fatura(", "sahip*"], ["FIN"]);

        Assert.Equal("type = page AND (text ~ \"fatura\" OR text ~ \"sahip\" OR title ~ \"fatura\" OR title ~ \"sahip\") AND space in (\"FIN\")", cql);
        Assert.Null(CqlBuilder.Build(["*", "?"], null));
    }

    [Fact]
    public void KeywordExtractor_TurkceHarf_Durak_Tekrar()
    {
        Assert.Equal(["istanbul", "ılık", "sahibi"], KeywordExtractor.Extract("İSTANBUL ılık ve sahibi kim? İstanbul"));
    }

    [Fact]
    public void Fuse_Agirlik_Tekillestirme()
    {
        IReadOnlyList<KnowledgeHit> a = [Hit("1"), Hit("2")];
        IReadOnlyList<KnowledgeHit> b = [Hit("2", "daha uzun metin", "kb") with { Url = null }, Hit("3", source: "kb")];

        var fused = HybridRetriever.Fuse([(1.0, a), (3.0, b)], 60);

        Assert.Equal("kb:2", fused[0].Key);
        Assert.Equal(4, fused.Count);

        var dup = HybridRetriever.Fuse([(1.0, [Hit("1")]), (1.0, [Hit("1", "uzun snippet")])], 60);
        Assert.Equal("uzun snippet", Assert.Single(dup).Snippet);
    }

    [Fact]
    public async Task Retriever_BirKaynakHata_DigerleriDoner_HepsiHata_AllSourcesFailed()
    {
        var options = Options.Create(new AgentOptions());
        var ok = new StubSource("ok", [Hit("1")]);
        var bad = new StubSource("bad", null);

        var partial = await new HybridRetriever([ok, bad], options, NullLogger<HybridRetriever>.Instance)
            .RetrieveAsync(new KnowledgeQuery("q", [], ["q"], 5), default);
        var all = await new HybridRetriever([bad], options, NullLogger<HybridRetriever>.Instance)
            .RetrieveAsync(new KnowledgeQuery("q", [], ["q"], 5), default);

        Assert.Single(partial.Hits);
        Assert.False(partial.AllSourcesFailed);
        Assert.True(all.AllSourcesFailed);
    }

    [Theory]
    [InlineData("TCKN 10000000146", "T.C. kimlik numarası")]
    [InlineData("IBAN TR33 0006 1005 1978 6457 8413 26", "IBAN")]
    [InlineData("kart 4111 1111 1111 1111", "kart numarası")]
    [InlineData("tel +90 532 123 45 67", "telefon numarası")]
    [InlineData("sipariş 12345678901 ve sistem v2", null)]
    public void PiiGuard_Tespit(string text, string? expected)
    {
        Assert.Equal(expected, PiiGuard.Detect(text));
    }

    [Fact]
    public void Planner_Fallback_KeywordExtractor()
    {
        var plan = QueryPlanner.Fallback("Fatura modülünün sahibi kim?");

        Assert.Equal(["Fatura modülünün sahibi kim?"], plan.SubQuestions);
        Assert.Contains("fatura", plan.Keywords);
    }

    private sealed class StubSource(string name, IReadOnlyList<KnowledgeHit>? hits) : IKnowledgeSource
    {
        public string Name => name;

        public double Weight => 1;

        public Task<IReadOnlyList<KnowledgeHit>> SearchAsync(KnowledgeQuery query, CancellationToken ct) =>
            hits is null ? throw new InvalidOperationException("down") : Task.FromResult(hits);
    }
}

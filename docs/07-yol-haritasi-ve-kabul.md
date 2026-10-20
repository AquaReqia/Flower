# 07 — Yol Haritası, İş Sırası ve Kabul Kriterleri

## 1. Başlangıç kodunun durumu (`starter-code/src/Flower.Agents`)

`dotnet build` → 0 warning, 0 error (.NET 10).

| Dosya | Durum | İçerik |
|---|---|---|
| `Abstractions/ChatModels.cs` | ✅ | ChatMessage, ChatRequest (AgentName zorunlu), ChatResponse, ModelPriority, IChatModelClient, IEmbeddingClient, ModelApiException |
| `Abstractions/OrgDirectory.cs` | ✅ | PersonRef, PersonInfo, IOrgDirectory, NullOrgDirectory (Faz D için) |
| `Abstractions/McpToolCaller.cs` | ✅ | IMcpToolCaller, McpToolException, MissingMcpToolCaller |
| `Configuration/AgentOptions.cs` | ✅ | Tüm ayarlar (Model, Profiles, Retrieval, McpSources, Embedding, KnowledgeBase, Scheduler) |
| `ModelApi/OpenAiCompatibleChatClient.cs` | ✅ | `/chat/completions`, json_schema, header'lar, `<think>` temizliği, TIMING log, telemetri |
| `ModelApi/OpenAiCompatibleEmbeddingClient.cs` | ✅ | `/embeddings`, batch |
| `ModelApi/ModelCallScheduler.cs` | ✅ | Interactive/Background semaforları |
| `Structured/StructuredOutputInvoker.cs` | ✅ | Şema üretimi, JSON çıkarma, 1 düzeltme, fallback |
| `Prompts/PromptLibrary.cs` + `Templates/*.md` | ✅ | Gömülü şablonlar, disk override, `{{}}` render |
| `Text/TextUtilities.cs` | ✅ | KeywordExtractor (TR), HtmlText, SnippetSelector, VectorMath, CqlBuilder |
| `Knowledge/KnowledgeModels.cs` | ✅ | KnowledgeHit, KnowledgeQuery, RetrievalResult, IKnowledgeSource |
| `Knowledge/HybridRetriever.cs` | ✅ | Paralel arama, ağırlıklı RRF, tekilleştirme, space filtresi |
| `Knowledge/Mcp/*` | ✅ | McpKnowledgeSource, McpResultParser |
| `Telemetry/AgentTelemetry.cs` | ✅ | ActivitySource |

## 2. İş sırası (yazılacaklar)

Her adım sonunda `dotnet build Flower.slnx` (0 warning) ve `dotnet test` yeşil olmalı.

1. **Agent modelleri:** `Agents/AgentModels.cs` (docs/03'teki tipler) + `ContextFormatter`.
2. **Agent'lar:** `IntentRouter`, `QueryPlanner`, `SufficiencyJudge`, `AnswerSynthesizer`, `KnowledgeWriter`. Her biri arayüz + implementasyon, prompt'ları `PromptLibrary`'den.
3. **Run kaydı:** `Runs/AgentRun.cs`, `IAgentRunStore`, `InMemoryAgentRunStore`.
   - AgentRun alanları: Id, RequestId, UserEmail, Question, Kind, Intent, Verdict, Confidence, AnswerText, CitedSourcesJson, StepsJson (adım adı, süre ms, token, fallback mı), Error, Feedback (None/Confirmed/Disputed), FeedbackNote, CreatedAt, CompletedAt, FeedbackAt.
4. **Orkestratör:** `Orchestration/*` (docs/03 akışı) + cevap biçimlendirme (kaynak listesi, FeedbackHint, eskimiş etiketi).
5. **Bilgi deposu:** `KnowledgeBase/*` — KnowledgeEntry, IKnowledgeStore, InMemoryKnowledgeStore, KnowledgeScoring, KnowledgeBaseSource, PiiGuard, KnowledgeCurator, IKnowledgePublisher + Noop.
6. **Geri bildirim:** `Feedback/AgentFeedbackService` (#Dogru, #Yanlis, #Kaynak).
7. **DI:** `AddFlowerAgents(IServiceCollection, IConfiguration, Action<IHttpClientBuilder>? configureHttp = null)`.
   - Config'e göre kaynakları kaydeder: `KnowledgeBase.Enabled` → KnowledgeBaseSource; her etkin `McpSources` elemanı → McpKnowledgeSource.
   - `TryAdd` ile varsayılanlar: InMemory store'lar, `NullOrgDirectory`, `MissingMcpToolCaller`, `NoopKnowledgePublisher`, `TimeProvider.System`.
   - `PostConfigure`: `Flower:Model` değerlerine düşme (docs/06).
8. **Persistence:** `Persistence/AgentsDbContext` (`HasDefaultSchema("agents")`), entity config'leri, `EfKnowledgeStore`, `EfAgentRunStore`, `AddFlowerAgentsPersistence(connectionString)`, `IDesignTimeDbContextFactory`, ilk migration.
9. **Flower entegrasyonu:** docs/05 §1–4.
10. **Smoke test:** `smoketest`'e `--agents "<soru>"` senaryosu: gerçek Bifrost + gerçek MCP ile tek soru; çıktıda karar, kaynaklar, süreler.

## 3. Test listesi (xUnit, gerçek LLM/MCP yok)

**Yardımcılar ve parser'lar**
- `StructuredOutputInvoker`: kod bloğu içindeki JSON'u okur; ilk denemede bozuk, ikincide doğru JSON → başarı; iki kez bozuk → fallback + `IsFallback`; `ModelApiException` fırlatılırsa yutmaz.
- `OpenAiCompatibleChatClient.BuildBody/ParseResponse/StripThink`: json_schema alanı, `UseJsonSchemaResponseFormat=false`, `<think>` temizliği, usage okuma.
- `McpResultParser`: `results[]` biçimi, düz dizi, `_links.webui`, `body.storage.value` HTML temizliği, kişi nesnesi/metni, JSON olmayan metin.
- `CqlBuilder`: özel karakter temizliği, tırnak kaçışı, space filtresi, boş anahtar kelime → null.
- `KeywordExtractor`: Türkçe büyük/küçük harf (İ/ı), durak kelimeler, tekrar.
- `HybridRetriever.Fuse`: ağırlık sıralamayı değiştirir, tekilleştirme, bir kaynak hata → diğerleri döner, hepsi hata → `AllSourcesFailed`.

**Agent akışı (FakeChatModelClient + FakeMcpToolCaller)**
- SmallTalk → PassThrough, MCP çağrılmaz.
- Enabled=false → PassThrough, model çağrılmaz.
- Kaynak yok → Judge çağrılmaz, NotFound.
- Sufficient + yüksek güven → Answered; mesajda `[1]` ve "Kaynaklar:" listesi; atıf yapılmayan kaynak listede yok.
- Sufficient + düşük güven → Partial muamelesi.
- Tüm kaynaklar hata → Failed (NotFound DEĞİL).
- İptal (ct) → OperationCanceledException.
- Her senaryoda AgentRun yazılır.

**Bilgi deposu**
- `#Dogru` → yeni kayıt (Confirmed, 0.7); sonraki aynı soruda KnowledgeBaseSource sonucu ilk sırada.
- Aynı kayda dayanan cevaba farklı kişiden `#Dogru` → Verified (0.9), ConfirmationCount=2.
- Aynı soruya başka kaynaktan cevap + `#Dogru` → eski Superseded, yeni Confirmed.
- KnowledgeWriter `shouldStore=false` → kayıt yok, kullanıcıya neden.
- PII içeren cevap → kayıt yok.
- `#Yanlis` → kayıt Disputed, aramada çıkmaz; run Disputed.
- Onay penceresi dışında / onaylanacak run yoksa → anlaşılır mesaj.
- `#Kaynak` → doğru biçim.

**Flower tarafı**
- CommandParser: `#Dogru`, `#Yanlis`, `#Kaynak` başta/ortada/sonda.
- JobProcessor: PassThrough → mevcut AgentService çağrılır; Answered → outcome.Message gönderilir.

## 4. Kabul kriterleri (bu faz)
1. `Enabled=false` iken Flower davranışı ve mevcut 89 test birebir aynı.
2. Kurum bilgisi sorusuna kaynaklı cevap; kaynaksız iddia yok; kaynak yoksa "bulamadım".
3. MCP server kapalıyken kullanıcıya "kaynaklara erişemiyorum" (bilgi yok değil).
4. `#Dogru` ile onaylanan cevap aynı/benzer soruda bilgi deposundan, ilk kaynak olarak gelir.
5. `#Yanlis` sonrası ilgili kayıt cevaplarda kullanılmaz.
6. Model JSON bozsa bile akış çökmez (fallback); model kapalıysa açık hata mesajı.
7. Her soru için AgentRun'da adımlar, süreler, karar ve kaynaklar görülebilir.
8. Build 0 warning; testler gerçek ağ çağrısı yapmaz.

## 5. Sonraki fazlar (bu fazda yazılmaz)
- **C – Vektör/senkron:** Confluence sayfalarını indeksleme (MCP'de listeleme tool'u varsa), embedding'li arama.
- **D – Uzmana soru:** Inquiry, Expert Router (sayfa sahibi → yönetici), `#Cevap/#Bilmiyorum/#Yonlendir`, hatırlatma, kota, sessiz saat, pilot listesi. `NotFound` → `NeedsExpert`.
- **E – Kurum modeli/proaktif:** Fact çıkarımı, Gap Detector, Org senkronu, `#NoAsk`.
- **F – Yayın:** `IKnowledgePublisher` → Confluence MCP yazma tool'u (taslak sayfa), WinForms ekranları.

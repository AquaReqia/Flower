# 02 — Mimari ve Modül Yapısı

## Bağımlılık yönü

```
Flower.WinApp ──► Flower.Core ──► Flower.Infrastructure ──► Flower.Domain
                       │
                       └──────► Flower.Agents      (Flower projelerine REFERANS VERMEZ)
```

- `Flower.Agents` Domain'i, Infrastructure'ı ve Core'u görmez. Kendi port arayüzlerini tanımlar.
- `Flower.Core` hem Flower servislerini hem Agents'ı bilir. Port'ların implementasyonlarını (adaptörleri) `Flower.Core/Agents/` altında yazar ve DI'a kaydeder.
- **Sonuç:** Agent modülü Flower'ı bozmadan geliştirilir ve ayrı test edilir. Mevcut 89 test etkilenmez. `Enabled=false` ile tamamen devre dışı kalır.

## Port'lar (Agents tanımlar, Core implemente eder)

| Port | Amaç | Bu fazda Core implementasyonu |
|---|---|---|
| `IChatModelClient` | Chat completion | **Gerekmez.** Agents içindeki `OpenAiCompatibleChatClient` kullanılır; Core yalnızca HttpClient pipeline'ına proxy handler ve `DebugHttpHandler` ekler. |
| `IEmbeddingClient` | Embedding | Gerekmez (Agents içinde). Opsiyonel: `Embedding.Enabled=false` ise hiç çağrılmaz. |
| `IMcpToolCaller` | MCP tool çağrısı | **Gerekli.** Resmi C# MCP SDK'sı (`ModelContextProtocol` NuGet) ile Confluence MCP server'ınıza bağlanır. |
| `IOrgDirectory` | Kişi çözme / yönetici | Bu fazda gerekmez (`NullOrgDirectory`). Faz D'de Graph ile. |
| `IKnowledgeStore` | Doğrulanmış bilgi deposu | Agents içinde EF implementasyonu (`AgentsDbContext`). Testte `InMemoryKnowledgeStore`. |
| `IAgentRunStore` | Çalışma kayıtları | Aynı şekilde EF / InMemory. |
| `IKnowledgePublisher` | Onaylı bilgiyi başka sisteme yayınlama (örn. Confluence'a taslak) | Bu fazda `NoopKnowledgePublisher`. |

## Klasör yapısı (`src/Flower.Agents/`)

```
Flower.Agents/
├── Abstractions/          ✅ ChatModels, OrgDirectory, McpToolCaller (port'lar ve ortak tipler)
├── Configuration/         ✅ AgentOptions ("Flower:Agents")
├── ModelApi/              ✅ OpenAiCompatibleChatClient, OpenAiCompatibleEmbeddingClient, ModelCallScheduler
├── Structured/            ✅ StructuredOutputInvoker (JSON şema + düzeltme + güvenli varsayılan)
├── Prompts/               ✅ PromptLibrary   ⬜ Templates/*.md (metinler docs/03'te hazır)
├── Text/                  ✅ KeywordExtractor, HtmlText, SnippetSelector, VectorMath, CqlBuilder
├── Knowledge/             ✅ KnowledgeModels, HybridRetriever
│   └── Mcp/               ✅ McpKnowledgeSource, McpResultParser
├── KnowledgeBase/         ⬜ KnowledgeEntry, IKnowledgeStore, InMemoryKnowledgeStore,
│                             KnowledgeBaseSource, KnowledgeCurator, IKnowledgePublisher
├── Agents/                ⬜ IntentRouter, QueryPlanner, SufficiencyJudge, AnswerSynthesizer,
│                             KnowledgeWriter, AgentModels (girdi/çıktı tipleri)
├── Orchestration/         ⬜ IAgentOrchestrator, AgentOrchestrator, AgentRequest, AgentOutcome
├── Runs/                  ⬜ AgentRun, IAgentRunStore, InMemoryAgentRunStore
├── Feedback/              ⬜ IAgentFeedbackService (#Dogru, #Yanlis, #Kaynak)
├── Persistence/           ⬜ AgentsDbContext, entity config'leri, EfKnowledgeStore, EfAgentRunStore,
│                             design-time factory, Migrations/
├── Telemetry/             ✅ AgentTelemetry (ActivitySource "Flower.Agents")
└── DependencyInjection/   ⬜ ServiceCollectionExtensions.AddFlowerAgents(...)
```
✅ = başlangıç kodunda var, ⬜ = yazılacak.

## Katman kuralları

- Agents içindeki her servis bir arayüz arkasındadır. Orkestratör somut tipleri değil arayüzleri kullanır (test edilebilirlik).
- DI ömürleri:
  - **Singleton:** `ModelCallScheduler`, `PromptLibrary`, InMemory store'lar
  - **Scoped:** Agent'lar, orkestratör, EF store'lar, feedback servisi
  - **Typed HttpClient:** Model ve embedding istemcileri
- Agents, Flower'ın kod tarzına uyar: file-scoped namespace, nullable, `Async` soneki, `ct` parametresi, Türkçe kısa XML doc, `TIMING ...` log ön eki, `catch (OperationCanceledException) when (ct.IsCancellationRequested)`.
- Agents hiçbir yerde Teams'e mesaj göndermez. Cevabı `AgentOutcome` olarak döner; gönderimi Flower yapar (bugünkü `TeamsMessagingService` yolu).

## Veritabanı

- Ayrı `AgentsDbContext`, aynı PostgreSQL veritabanı (`flower`), **`agents` şeması**.
- Tablolar: `agents.AgentRuns`, `agents.KnowledgeEntries` (ileride `agents.Inquiries`).
- Migration'lar `src/Flower.Agents/Persistence/Migrations/` altında:
  ```powershell
  dotnet ef migrations add InitAgents --project src/Flower.Agents --context AgentsDbContext
  ```
- Embedding'ler `byte[]` (`bytea`) olarak saklanır ve benzerlik C# tarafında hesaplanır. Böylece pgvector gerekmez ve SQLite testleri de çalışır. Kayıt sayısı on binleri geçerse pgvector'e geçilir (`IKnowledgeStore` arayüzü değişmez).
- `DateTime` alanları UTC tutulur (`DateTimeOffset` SQLite'ta sorgulanamaz).

## NuGet bağımlılıkları (Flower.Agents)

- `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Logging.Abstractions`
- `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design` (PrivateAssets=all)
- MCP SDK **Agents'a değil**, `Flower.Core`'a eklenir.

Not: Başlangıç kodu NuGet erişimi olmayan ortamda `-p:FlowerOfflineBuild=true` ile derlendi. Bu bayrak `Microsoft.AspNetCore.App` shared framework'ünü kullanır. Normal ortamda PackageReference'lar kullanılır; bu bayrak kaldırılabilir.

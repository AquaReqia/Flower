# 05 — Flower Entegrasyonu

> Flower'ın gerçek tip ve metot imzaları bu dokümanda `context/PROJECT_CONTEXT_PROMPT_v2.md`'den çıkarıldı. Kodlarken **mevcut imzaları kontrol edin**; isim farkı varsa Flower'daki ismi esas alın.

## 0. Solution'a ekleme
```powershell
# src/Flower.Agents klasörünü kopyaladıktan sonra
dotnet sln Flower.slnx add src/Flower.Agents/Flower.Agents.csproj
dotnet add src/Flower.Core/Flower.Core.csproj reference src/Flower.Agents/Flower.Agents.csproj
dotnet add tests/Flower.Tests/Flower.Tests.csproj reference src/Flower.Agents/Flower.Agents.csproj
dotnet add src/Flower.Core/Flower.Core.csproj package ModelContextProtocol --prerelease
```
`Flower.Agents.csproj` içindeki `FlowerOfflineBuild` koşulu kurumda gerekmez; kaldırılabilir.

## 1. `Flower.Core/HostSetup.cs` — DI kaydı
```csharp
// Mevcut kayıtlardan sonra:
services.AddFlowerAgents(configuration, http =>
{
    // Kurumsal proxy + Windows default credentials (mevcut HttpClientHandler kurulumunuzla aynı)
    http.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        UseProxy = true,
        DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        UseDefaultCredentials = true,
    });
    http.AddHttpMessageHandler<DebugHttpHandler>();   // HttpDebug açıksa model istek/yanıtları loglanır
});

services.AddSingleton<IMcpToolCaller, McpToolCaller>();          // Flower.Core/Agents/McpToolCaller.cs
services.AddFlowerAgentsPersistence(configuration["Flower:Postgres:ConnectionString"]!);  // AgentsDbContext + EF store'lar
```
- `AddFlowerAgents` şunları yapar: `Flower:Agents` bölümünü bağlar; model/embedding typed HttpClient'larını, agent'ları, orkestratörü, retriever'ı, kaynakları (config'e göre), curator'ı ve feedback servisini kaydeder. Store'lar varsayılan olarak InMemory'dir (TryAdd).
- `AddFlowerAgentsPersistence` InMemory store'ları EF implementasyonlarıyla değiştirir (`Replace`).
- Testlerde (`TestHostFactory`) `AddFlowerAgentsPersistence` yerine InMemory store'lar kalır, `IChatModelClient` ve `IMcpToolCaller` fake ile değiştirilir.
- Açılışta `AgentsDbContext` migration'ını uygulamak için mevcut migration akışınıza `AgentsDbContext.Database.MigrateAsync()` eklenir (Flower'ın kendi migration'ını nasıl uyguladığına bakın).

## 2. `Flower.Core/Services/JobProcessorService.cs` — agent çağrısı
Bugün iş, doğrudan `IAgentService` ile model API'ye gidiyor. Yeni hâli:
```csharp
// ctor'a: IServiceScopeFactory zaten varsa scope içinden IAgentOrchestrator alın
var orchestrator = scope.ServiceProvider.GetRequiredService<IAgentOrchestrator>();

var outcome = await orchestrator.HandleAsync(
    new AgentRequest(
        RequestId: job.Id,
        UserEmail: user.Email,
        UserDisplayName: user.DisplayName,
        Question: job.Content,
        ConversationContext: contextText),   // ContextManager'dan: özet + son mesajlar (kısa tutun, ör. son 2K token)
    ct);

string reply = outcome.Kind switch
{
    AgentOutcomeKind.PassThrough => await agentService.GetReplyAsync(...),   // BUGÜNKÜ YOL, değişmeden
    _ => outcome.Message ?? "Bir sorun oluştu, lütfen tekrar deneyin."
};

// Sonrası bugünkü gibi: TextToHtml(reply) → TeamsMessagingService.SendAsync(..., "html")
// ConversationMessage'a assistant cevabı olarak reply yazılır (RAG bağlamı session geçmişine YAZILMAZ).
```
- `#Stop` iptali aynı `CancellationToken` ile orkestratöre geçer. Orkestratör iptalde `OperationCanceledException` fırlatır, mevcut akışınız bunu yakalıyor.
- `outcome.Kind == Failed` ise Job yine `Completed` olur (kullanıcıya mesaj gitti). Hata `AgentRun`'da tutulur.

## 3. `Flower.Domain/Commands/CommandParser.cs` + `Flower.Core/Commands/CommandDispatcher.cs` — yeni komutlar

| Komut | Handler | Davranış |
|---|---|---|
| `#Dogru [not]` | `IAgentFeedbackService.ConfirmLastAsync(email, note, ct)` | Son cevabı onaylar, bilgi deposuna yazar (docs/04 §4.4) |
| `#Yanlis <açıklama>` | `IAgentFeedbackService.DisputeLastAsync(email, note, ct)` | Son cevaba itiraz (§4.5). Açıklama boşsa: "Neyin yanlış olduğunu yazar mısın? #Yanlis <açıklama>" |
| `#Kaynak` | `IAgentFeedbackService.DescribeLastSourcesAsync(email, ct)` | Son cevabın kaynakları (§4.6) |

```csharp
public interface IAgentFeedbackService
{
    Task<FeedbackResult> ConfirmLastAsync(string userEmail, string? note, CancellationToken ct);
    Task<FeedbackResult> DisputeLastAsync(string userEmail, string note, CancellationToken ct);
    Task<FeedbackResult> DescribeLastSourcesAsync(string userEmail, CancellationToken ct);
}
public sealed record FeedbackResult(bool Success, string Message);   // Message düz metin; dispatcher HTML'e çevirir
```
- Komut kuralları mevcut parser'la aynı: `#` başta, case-insensitive. `#ASK` özel kuralı değişmez.
- `#Dogru`, LLM çağrısı (KnowledgeWriter) içerir; dispatcher'ı (polling döngüsünü) bloklamaması için:
  - **Seçenek A (önerilen):** Dispatcher hemen "Kaydediyorum…" cevabı verir ve işi `JobSource` = yeni bir `Feedback` tipiyle kuyruğa alır. `JobProcessorService` bu tip için `ConfirmLastAsync` çağırıp sonucu gönderir.
  - **Seçenek B:** `Task.Run` ile arka planda çalıştırıp sonucu `TeamsMessagingService` ile gönderir (daha basit ama #Stop ve restart'a dayanıksız).
- `#Help` tablosuna üç komut eklenir.
- `CommandParserTests`'e her komut için başta / ortada / sonda testleri eklenir.

## 4. `Flower.Core/Agents/` — adaptörler (bu faz)
```
Flower.Core/Agents/
├── McpToolCaller.cs          IMcpToolCaller (docs/04 §3.3)
└── McpOptions.cs             Flower:Mcp:Servers:<ad>:{Url | Command, Arguments}
```

## 5. WinForms (opsiyonel, bu fazda gerekmez)
- MainForm'a "Agent" sekmesi: son AgentRun'lar (zaman, kullanıcı, soru, karar, güven, süre, geri bildirim).
- `IAgentRunStore.ListRecentAsync(take)` ile okunur. Mevcut VirtualMode ListView ve `_applying` deseni kullanılır.
- `SettingsForm`: `Flower:Agents` alanları ve yeni secret'lar.

## 6. Testler (`tests/Flower.Tests/Agents/`)
- `FakeChatModelClient`: `request.AgentName`'e göre sıradaki hazır cevabı döner; gelen istekleri kaydeder (assert için).
- `FakeMcpToolCaller`: tool adına göre hazır JSON döner; istenirse exception fırlatır.
- `TestHostFactory`: `services.AddFlowerAgents(config)` + fake'lerin `Replace` ile eklenmesi. InMemory store'lar kalır.
- Hiçbir test gerçek LLM'e veya MCP'ye gitmez.
- Test listesi: `docs/07` §3.

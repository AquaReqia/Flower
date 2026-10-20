# Flower (Flow Follower) — Proje Yapısı ve Mimari Bağlam

Aşağıdaki promptu başka bir AI aracına vererek geliştirme/iyileştirme desteği alabilirsin. Prompt, projenin mevcut yapısını, mimari kararlarını, teknoloji yığınını, dosya organizasyonunu, belirgin servis akışlarını, bilinen sorun/iyileştirme alanlarını ve **bir sonraki faz olan "Kurumsal Öğrenme Ajanı" hedef mimarisini** (Bölüm 18–26) anlatır. Karşı tarafın somut kod önerileri sunabilmesi için gerekli tüm bağlam buradadır.

> **Güvenlik notu (paylaşmadan önce):** Bu dosya iç sistem adresleri (Bifrost, proxy, Bitbucket), servis hesabı, kullanıcı e-postaları ve Teams chat ID'leri içerir. Kurum dışı bir AI aracına vermeden önce bunları `<BIFROST_URL>`, `<SERVICE_ACCOUNT>` gibi yer tutucularla değiştir. Secret değerleri zaten dosyada yok; öyle kalmalı.

---

## ROL

Sen kıdemli bir .NET/C# mimarı ve LLM agent sistemleri tasarımcısısın. Aşağıda anlatılan "Flower" projesinin yapısını oku, anla ve son kullanıcıdan gelen geliştirme/iyileştirme taleplerini bu yapıya uygun, tutarlı ve mevcut kod tarzıyla uyumlu şekilde çöz. Önerilerin somut olsun; soyut "iyi pratik" tavsiyesi yerine, dosya yolu + metot imzası + kod parçası düzeyinde yaz.

Bölüm 1–17 **mevcut ve çalışan** sistemi (Faz 1: agent kabuğu) anlatır. Bölüm 18–26 **henüz yazılmamış** hedef mimariyi (Faz 2+: Confluence RAG, kurum modeli, proaktif soru soran ajan) anlatır. Yeni özellik önerirken mevcut kabuğu bozma, hedef mimariye doğru adım adım genişlet.

## 1. PROJE ÖZETİ

Flower (Flow Follower), Microsoft 365 / Teams üzerinden bire bir (1:1) mesajları otomatik olarak alan, kullanıcı bazlı kalıcı konuşma session'ları yöneten ve kurumsal bir model API'si (OpenAI uyumlu `/v1` endpoint) üzerinden agent yanıtlarını yine aynı Teams sohbetine ileten bir **arka plan uygulaması**dır. İlk sürüm agent'ın "kabuğu" hedeflenir: Teams bağlantısı, mesaj toplama, komutlar, session yönetimi, kullanıcı bazlı iş kuyruğu, durdurma/onay akışları, kalıcılık ve yerel Windows Forms takip arayüzü.

### Nihai hedef (Faz 2+)
Flower'ın asıl amacı **kurumun yapısını öğrenen bir ajan** olmaktır. Sistem aynı anda iki modda çalışır ve iki mod ortak bir çekirdeği paylaşır:
- **Reaktif mod:** Kullanıcı soru sorar → ajan Confluence (CQL + vektör arama) ve öğrenilmiş kurum modelinde arar → bilgi yeterliyse kaynaklı cevap verir; yetersizse ilgili kurum içi uzmana Teams'ten soru sorar, cevabı kullanıcıya iletir ve kalıcı bilgiye dönüştürür.
- **Proaktif mod:** Ajan, kurum hakkındaki eksik/çelişkili/eskimiş bilgileri kendisi tespit eder, bunlardan soru üretir, doğru kişiye sorar, cevabı doğrulayıp kurum modeline ve (onaylıysa) Confluence'a yazar.
- Cevaplar **kurumun kendi içinden** (Confluence + çalışanlar) gelir; model genel bilgisinden cevap uydurmaz.

### Hedef akış (uçtan uca)
1. Kullanıcı Teams'ten bire bir mesaj yazar (örn. `Selam`, `Bankada en yaygın kullanılan programlama dili nedir?`, `#ASK`).
2. Flower 30 sn'de bir Graph API'yi poll eder, yeni mesajları toplar, kronolojik sıralar.
3. Komutlar (`#` ön ekli) uygulama içinde işlenir; agent'a gönderilmez.
4. `#ASK` gelene kadar normal mesajlar `PendingMessage` tablosunda biriktirilir.
5. `#ASK` gelince birikmiş metin (varsa inline argüman) birleştirilip `Job` oluşturulur, kuyruğa alınır.
6. `JobProcessorService` kullanıcı başına 1 iş olacak şekilde paralel işletir; agent yanıtı üretilince `TeamsMessagingService` üzerinden Teams'e HTML olarak gönderilir.
7. Konuşma geçmişi (`ConversationMessage`) ve kullanım istatistikleri kalıcı olarak PostgreSQL'e yazılır.

## 2. TEKNOLOJİ YIĞINI

- **Dil/Çalışma zamanı:** C# / .NET 10
- **Uygulama modeli:** Worker Service (`IHostedService`/`BackgroundService`) + Windows Forms (yerel takip arayüzü)
- **Kalıcılık:** PostgreSQL 17 (portable, `127.0.0.1:5432`, db `flower`, user `postgres`, şifresiz geliştirme). EF Core + Npgsql provider. Migration'lar `Flower.Domain` projesinde.
- **Test:** xUnit, 89 test, SQLite in-memory (`:memory:`) ile PostgreSQL'ye yazmadan.
- **Windows Forms:** VirtualMode ListView (büyük listeler için), `TabControl` ile dinamik session tab'ları, `SplitContainer` ile boyutlandırılabilir paneller.
- **HTTP:** `IHttpClientFactory` + `HttpClientHandler` (system proxy + Windows default credentials → kurumsal proxy `gmproxy.kfs.local:8080` çalışır). Debug loglama için `DebugHttpHandler` pipeline'a eklenir.
- **Logging:** `Microsoft.Extensions.Logging` + özel `InMemoryLoggerProvider` (UI log paneli için ring buffer, 1000 satır, sequence ID, local time).
- **Secrets:** `Microsoft.Extensions.Configuration.UserSecrets` (repoya yazılmaz). `UserSecretsId: dotnet-Flower-Core-9f453167-4412-4ff0-8da1-60261855addf`.

## 3. PROJE ORGANİZASYONU (Solution)

```
Flower.slnx
├── src/
│   ├── Flower.Domain/            (classlib, net10.0) — Entities, interfaces, config, EF DbContext, migrations
│   ├── Flower.Infrastructure/    (classlib, net10.0) — Service implementasyonları, Graph, Model API, HTTP debug
│   ├── Flower.Core/              (classlib, net10.0) — Background services, command dispatcher, host setup
│   └── Flower.WinApp/            (winexe, net10.0-windows) — Windows Forms (MainForm, SettingsForm)
├── tests/
│   └── Flower.Tests/             (xunit, net10.0) — Domain + Integration testleri
└── smoketest/
    └── Flower.SmokeTest.csproj   (console) — Uçtan uca smoke test (gerçek Graph + model API)
```

### Katmanlı mimari kuralı
- `Flower.Domain`: sıfır bağımlılık. Entities, servis arayüzleri, config (`FlowerOptions`), `FlowerDbContext`, migration'lar, domain servisleri (`IJobQueue`, `ISessionService`, `IAgentService`, vb.)
- `Flower.Infrastructure`: `Flower.Domain`'e bağımlı. Arayüzlerin somut implementasyonları (`JobQueue`, `SessionService`, `AgentService`, `TeamsMessagingService`, `GraphChatReader`, `ModelApiClient`, `ContextManager`, `SummarizerService` vb.).
- `Flower.Core`: `Domain` + `Infrastructure`'a bağımlı. Background servisler (`MessagePollingService`, `JobProcessorService`, `GracefulShutdownService`, `ModelWarmupService`), `CommandParser`+`CommandDispatcher`, `HostSetup` (DI container kurulumu).
- `Flower.WinApp`: `Core`'a bağımlı. `HostSetup.BuildHost` ile host oluşturur, `MainForm` ile takip/kontrol arayüzü.
- `Flower.Tests`: `Flower.Core` (internals visible) üzerinden `TestHostFactory` ile SQLite + fake'lerle test host kurar.

## 4. DOMAIN MODELİ (Entities)

8 entity, 9 tablo. Tüm tablolar `Id` (Guid PK), `CreatedAt`, `UpdatedAt` taşır.

- **User**: `Email` (kararlı kimlik, unique), `DisplayName`, `SelectedSessionId` (FK→Session, nullable), `LastReminderAt` (nullable). Navigation: `Sessions`, `Jobs`, `PendingMessages`. Tenant: `ykteknoloji.com.tr`.
- **Session**: `Id` (Guid), `UserId` (FK), `SessionKey` (örn. `Osman-19102026-5F3A`), `Title`, `CreatedAt`, `LastActiveAt`. Navigation: `User`, `Messages`. **Not:** `Session.Jobs` navigation yok (Job.SessionId var).
- **Job**: `Id`, `UserId`, `SessionId`, `ChatId` (Teams chat ID), `Content` (birleşmiş metin), `State` (`JobState` enum), `Source` (`JobSource` enum), `StartedAt`, `FinishedAt`, `ErrorMessage`. Navigation: `User`.
  - `JobState`: Pending, Running, Completed, Failed, Stopped, Interrupted, Merged.
  - `JobSource`: Teams (0), Manual (1).
- **ConversationMessage**: `Id`, `SessionId`, `Role` (`user`/`assistant`/`system`/`summary`), `Content`, `CreatedAt`, `TokenCount`.
- **PendingMessage**: `Id`, `UserId`, `Content`, `CreatedAt`. #ASK gelene kadar birikmiş metinler.
- **IngestedMessage**: `Id`, `MessageId` (Graph mesaj ID, unique), `ChatId`, `SenderEmail`, `State` (Pending/Ignored/Processed), `ReceivedAt`, `ProcessedAt`. Idempotency için.
- **PendingSessionTransition**: `Id`, `UserId`, `FromSessionId`, `ToSessionId`, `RequestedAt`, `State` (Pending/Approved/Cancelled/Completed). Session geçiş onayı.
- **SessionSummary**: `Id`, `SessionId`, `SummaryText`, `CreatedAt`, `TokenCount`. Context compaction sonrası özet.

### SessionKey biçimi
`<AdSoyAd>-<DDMMYYYY>-<UniqUID>` — örn. `Osman-19102026-5F3A`. `SessionKeyFactory` üretir; UID uzunluğu config (`Session:UidLength`, default 4).

## 5. KONFIGÜRASYON (`FlowerOptions`)

`Flower.Domain/Configuration/FlowerOptions.cs` — `Flower` section'ı. Flat notation (duplicate key hatası çözüldü).

```csharp
FlowerOptions {
  Polling: { IntervalSeconds: 30, MaxRetryAfterSeconds: 300, NotifyOnStartup: true, LookbackSeconds: 90 }
  Session: { UidLength: 4 }
  Reminder: { MinIntervalHours: 1, Text: "<html>..." }
  Queue: { MaxConcurrentUsers: 5 }
  Tenant: { AllowedDomain: "ykteknoloji.com.tr", AllowExternalTenants: false }
  Model: {
    BaseUrl: "https://bifrost-proxy-server-dsp-ddi.apps.lan-dev-dc1-ocp.yapikredi.com.tr/v1",
    DefaultModel: "vllm-qwen38-flash-next/Qwen/Qwen3.8-Flash-Next-FP8",
    ApiKey: <secret>,
    ContextLimit: 0,  // 0 = otomatik (model kartından al)
    RequestTimeoutSeconds: 120,
    MaxRetries: 3
  }
  Graph: {
    BaseUrl: "https://graph.microsoft.com/v1.0",
    AccountEmail: "messenger.pigeon@yapikredi.onmicrosoft.com",
    ClientId, TenantId, ClientSecret: <secret>,
    Scope: "https://graph.microsoft.com/.default",
    SmokeTestEmail: "osman.yilmaz@ykteknoloji.com.tr",
    KeyVault: { VaultUrl: <nullable> },
    MessengerUsername: <nullable>, MessengerPassword: <secret>,
    TokenCachePath: "graph-token-cache.json"  // DPAPI CurrentUser şifreli
  }
  Postgres: { ConnectionString: <secret> }
  HttpDebug: { Enabled: true, MaskSecrets: false, LogDir: "logs/http" }  // production'da Enabled=false, MaskSecrets=true
}
```

## 6. KOMUT SİSTEMİ

`CommandParser` (`Flower.Domain/Commands/CommandParser.cs`) — `#` ön ekli, case-insensitive. Komut listesi:

| Komut | Davranış |
|---|---|
| `#Help` | Komut yardımı (HTML tablo). Agent çağrılmaz. |
| `#Session` | Seçili session bilgisi. |
| `#Sessions` | Kullanıcının session listesi. |
| `#NewSession` | Yeni session oluşturur, geçiş akışını uygular. |
| `#ChangeSession <key>` | Mevcut session'a geçiş. |
| `#ASK [metin]` | Birikmiş/inline metni agent'a gönderir, kuyruk kuralı uygulanır. |
| `#Stop` | Kullanıcının çalışan işini durdurur (HTTP iptali). |
| `#Approve` | Bekleyen session geçişinde durdurmayı onaylar. |
| `#Cancel` | Bekleyen session geçişini iptal eder. |
| `#Retry` | Kesintiye uğramış işi yeniden dene. |
| `#Abort` | Kesintiye uğramış işi bırak. |

### #ASK özel kuralı (son karar)
`#ASK` metnin **başında veya sonunda** olabilir, **ortada olmamalı**. Sadece `#ASK` için geçerli; diğer komutlar `#` ile başlamak zorunda.
- `#ASK selam` → komut, inline = "selam" ✅
- `selam #ASK` → komut, inline = "selam" ✅
- `selam #ask nasil` → komut DEĞİL, normal mesaj (ortada) ❌
- `merhaba #stop` → komut DEĞİL (diğer komutlar başta olmalı) ❌

`CommandParser.TryParseAskAtEnd` metodu `#ASK` sonda ise yakalar.

### Komut yanıtları HTML formatında
`CommandOutcome.IsHtml = true` (default). `TeamsMessagingService.SendAsync(chatId, content, ct, contentType="text"|"html")`. `HtmlEscape` ve `TextToHtml` helper'ları. Teams destekli tag'ler: `<p>`, `<div>`, `<br>`, `<strong>`, `<em>`, `<ul>`, `<li>`, `<table>`, `<hr>`, `<h1>-<h3>`, `<code>`.

## 7. BACKGROUND SERVİSLERİ (`Flower.Core/Services/`)

### MessagePollingService (`BackgroundService`)
- 30 sn'de bir Graph'ı poll eder. 90 sn lookback (eski mesajları kaçırma).
- İlk poll'da SmokeTest (gerçek Graph token + "Basladim" mesajı → `SmokeTestEmail`) ve `NotifyPendingUsersAsync` (Flower kapalıyken gelen mesajları olan kullanıcılara bildirim).
- SmokeTest ve Startup bildirimi **process başına bir kez** (`static Interlocked` flag).
- Manuel tetikleme: `_pollSignal` (SemaphoreSlim), `TriggerPollNow()` ile.
- Graph mesaj sırası tuzağı: `/chats/{id}/messages` DESC döner. Önce tüm mesajları topla, ascending sırala, sonra dispatch.
- Sayfalama 3 kural: EMPTY / items < $top / early-stop.
- `IsRunning`, `IntervalSeconds`, `LastPollAt` public (UI countdown için). `_intervalSeconds = 0` → durmuş.
- `_lastPollAt` poll'un **başında** alınır, **sonunda** set edilir (mesaj olsa da olmasa da).
- `StopAsync` override ile log + Stopwatch.

### JobProcessorService (`BackgroundService`)
- 2 sn'de bir pending job'ları işler. `MaxConcurrentUsers` (SemaphoreSlim) ile paralel, kullanıcı başına 1 iş.
- `JobCancellationRegistry` (`Singleton`) — #Stop → HTTP iptali için CancellationTokenSource kaydı.
- `_running` ConcurrentDictionary<Guid, CancellationTokenSource>.
- Job tamamlanınca: yanıt Teams'e HTML olarak gönderilir, `ConversationMessage` yazılır, transition varsa `OnJobFinishedAsync`.
- `MarkInterruptedOnStartupAsync` — açılışta "Running" job'ları "Interrupted" yap.
- `TextToHtml`, `HtmlEscape` helper'ları.
- `StopAsync` override ile log.

### GracefulShutdownService (`IHostedService`)
- `IHostApplicationLifetime.ApplicationStopping`'e `OnStopping` register.
- `OnStopping`: "Running" job'ları "Interrupted" yap. **Önemli:** UI thread'de deadlock olmasın diye `Task.Run` + 5 sn timeout (async-over-sync çözümü).
- `StopAsync` override ile log.

### ModelWarmupService (`IHostedService`)
- Açılışta `/v1/models` çağırıp context limit cache'ler. Hata olursa güvenli varsayılan (128K).
- `StopAsync` override ile log.

## 8. COMMAND DISPATCHER (`Flower.Core/Commands/CommandDispatcher.cs`)

`ICommandDispatcher.DispatchAsync(email, displayName, chatId, text, ct)` — `CommandOutcome` döner (`Handled`, `ReplyMessage`, `IsHtml`, `SendReminderFirst`).
- Önce reminder kontrolü (her mesajda, #ASK dahil). Süre dolmuşsa `SendReminderFirst = true`.
- Komut ayrıştırma → handler'a yönlendirme.
- Normal mesaj → `UserService.AppendPendingAsync` ile biriktir.
- `#ASK`: `pending + "\n\n" + inline` birleşimi ile `JobQueue.EnqueueAsync`. Birikmiş + inline yoksa hata yanıtı.
- `#Stop`: `JobQueue.StopAsync(userId)` → `JobCancellationRegistry`'den token cancel → HTTP iptali.
- Session geçiş: `SessionTransitionService.RequestTransitionAsync` — çalışan iş varsa onay bekle, yoksa direkt geç.

## 9. INFRASTRUCTURE SERVİSLERİ

### Graph entegrasyonu (`Flower.Infrastructure/Graph/`)
- `GraphAuthenticator` — ROPC (iki aşamalı token): önce `KeyVaultSecretClient` ile messenger şifresi al, sonra `login.microsoftonline.com` ile token. `refresh_token` ile sessiz yenileme. Token cache `graph-token-cache.json` (DPAPI CurrentUser şifreli).
- `GraphChatReader` (`IGraphChatReader`, **Singleton**) — `/me/chats`, `/chats/{id}/messages`, `markChatReadForUser`. `GetSelfEmailAsync` (`/me` ile dinamik self identity: `messenger.pigeon@yapikredi.onmicrosoft.com`, userId `fe3b22ff-8c5b-4505-84b6-8a60f44d123b`). `IsSelfChat` (chat ID'de iki userId de self ise atla). Sayfalama 3 kural.
- `InMemoryGraphChatReader` — dev/test, gerçek Graph config yoksa.
- `KeyVaultSecretClient` — Azure Key Vault HTTPS ile secret alma (kurum proxy'si ile).

### Model API (`Flower.Infrastructure/ModelApi/`)
- `ModelApiClient` — `/v1/chat/completions` OpenAI uyumlu. Streaming yok (bu sürüm). Bifrost virtual key `x-bf-vk` header'ı + `Authorization: Bearer`.
- `ModelInfoProvider` — `/v1/models` çağırıp context limit dinamik alır. `ContextLimit=0` config'de ise otomatik mod. `KnownModelLimits` fallback tablosu.
- Qwen/Bifrost: "System message must be at the beginning" → SummaryBlock tek system mesajına birleştirildi.

### Context yönetimi (`Flower.Infrastructure/Context/`)
- `ContextManager` (`IContextManager`) — OpenCode yaklaşımı: **özet (8 başlık) + yakın geçmiş (15K token)**. Compaction threshold'ları aşınca `SummarizerService` çağrılır, `SessionSummary` yazılır, eski mesajlar summary + yakın mesajlarla değiştirilir.
- `SummarizerService` — model API ile özet üretir. `SummarizerPrompt` template.
- `UsageTracker` (`IUsageTracker`) — token kullanım istatistikleri.
- `TokenEstimator` — char/4 basit tahmin (token API yok).

### Mesaj akışı servisleri
- `MessageIngestService` — tenant filtre + idempotency (`IngestedMessage`) + self filter (`/me`'den gerçek email ile, config `AccountEmail` yanlış). `GetSelfEmailCachedAsync`.
- `TeamsMessagingService` — `POST /chats/{chat-id}/messages` ile HTML/text gönderme. `contentType` parametresi.
- `StartupNotifier` — Flower kapalıyken gelen mesajları olan kullanıcılara "acildim" bildirimi. Self email filtre, HTML mesaj.
- `UserService` — `PendingMessage` yönetimi. `AppendPendingAsync`, `GetPendingTextAsync`, `ClearPendingAsync`.
- `JobQueue` — kullanıcı bazlı kuyruk, kuyruk birleştirme (#ASK B'ye ekleme). `StopAsync(userId)` → cancel.
- `SessionService` — session CRUD, `SessionKeyFactory` ile key üretimi.
- `SessionTransitionService` — geçiş onay akışı (`PendingSessionTransition`).
- `ReminderService` — saatlik hatırlatma kontrolü.

### HTTP debug (`Flower.Infrastructure/HttpDebug/`)
- `DebugHttpLogger` (`Singleton`) — `HttpDebug:Enabled=true` ise `logs/http/{graph,model}/` altına istek/yanıt yazar. `MaskSecrets=true` ile API key, Authorization, client secret gibi alanlar maskelenir.
- `DebugHttpHandler` — `DelegatingHandler`, pipeline'a eklenir.

## 10. WINDOWS FORMS ARAYÜZÜ (`Flower.WinApp/`)

### MainForm (`MainForm.cs` + `MainForm.Designer.cs`)
- **VirtualMode ListView**: `_usersList` (sol, kullanıcılar), `_jobsList` (sağ, job'lar). `RetrieveVirtualItem` + `CacheVirtualItems` ile sadece görünür satırlar çizilir. `ListRows.cs` (`UserRow`, `JobRow`, `SessionRow` record'ları).
- **Sağ panel dinamik tab'lar**: `_rightTabs` TabControl (Dock=Top), tek `_jobsList` (Dock=Fill) altta paylaşılır. Kullanıcı seçilince o kullanıcının her session'ı için bir TabPage dinamik oluşturulur (tab başlığı = SessionKey, Name = SessionId). İlk tab sabit **"Tümü"** (Name=`__ALL__`, tüm session'ların job'ları). Aktif session (User.SelectedSession) seçili gelir. Tab değişince `_selectedSessionId` güncellenir, job listesi o session'a göre filtrelenir.
- **Event handler'lar designer'da** (`InitializeComponent`): Tüm event bağlama (Click, SelectedIndexChanged, RetrieveVirtualItem, CacheVirtualItems, Tick) `MainForm.Designer.cs`'te. Handler'lar `MainForm.cs`'te instance metot. Designer'da butona çift tıklayınca mevcut event düzenlenir, yenisi oluşmaz. `WireUpEvents` metodu kaldırıldı.
- **Sonsuz döngü önlemi**: `_applying` bool flag. `ApplyUsersAndJobsToUi` başında `true`, sonunda `false` (finally). Event handler'lar `if (_applying) return;` ile erken çıkış. `RightTabs_SelectedIndexChanged` için ek çift koruma: event `-= / +=`.
- **Tab seçimi koruma**: `_userSelectedTab` bool flag. Kullanıcı elle tab seçince `true`, kullanıcı değişince `false` (yeni kullanıcının aktif session'ı seçilsin).
- **Timer'lar**:
  - `_refreshTimer` (2 sn): `RefreshAsync` → DB'den kullanıcı/job/session çeker, `ApplyUsersAndJobsToUi`. **Polling kısmını dokunmaz** (countdown `_statusTimer`'da).
  - `_statusTimer` (500ms): `UpdateStatusCountdown` → status bar'da "son polling Xs, Ys kaldi" countdown. **Polling kısmını yalnızca bu yönetir.**
- **IHost restart**: `Microsoft.Extensions.Hosting.Host` restart desteklemiyor. `_host` nullable. "Polling Durdur" → `StopAsync` + `Dispose` + `_host = null`. "Polling Başlat" → `_host = HostSetup.BuildHost([])` + `StartAsync`. Tüm buton handler'ları null-safe (MessageBox uyarısı). `RefreshAsync` host null ise `ApplyEmptyToUi` (boş listeler, "Tümü" tab).
- **SplitContainer**: `_outerSplit` (Orientation=Vertical) — üstte `_split` (users+tabs), altta `_logBox`. Drag ile log yüksekliği ayarlanır.
- **Log panel**: `InMemoryLogSink`'ten sequence ile yeniler, ring buffer 1000 satır, local time. `BeginInvoke` ile UI thread'e marshal.
- **OnFormClosing**: `_refreshTimer.Stop()`, `_statusTimer.Stop()`, `_host.StopAsync(TimeSpan.FromSeconds(10))` (sync, 10 sn timeout), `_host.Dispose()`, `Application.Exit()`. Logger null-safe (NullLogger fallback). `OnStopping` deadlock çözüldü (`Task.Run` + timeout).

### SettingsForm (`SettingsForm.cs`)
- Config değerlerini gösterir (secret dahil). Kaydet → secret'lar `user-secrets`'a (`secrets.json`), diğerleri `appsettings.Development.json`'a. `UserSecretsId` Flower.Core.csproj'tan okunur.

## 11. TEST YAPISI (`tests/Flower.Tests/`)

- **TestHostFactory** — `HostSetup.BuildHost` çağırır, `configureServices` ile SQLite in-memory, `InMemoryGraphChatReader`, background servisleri olmadan test host kurar.
- **Domain testleri**: `CommandParserTests` (34 test, #ASK başta/sonda/ortada), `ContextManagementTests`, `InMemoryLogSinkTests`, `KnownModelLimitsTests`, `ModelInfoProviderTests`, `SessionKeyFactoryTests`.
- **Integration testleri**: `CommandDispatcherIntegrationTests`, `JobProcessorFlowTests`, `PollingFlowTests`, `StartupNotifierTests`.
- **Kural**: Testler SQLite in-memory (`:memory:`) kullanır, PostgreSQL'ye yazmaz. User-secrets duplicate key hatası çözüldü (flat notation).

## 12. SMOKE TEST (`smoketest/Flower.SmokeTest.csproj`)

Console uygulaması. `HostSetup.BuildHost` ile gerçek host (gerçek Graph + model API). Süre parametresi (`dotnet run -- 120`) veya `--ask <email> <chatId> <content>` ile manuel #ASK test. `InMemoryLogSink` console'a yazılır.

## 13. ÖNEMLİ KARARLAR VE ÖZELLİKLER

- **Tenant**: Yalnızca `ykteknoloji.com.tr`. `AllowExternalTenants=false`.
- **Kullanıcı kimliği**: e-posta adresi (kararlı).
- **Session kimlik biçimi**: `<AdSoyAd>-<DDMMYYYY>-<UniqUID>`.
- **#Stop**: HTTP iptali yeterli (Seçenek A), kullanıcıya "durduruldu" doğrulanmadan bildirilmez.
- **Context yönetimi**: OpenCode yaklaşımı — özet (8 başlık) + yakın geçmiş (15K token).
- **Dinamik context limit**: config `ContextLimit=0` = otomatik mod (model kartından al).
- **Graph auth**: ROPC (iki aşamalı token), `refresh_token` ile sessiz yenileme.
- **Token cache**: `graph-token-cache.json` (DPAPI CurrentUser şifreli).
- **Self identity**: `/me` ile dinamik (`messenger.pigeon@yapikredi.onmicrosoft.com`, userId `fe3b22ff-8c5b-4505-84b6-8a60f44d123b`).
- **GraphChatReader Singleton**: self identity ve seen-id set'leri instance-level.
- **Manuel polling**: `_pollSignal` SemaphoreSlim, `TriggerPollNow()`.
- **Graph mesaj sırası tuzağı**: `/chats/{id}/messages` DESC döner — `PollOnceAsync` önce toplar, ascending sıralar, sonra dispatch.
- **MessagePollingService concrete tip DI'a register**: `AddSingleton` + `AddHostedService(sp => sp.GetRequiredService<...>())`.
- **Self chat filtreleme**: `IsSelfChat` (chat ID'de iki userId de self ise atla).
- **MessageIngestService self filter**: `/me`'den gerçek email (config AccountEmail yanlış).
- **Teams HTML contentType**: komut yanıtları HTML, agent yanıtları `TextToHtml` (escape + \n→`<br>`).
- **WinForms designer**: event handler'lar `InitializeComponent`'te, kod seviyesinde değil. `LicenseManager.UsageMode == Designtime` ile designer modunda host başlatma.
- **VirtualMode ListView**: `RetrieveVirtualItem` içinde `item.Selected` atanmaz (StackOverflow).
- **Bifrost/Qwen**: "System message must be at the beginning" — SummaryBlock tek system mesajına birleştirildi.
- **Graph bug**: `@odata.nextLink` var ama `value` boş gelebiliyor — 3 kural sayfalama koruması.
- **Repo**: Bitbucket `https://sdlc.yapikredi.com.tr/bitbucket/scm/~u090548/flower-ai-agent.git`, `origin/master` upstream bağlı.

## 14. ÇALIŞTIRMA / BUILD / TEST KOMUTLARI

```powershell
# PostgreSQL portable başlat (admin yetki yok)
.\postgres-portable\start.ps1

# Build (hedef: sıfır warning, sıfır error)
dotnet build Flower.slnx

# Tüm testler (89 test, SQLite in-memory)
dotnet test tests/Flower.Tests/Flower.Tests.csproj

# Smoke test (120 sn, gerçek Graph + model)
dotnet run --project smoketest/Flower.SmokeTest.csproj -- 120

# Manuel #ASK smoke test
dotnet run --project smoketest/Flower.SmokeTest.csproj -- 60 --ask osman.yilmaz@ykteknoloji.com.tr "19:1e714602-04ee-441f-b400-adffe6e69fbc_fe3b22ff-8c5b-4505-84b6-8a60f44d123b@unq.gbl.spaces" "Merhaba"

# WinApp çalıştır
dotnet run --project src/Flower.WinApp/Flower.WinApp.csproj

# Git push (upstream bağlı)
git push
```

## 15. BİLİNEN SORUN / İYİLEŞTİRME ALANLARI (gelecek geliştirme için)

1. **Canlıya çıkış**: Polling → Azure Event Hubs + Graph change notifications (SPEC 3.2). Şu an 30 sn polling riskli (Microsoft throttling).
2. **Job.MergedIntoJobId + JobState.Merged**: DB'den kaldıran yeni migration (kullanılmıyor).
3. **HTTP debug log'ları**: `appsettings.json` `HttpDebug:Enabled: false`, `MaskSecrets: true` (production güvenliği).
4. **`KeyVaultSecretClient` ve `GraphAuthenticator`**: `DEBUG secret source/value` ve `DEBUG ROPC credentials` loglarını kaldır (production).
5. **Session transition onay timeout'u**: Henüz yok. Aynı anda `#NewSession` + normal mesaj + `#Stop` çakışması.
6. **Düzenlenen/silinen Teams mesajları, dosya/görsel, reaction, quote**: Desteklenmiyor, kapsam dışı.
7. **Teams okundu işaretleme**: `markChatReadForUser` çağrılıyor ama yeni gelen mesajları kaçırmayacak akış teyit edilmeli.
8. **Outbox pattern**: DB kaydetme + kuyruk/session durum değişikliği tutarlılığı için transaction/outbox tasarımı henüz yok.
9. **Gönderilmemiş metin restart sonrası**: `PendingMessage` kalıcı ama restart davranışı teyit edilmeli.
10. **Sunucu çalışması**: Windows Forms'a bağımlı değil olmalı (SPEC 13). Şu an `Flower.WinApp` WinExe, sunucu için console/worker proje eklenebilir.
11. **`#Sessions` komutu**: Listeleme davranışı henüz net değil (SPEC açık karar).
12. **Token süresi**: `refresh_token` ile sessiz yenileme ama süresiz müdahalesiz oturum garantisi yok (SPEC 3.3).
13. **Self-mesaj döngüsü (Faz 2 ile kritikleşir)**: Flower uzmanlarla yeni chat açıp mesaj gönderecek; gönderilen her mesajın `MessageId`'si `IngestedMessage`'a `Ignored` olarak yazılmalı ki polling kendi mesajını soru/cevap sanmasın (self email filtresine ek güvence).
14. **Polling kapsamı**: Proaktif modda açık chat sayısı artacak; `/me/chats` taramasında yalnızca `lastUpdatedDateTime` lookback içindeki chat'lerin mesajları çekilmeli (throttling).
15. **`AgentService` tek çağrı**: Tool calling, JSON schema çıktı ve agent başına model desteği yok (bkz. Bölüm 22.6).

## 16. KOD TARZI VE KURALLAR

- C# 13 + .NET 10. File-scoped namespace. Nullable enable. `record` / `record struct` tercih.
- XML doc comment'lar Türkçe, kısa. Karmaşık mantık için uzun yorum.
- Async metotlar `Async` soneki. `CancellationToken` parametre adı `ct` veya `stoppingToken`.
- DI: `AddScoped` (DbContext bağımlı servisler), `AddSingleton` (cache'ler, registry'ler, Singleton reader'lar). `AddHostedService` + concrete tip `AddSingleton` çift kayıt (manuel resolve için).
- EF Core: `AsNoTracking` read'lerde. `Include` minimal. Navigation yerine explicit join query (N+1 önlemek için).
- Logging: `_log.LogInformation("...{Var}...", val)` (structured). Timing loglar `TIMING ...` prefix ile.
- Exception handling: `catch (OperationCanceledException) when (ct.IsCancellationRequested)` ile cancel'ı sessiz yut. Diğer exception'ları logla. UI thread'de async-over-sync deadlock'tan kaçın (`Task.Run` + timeout veya tam async).
- WinForms: UI thread dışından kontrole erişim `InvokeRequired` + `BeginInvoke`. `BeginUpdate`/`EndUpdate` ile flicker önle.
- Git commit message: `type: kısa açıklama (Turkçe, diacriticsiz)` + boş satır + detay. `type`: feat/fix/refactor/docs/test/chore.

## 17. ŞU ANKİ DURUM (commit `b55abb5`, master)

- 89/89 test geçti. Build sıfır warning, sıfır error.
- Uçtan uca akış çalışıyor: gerçek Graph polling, smoke test, #ASK → agent → Teams yanıtı.
- Repo Bitbucket'a push edildi.
- Son yapılanlar: IHost restart sorunu (host dispose + yeniden BuildHost), status bar timer çakışması, #ASK metnin sonunda, `_lastPollAt` mesaj yoksa güncellenmiyordu, `OnStopping` deadlock (`Task.Run`), `JobProcessorService` `Task.Delay` `TaskCanceledException`, StopAsync log'ları.

---

---

# FAZ 2+ — KURUMSAL ÖĞRENME AJANI (HEDEF MİMARİ, HENÜZ YAZILMADI)

## 18. MEVCUT KABUKTAN HEDEFE: EKSİKLER ÖZETİ

Mevcut kabuk (Faz 1) Teams I/O, kuyruk, session ve kalıcılık tarafını çözmüş durumda. Hedefe ulaşmak için eksik olanlar:

| # | Eksik | Bugünkü durum | Gereken |
|---|---|---|---|
| E1 | **Agent katmanı** | `AgentService` tek bir chat completion çağrısı | Orkestratör + rollere ayrılmış agent'lar (Bölüm 20), tool calling, yapılandırılmış (JSON schema) çıktı |
| E2 | **Confluence entegrasyonu** | Yok | `IConfluenceClient` (REST + CQL), artımlı senkron, sayfa yetki bilgisi |
| E3 | **RAG / bilgi deposu** | Yok | Chunk + embedding + hibrit arama (CQL + vektör) + rerank |
| E4 | **Kurum modeli** | Yok | Varlık/ilişki tabloları + doğrulanmış bilgi (Fact) kayıtları, güven seviyesi, çürüme |
| E5 | **Giden soru (Inquiry) akışı** | Bot yalnızca gelen mesaja cevap veriyor | Uzmana proaktif 1:1 chat açma (`Chat.Create`), cevap korelasyonu, hatırlatma, zaman aşımı, başka uzmana geçiş |
| E6 | **Mesaj yönlendirme** | Her normal mesaj `PendingMessage`'a gider (#ASK bekler) | Kullanıcının açık bir Inquiry'si varsa gelen mesaj **uzman cevabı** olarak işlenmeli (Bölüm 22.1) |
| E7 | **Proaktif tetikleyiciler** | Yok | Gap Detector, Confluence senkronu, Graph org değişikliği taraması (zamanlanmış BackgroundService'ler) |
| E8 | **Soru havuzu + öncelik + kota** | `JobQueue` yalnızca kullanıcı #ASK işleri | Ortak `QuestionPool`, öncelik (reaktif > çelişki > rutin), kişi başı soru kotası, sessiz saatler, opt-out |
| E9 | **Org bilgisi** | Yalnızca `/me`, `/me/chats` | `User.Read.All` / `GroupMember.Read.All` ile yönetici zinciri, ekip üyelikleri |
| E10 | **Gözlemlenebilirlik** | `InMemoryLoggerProvider` + HTTP debug | OpenTelemetry trace (agent adımı başına span), self-hosted Langfuse |
| E11 | **Değerlendirme seti** | Yok | 100–200 gerçek Türkçe kurum sorusu; Judge doğruluğu ve model karşılaştırması |
| E12 | **Sunucu modu** | WinExe | `Flower.Worker` (console/Windows Service) projesi — proaktif mod 7/24 çalışmalı (bkz. Bölüm 15 madde 10) |
| E13 | **Uyum / onay** | — | KVKK/İK onayı, çalışan bilgilendirmesi, Confluence'a yazma yetkisi kararı |

## 19. HEDEF AKIŞLAR

### 19.1 Reaktif akış (mevcut #ASK akışının genişlemesi)
1. Kullanıcı `#ASK` ile soru gönderir → mevcut `Job` oluşur (değişmez).
2. `JobProcessorService` artık doğrudan `AgentService`'i değil `IAgentOrchestrator.HandleQuestionAsync(job, ct)`'yi çağırır.
3. **Router** → soru tipi (kurum bilgisi / sohbet / kapsam dışı). Sohbet ise mevcut davranış (düz chat completion).
4. **Query Planner** → alt sorular + CQL sorguları + TR/EN eş anlamlılar.
5. **Retriever** → Confluence CQL + vektör arama + kurum modeli (Fact) → birleştir → rerank → kullanıcının erişebildiği space'lerle filtrele.
6. **Sufficiency Judge** → `{ verdict: sufficient|partial|insufficient, missing: [...], confidence: 0-1 }` (JSON schema zorunlu).
7. `sufficient` → **Synthesizer** kaynaklı cevap yazar → Teams'e gönderilir (bugünkü yol).
8. `partial`/`insufficient` → kullanıcıya ara cevap ("şunu buldum, eksik kısmı X kişisine soruyorum") → `QuestionPool`'a reaktif öncelikli kayıt → Inquiry akışı (19.3).
9. Uzman cevabı gelince Synthesizer yeniden çalışır → **ilk kullanıcıya proaktif mesaj** (`TeamsMessagingService.SendAsync` ile kullanıcının mevcut chat'ine).

### 19.2 Proaktif akış
1. `GapDetectionService` (gece) eksikleri bulur: sahibi olmayan sistem/süreç, tanımsız kavram, eskimiş kritik sayfa, çelişen Fact'ler, reaktifte cevapsız kalmış sorular.
2. **Question Writer** eksikten kısa, doğrula/düzelt tipinde soru üretir → `QuestionPool` (düşük/orta öncelik).
3. `InquiryDispatchService` kota ve sessiz saat kurallarıyla soruları uzmanlara dağıtır.
4. Cevap → **Fact Processor** → **Verifier** → `KnowledgeFact` (beyan / doğrulanmış) → **Publisher** (RAG indeksine ve onaylıysa Confluence taslağına).

### 19.3 Inquiry (uzmana soru) akışı — iki modun ortak çekirdeği
1. **Expert Router** aday listesi üretir: sayfa sahibi/son düzenleyen → space sahibi → ekip lideri (Graph `manager`) → ekip üyesi → geçmişte doğru cevap vermiş kişi.
2. Kota kontrolü (`ExpertProfile`). Dolu ise sıradaki aday veya erteleme (reaktifte kota esnetilebilir).
3. Uzmanla 1:1 chat yoksa `POST /chats` (oneOnOne) ile oluşturulur, varsa mevcut chat kullanılır.
4. Soru gönderilir (format: Bölüm 22.2). `Inquiry.State = AwaitingAnswer`, `DueAt` set edilir.
5. Gelen mesaj → `MessageIngestService` → `InquiryResponseRouter` (Bölüm 22.1) → `Inquiry.State = Answered`.
6. `DueAt` geçti → hatırlatma (1 kez) → yine cevap yoksa `Expired` → sıradaki aday.
7. Cevap Judge'dan yetersiz dönerse en fazla 1 takip sorusu.
8. Aynı soruyu bekleyen birden fazla kullanıcı varsa (dedupe), cevap hepsine iletilir.

## 20. AGENT ROLLERİ VE KOD KARŞILIKLARI

Yeni klasör: `Flower.Infrastructure/Agents/` (implementasyonlar), arayüzler `Flower.Domain/Agents/`.

| Agent | Arayüz | Girdi → Çıktı | Model sınıfı |
|---|---|---|---|
| Orchestrator | `IAgentOrchestrator` | Job → AgentResult | — (deterministik C# akışı, LLM değil) |
| Router | `IIntentRouter` | soru → `IntentResult` | hafif |
| Query Planner | `IQueryPlanner` | soru → `QueryPlan` (alt sorular, CQL, anahtar kelimeler) | hafif |
| Retriever | `IRetriever` | `QueryPlan` → `RetrievedContext` | LLM yok (arama + rerank) |
| Sufficiency Judge | `ISufficiencyJudge` | soru + bağlam → `SufficiencyVerdict` | **güçlü** |
| Synthesizer | `IAnswerSynthesizer` | soru + bağlam + uzman cevapları → kaynaklı cevap | **güçlü** |
| Expert Router | `IExpertRouter` | eksik konu → `ExpertCandidate[]` | LLM yok (skorlama) + opsiyonel hafif |
| Question Writer | `IQuestionWriter` | eksik → kısa Türkçe soru metni | hafif |
| Fact Processor | `IFactExtractor` | uzman cevabı / Confluence sayfası → `KnowledgeFact[]` + çelişki listesi | orta/güçlü |
| Gap Detector | `IGapDetector` | kurum modeli → `KnowledgeGap[]` | orta |
| Verifier | `IFactVerifier` | Fact → güven seviyesi güncelle | kural tabanlı |
| Publisher | `IKnowledgePublisher` | doğrulanmış Fact → indeks / Confluence taslağı | LLM yok |
| Guard | `IContentGuard` | her çıktı → PII / yetki / spam kontrolü | kural tabanlı |

**Tasarım kuralları:**
- Orkestrasyon **deterministik C#** olarak yazılır (state machine). "Hangi agent çalışsın" kararı LLM'e bırakılmaz; LLM yalnızca her adımın içindeki işi yapar. Model değiştiğinde davranış stabil kalır.
- Judge, Router, Planner, Fact Processor çıktıları **JSON schema** ile zorlanır (`response_format: { type: "json_schema", ... }`). Şema dışı çıktıda 1 kez retry, sonra güvenli varsayılan (`insufficient`).
- Her agent'ın prompt'u ayrı dosyada (`Flower.Infrastructure/Agents/Prompts/*.md`, embedded resource) ve versiyonlu.
- Agent başına model seçimi config'den (`Flower:Agents:<AgentName>:Model`). Model değişimi kod değişikliği gerektirmemeli.
- Synthesizer yalnızca verilen bağlamdan cevap verir; her iddiaya kaynak (`[Sayfa başlığı](url)` veya "X kişisinin beyanı, tarih") ekler. Kaynak yoksa "bilmiyorum" der.
- Doğrulanmamış (`Claimed`) bilgi cevapta kullanılabilir ama "doğrulanmamış beyan" etiketiyle gösterilir.

## 21. YENİ DOMAIN MODELİ (önerilen entity'ler)

Tümü mevcut kurala uyar (`Id` Guid PK, `CreatedAt`, `UpdatedAt`). Migration'lar `Flower.Domain`'de.

- **ConfluencePage**: `PageId` (unique), `SpaceKey`, `Title`, `Url`, `Version`, `LastModifiedAt`, `AuthorEmail`, `LastEditorEmail`, `Labels`, `IsDeleted`, `ContentHash`, `LastSyncedAt`.
- **DocumentChunk**: `PageId` (FK), `ChunkIndex`, `HeadingPath` (örn. "Fatura > Sorumlular"), `Text`, `TokenCount`, `Embedding` (`vector(1024)` — pgvector), `SpaceKey` (yetki filtresi için denormalize).
- **OrgEntity**: `Type` (Person / Team / Role / System / Process / Concept / Project), `Name`, `NormalizedName`, `Aliases`, `ExternalRef` (Graph userId, Confluence pageId vb.).
- **OrgRelation**: `FromEntityId`, `ToEntityId`, `Type` (owns / responsible_for / knows / uses / member_of / reports_to / approves), `FactId` (kaynağı).
- **KnowledgeFact**: `Statement` (doğal dil), `SubjectEntityId`, `Predicate`, `ObjectEntityId?`, `ObjectText?`, `SourceType` (Confluence / Expert / Graph / Inferred), `SourceRef`, `StatedByEmail?`, `Confidence` (0–1), `Status` (Claimed / Verified / Disputed / Expired / Retracted), `VerifiedAt?`, `ReviewDueAt`, `SupersedesFactId?`, `Embedding`.
- **KnowledgeGap**: `Kind` (MissingOwner / UndefinedConcept / StalePage / Conflict / UnansweredQuestion), `Description`, `RelatedEntityId?`, `Priority`, `State` (Open / Queued / Resolved / Dismissed).
- **QuestionPoolItem**: `Origin` (Reactive / Proactive), `GapId?`, `QuestionText`, `Priority` (0=bekleyen kullanıcı, 1=çelişki/kritik, 2=rutin), `DedupeKey`, `State` (Open / Dispatched / Answered / Exhausted).
- **QuestionWaiter**: `QuestionPoolItemId`, `JobId`, `UserId` — aynı soruyu bekleyen kullanıcılar (çoka-çok).
- **Inquiry**: `QuestionPoolItemId`, `ShortCode` (örn. `S-12`), `ExpertEmail`, `ChatId`, `SentMessageId`, `State` (AwaitingAnswer / Reminded / Answered / Expired / Declined / Redirected), `SentAt`, `DueAt`, `AnsweredAt`, `AnswerText`, `AttemptNo`.
- **ExpertProfile**: `Email`, `OptOut`, `OptOutTopics`, `QuietHoursStart/End`, `WeeklyQuota`, `SentThisWeek`, `AnsweredCount`, `HelpfulScore`.

**Graf için ayrı DB gerekmez:** Pilot ölçeğinde `OrgEntity` + `OrgRelation` tabloları ve PostgreSQL recursive CTE yeterlidir. Apache AGE Windows/portable kurulumda zahmetli; ihtiyaç olursa sonradan graf DB'ye taşınır.

## 22. MEVCUT YAPIDA YAPILMASI GEREKEN DEĞİŞİKLİKLER

### 22.1 Gelen mesaj yönlendirmesi (kritik çakışma)
Bugün her normal mesaj `UserService.AppendPendingAsync` ile `#ASK` beklemek üzere biriktiriliyor. Uzman cevapları da normal mesaj olarak geleceği için:
- `CommandDispatcher.DispatchAsync` içinde, komut ayrıştırmadan **sonra**, normal mesaj biriktirmeden **önce** `IInquiryResponseRouter.TryHandleAsync(email, chatId, text, ct)` çağrılır.
- Gönderenin **açık (AwaitingAnswer/Reminded) Inquiry'si varsa** mesaj Inquiry cevabı olarak işlenir; `PendingMessage`'a eklenmez; `#ASK` gerekmez.
- Birden fazla açık Inquiry varsa: soru mesajları kısa kodla numaralandırılır (`[S-12]`) ve kullanıcı `#Cevap S-12 ...` ile cevaplar; tek açık Inquiry varsa düz metin yeterli.
- Komutlar (`#...`) her zaman önce komut olarak işlenir (mevcut kural korunur).
- Uzman aynı zamanda normal kullanıcıysa ve kendi sorusunu yazıyorsa karışmaması için: açık Inquiry varken gelen mesaja kısa bir onay yanıtı ("S-12 için cevabın olarak kaydettim. Yeni soru ise #ASK ile gönder.") verilir.

### 22.2 Uzmana soru formatı (Adaptive Card kısıtı)
Graph `chatMessage` ile Adaptive Card **gönderilebilir**, ancak Flower bir bot değil (ROPC ile servis hesabı); bu yüzden karttaki `Action.Submit` butonları **geri dönüş üretmez**. Bu sürümde:
- Sorular HTML mesaj olarak gönderilir, seçenekler numaralı yazılır: `1) Evet  2) Hayır  3) Başkası: <isim>  4) Bilmiyorum`.
- Kısa cevaplar (`1`, `2`, `3 Ahmet Yılmaz`) kural tabanlı, serbest metin Fact Processor ile işlenir.
- Her soru mesajının başında sabit etiket: `🤖 Flower – Kurum Asistanı` + neden sorulduğu + "#NoAsk ile bu tür soruları kapatabilirsin".
- Canlıda gerçek bot kaydına geçilirse (Bölüm 15 madde 1) butonlu Adaptive Card'a geçilir.

### 22.3 Yeni komutlar (öneri)
| Komut | Davranış |
|---|---|
| `#Cevap <S-no> <metin>` | Belirli bir Inquiry'ye cevap (birden fazla açık soru varsa). |
| `#Bilmiyorum [S-no]` | Inquiry'yi `Declined` yapar, sıradaki adaya geçilir. |
| `#Yonlendir <S-no> <e-posta>` | Soruyu başka birine yönlendirir (Expert Router'a güçlü sinyal). |
| `#Sorular` | Kişiye açık soruları listeler. |
| `#NoAsk [konu]` | Proaktif soruları kapatır (opt-out); konu verilirse yalnızca o konu. |
| `#AskMe` | Opt-out'u geri alır. |
| `#Kaynak` | Son cevabın kaynaklarını ve güven seviyesini listeler. |
| `#Yanlis <açıklama>` | Son cevaba itiraz → ilgili Fact `Disputed`, Gap oluşur. |

`CommandParserTests`'e her komut için başta/ortada/sonda testleri eklenir; `#ASK` özel kuralı değişmez. `#Help` tablosu güncellenir.

### 22.4 Tenant filtresi
`Tenant.AllowedDomain = ykteknoloji.com.tr`. Uzmanlar farklı domain'de olabilir. `AllowedDomains` listesine dönüştürülmeli; **giden soru** için izinli alıcı listesi gelen mesaj listesinden ayrı tutulmalı (`Inquiry:AllowedRecipientDomains`).

### 22.5 JobProcessorService
- Yeni `JobState.WaitingForExpert`. Bu durumdaki iş **kullanıcı slotunu (MaxConcurrentUsers) ve "kullanıcı başına 1 iş" kuralını meşgul etmez**; kullanıcı yeni `#ASK` gönderebilir.
- Inquiry cevaplandığında `InquiryResolvedHandler` ilgili Job'ı yeniden kuyruğa alır, Synthesizer çalışır, cevap kullanıcının chat'ine gönderilir.
- `MarkInterruptedOnStartupAsync` `WaitingForExpert` işlerine dokunmamalı.
- `#Stop` bekleyen işi iptal eder; uzmana giden soru açık kalır, cevap yalnızca bilgi tabanına yazılır.
- Kullanılmayan `JobState.Merged` kaldırılırken (Bölüm 15 madde 2) yeni state aynı migration'da eklenebilir.

### 22.6 ModelApiClient
- `tools` / `tool_choice` ve `response_format` (json_schema) parametre desteği. **Doğrulanmalı:** Bifrost bu alanları vLLM'e iletiyor mu? (smoke test'e `--schema-test` senaryosu eklenir).
- Agent başına `model` ve `temperature` override.
- `/v1/embeddings` için `IEmbeddingClient`. Bifrost'ta embedding modeli yayında mı doğrulanmalı.
- Rerank için vLLM `/v1/rerank` veya `/score` — yoksa `Rerank.Enabled=false`.
- `ModelInfoProvider` / context limit her agent modeli için ayrı cache'lenmeli.

### 22.7 ContextManager
Agent adımları kendi bağlamını kurar. `ContextManager` (özet + yakın geçmiş) yalnızca kullanıcı konuşma geçmişini sağlar ve Router/Synthesizer'a girdi olur. RAG bağlamı session geçmişine **yazılmaz** (token şişmesi); yalnızca nihai cevap ve kaynak listesi `ConversationMessage` olarak yazılır.

### 22.8 WinForms takip arayüzü
Yeni sekmeler: **Sorular** (QuestionPool + Inquiry durumları, uzman, kalan süre), **Bilgiler** (son eklenen Fact'ler, durum, kaynak; elle Verify/Retract), **Senkron** (Confluence son senkron, sayfa/chunk sayısı, hatalar). Mevcut VirtualMode ListView ve `_applying` deseni kullanılır.

## 23. YENİ BACKGROUND SERVİSLERİ (`Flower.Core/Services/`)

Mevcut desen korunur: `BackgroundService` + DB tabanlı durum + `StopAsync` log + concrete tip `AddSingleton` çift kayıt. **Hangfire zorunlu değil**; mevcut `JobProcessorService` deseni (periyodik DB taraması) yeterli. Zamanlanmış iş sayısı artarsa Hangfire (PostgreSQL storage) değerlendirilebilir.

| Servis | Periyot | İş |
|---|---|---|
| `ConfluenceSyncService` | 1 saatte bir artımlı + gece tam kontrol | `lastModified` ile değişen sayfaları çek, chunk'la, embed et, silinenleri işaretle |
| `KnowledgeExtractionService` | senkron sonrası, gece | Yeni/değişen sayfalardan `KnowledgeFact` + `OrgEntity` çıkar |
| `OrgSyncService` | günlük | Graph'tan kullanıcı, yönetici, grup üyeliği; ayrılan/ekip değiştiren kişiye bağlı Fact'leri yeniden doğrulamaya al |
| `GapDetectionService` | günlük | `KnowledgeGap` üret |
| `InquiryDispatchService` | 1 dk | `QuestionPool` → kota/sessiz saat/opt-out/pilot listesi → Inquiry gönder |
| `InquiryTimeoutService` | 15 dk | `DueAt` geçenlere hatırlatma / Expired / sonraki aday |
| `FactReviewService` | günlük | `ReviewDueAt` geçen Fact'leri yeniden doğrulama kuyruğuna al |

**vLLM kaynak paylaşımı:** `IModelCallScheduler` ile iki öncelik sınıfı (Interactive / Background). Background çağrılar mesai saatinde eşzamanlılık 1 ile veya yalnızca `Proactive:ActiveHours` içinde çalışır; reaktif `#ASK` işleri asla background işleri beklemez.

## 24. YENİ KONFIGÜRASYON (`FlowerOptions` ekleri)

```csharp
FlowerOptions {
  // ... mevcut alanlar ...
  Confluence: {
    BaseUrl: "<confluence-url>", Flavor: "DataCenter" | "Cloud",
    Pat: <secret>, IncludedSpaces: ["..."], DraftSpaceKey: "FLOWER",
    WriteEnabled: false, SyncIntervalMinutes: 60, MaxPagesPerRun: 500
  }
  Embedding: { Model: "<bge-m3 vb.>", Dimensions: 1024, BatchSize: 32 }
  Rerank:    { Enabled: true, Model: "<bge-reranker-v2-m3 vb.>", TopK: 8 }
  Retrieval: { CqlTopK: 20, VectorTopK: 20, ChunkMaxTokens: 800, ChunkOverlapTokens: 100 }
  Agents: {
    Default:       { Model: "<hafif model>", Temperature: 0.2 },
    Judge:         { Model: "<güçlü model>", Temperature: 0.0 },
    Synthesizer:   { Model: "<güçlü model>", Temperature: 0.3 },
    FactExtractor: { Model: "<orta/güçlü model>", Temperature: 0.0 }
  }
  Inquiry: {
    AnswerTimeoutHours: 24, ReminderAfterHours: 8, MaxCandidates: 3,
    MaxFollowUps: 1, WeeklyQuotaPerExpert: 5, ReactiveQuotaOverride: true,
    QuietHours: "18:30-09:00", AllowedRecipientDomains: ["ykteknoloji.com.tr"],
    PilotRecipients: []   // boş değilse yalnızca bu kişilere soru gidebilir
  }
  Proactive: { Enabled: false, ActiveHours: "20:00-07:00", MaxQuestionsPerDay: 20 }
  Knowledge: { VerifiedConfidence: 0.8, ReviewAfterDays: 180, ShowUnverifiedWithLabel: true }
  Telemetry: { Enabled: false, OtlpEndpoint: "http://localhost:3000/api/public/otel" }
}
```
`Proactive.Enabled = false` ve `PilotRecipients` ile kademeli açılış yapılır. Yeni secret'lar (`Confluence:Pat`) user-secrets'a, `SettingsForm` buna göre güncellenir.

## 25. ALTYAPI KARARLARI VE DOĞRULANMASI GEREKENLER

- **pgvector + portable PostgreSQL:** Admin yetkisi yok ve PostgreSQL portable. pgvector Windows'ta derlenmiş DLL gerektirir; portable kuruluma eklenebildiği doğrulanmalı. Alternatifler: (a) önceden derlenmiş pgvector Windows binary'si, (b) Qdrant (tek exe, portable), (c) pilotta embedding'i `real[]` olarak tutup C#'ta cosine (birkaç bin chunk için yeterli).
- **Test uyumu:** SQLite in-memory pgvector desteklemez → `IVectorStore` arayüzü + `InMemoryVectorStore` fake (testlerde), `PgVectorStore` (canlı). Confluence için `InMemoryConfluenceClient`, model için `FakeModelApiClient` (sabit JSON cevaplar ile Judge/Router/akış testleri). `TestHostFactory` bu fake'leri register eder.
- **Graph yetkileri (servis hesabı, ROPC/delegated):** mevcut yetkilere ek olarak `Chat.Create`, `User.Read.All`, `GroupMember.Read.All` (admin consent), opsiyonel `Presence.Read.All`. ROPC + MFA/Conditional Access politikası değişirse akış kırılır (Bölüm 15 madde 12).
- **Confluence:** sürüm (Data Center / Cloud), servis hesabı PAT, okunacak space listesi, yazma yetkisi (yalnızca taslak space önerilir), rate limit, proxy (`gmproxy`) üzerinden mi doğrudan mı erişileceği.
- **Bifrost:** tool calling, json_schema, embeddings ve rerank'ın geçirilip geçirilmediği; agent başına farklı model kullanılabilirliği.
- **Model:** Şu an `Qwen3.8-Flash-Next-FP8`. Router/Planner/Question Writer için yeterli olması muhtemel; Judge/Synthesizer/Fact Processor için daha güçlü bir model (örn. GLM-5.2 veya büyük Qwen 3.8) eval seti ile karşılaştırılarak seçilmeli. GLM-5.2'de vLLM üzerinden `tool_choice: "required"` bilinen bir hataya yol açabiliyor; `auto` kullanılmalı.
- **Gözlemlenebilirlik:** OpenTelemetry (`ActivitySource("Flower.Agents")`), her agent adımı bir span; prompt/cevap span attribute (PII maskeli). Langfuse self-hosted (Docker) kurum onayına bağlı; onay yoksa trace'ler yerel DB/dosyaya.
- **Uyum:** KVKK/İK onayı, çalışan bilgilendirme metni, yalnızca iş/uzmanlık bilgisi tutma (kişisel görüş/performans çıkarımı yok), opt-out, veri saklama süresi, Confluence içeriğinin yerel diskte tutulması için bilgi güvenliği onayı (disk şifreleme).
- **Üretim güvenliği (Bölüm 15 madde 3–4):** `HttpDebug` log'ları artık Confluence içeriği ve uzman cevaplarını da içerecek; `MaskSecrets=true` ve log saklama süresi zorunlu.

## 26. YOL HARİTASI (önerilen fazlar ve kabul kriterleri)

**Faz 2a — Confluence RAG (yalnızca reaktif, kimseye soru sormadan)**
- `ModelApiClient` json_schema + embeddings, `IConfluenceClient`, `ConfluenceSyncService`, chunk + `IVectorStore`, `IRetriever`, Router, Synthesizer, `IAgentOrchestrator`.
- Eval seti (ilk 50 soru) + OTel trace.
- Kabul: Kaynaklı doğru cevap oranı ölçülüyor; kaynak yoksa "bilmiyorum" diyor; kapsam dışı space içeriği cevaba girmiyor.

**Faz 2b — Judge + Inquiry (reaktif, pilot uzmanlarla)**
- Judge, Expert Router (sayfa sahibi + yönetici), `Inquiry`/`QuestionPoolItem`/`ExpertProfile`, `InquiryResponseRouter`, yeni komutlar, `JobState.WaitingForExpert`, `InquiryDispatchService`, `InquiryTimeoutService`.
- `PilotRecipients` ile yalnızca gönüllü kişilere soru.
- Kabul: Uzman cevabı ilk kullanıcıya iletiliyor; hatırlatma/zaman aşımı/sonraki aday çalışıyor; kota aşılmıyor; uzman cevabı `PendingMessage`'a düşmüyor; Flower kendi mesajını işlemiyor.

**Faz 2c — Kurum modeli**
- `OrgSyncService`, `KnowledgeFact`, `OrgEntity/OrgRelation`, Fact Processor, Verifier; uzman cevapları Fact'e dönüşür ve sonraki sorularda kullanılır.
- Kabul: "Uzmana sormak zorunda kaldım" oranı ölçülüyor ve zamanla düşüyor; aynı soru ikinci kez sorulduğunda uzmana gitmiyor.

**Faz 3 — Proaktif mod**
- Gap Detector, Question Writer, `QuestionPool` öncelik + dedupe, `IModelCallScheduler`, `Proactive.Enabled`.
- Kabul: Günlük soru limiti, sessiz saatler, opt-out çalışıyor; proaktif işler reaktif gecikmeyi artırmıyor.

**Faz 4 — Yayın ve canlı**
- Publisher (Confluence taslak sayfası, "Kurum Haritası"), `Flower.Worker` sunucu modu, Graph change notifications, bot kaydı + butonlu Adaptive Card.

---

## KULLANIM TALİMATI (BU PROMPTU ALAN AI İÇİN)

Yukarıdaki bağlamı okudun. Şimdi son kullanıcıdan gelen geliştirme/iyileştirme taleplerini bu yapıya uygun çöz. Kurallar:

1. **Mevcut yapıya uy**: Dosya organizasyonu, katmanlı mimari, DI pattern, kod tarzı, komut sistemi, HTML formatı, timer yapısı, IHost restart mantığı vb. değiştirme. Sadece istenen sorunu/iyileştirmeyi çöz.
2. **Somut öner**: Dosya yolu + metot imzası + kod parçası ver. "İyi pratik" tavsiyesi değil.
3. **Tutarsızlık varsa uyar**: Eğer talep mevcut bir kararla çakışıyorsa (örn. SPEC'te netleşmiş bir kural), belirt ve alternatif öner.
4. **Test ekle**: Yeni davranış için xUnit testi öner (`tests/Flower.Tests/` altında, SQLite in-memory). Model, Confluence ve vektör deposu için fake kullan; testler gerçek LLM veya ağ çağrısı yapmaz.
5. **Build/test komutlarını hatırla**: `dotnet build Flower.slnx` (sıfır warning hedef), `dotnet test tests/Flower.Tests/Flower.Tests.csproj` (89+ test).
6. **SPEC referansı**: Talep SPEC'te bir açık kararsa, netleştirme öner.
7. **Faz sırasına uy**: Faz 2+ özelliklerini Bölüm 26'daki sıraya göre öner; bir faz, sonraki fazın kodunu gerektirmemeli.
8. **Agent kuralları**: Orkestrasyon deterministik C#; LLM çıktıları JSON schema ile doğrulanır; cevaplar yalnızca kurum içi kaynaklardan ve kaynak gösterilerek üretilir; agent başına model config'den gelir.
9. **Çalışanlara giden her mesajda** kota, sessiz saat, opt-out ve pilot listesi kontrolünü atlama.

Şimdi talebimi dinle:

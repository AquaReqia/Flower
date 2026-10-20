# 04 — Bilgi Kaynakları, Hibrit Arama ve RAG Yazma

## 1. Kaynaklar

| Kaynak | Sınıf | Ağırlık (varsayılan) | Not |
|---|---|---|---|
| Doğrulanmış bilgi deposu | `KnowledgeBaseSource` | 1.5 | Onaylanmış soru-cevap notları. Önce bu kaynak gelsin diye ağırlığı yüksek. |
| Confluence (MCP) | `McpKnowledgeSource` | 1.0 | Kullanıcının yazdığı MCP server. Config'de birden çok MCP kaynağı tanımlanabilir. |

Tüm kaynaklar `IKnowledgeSource` arayüzünü uygular ve DI'a `IEnumerable<IKnowledgeSource>` olarak kaydedilir. Yeni bir kaynak (örn. başka bir MCP tool'u, Jira) eklemek için yalnızca config'e bir `McpSources` elemanı eklemek yeterlidir.

## 2. Hibrit arama (`HybridRetriever`) — ✅ başlangıç kodunda var

1. Tüm kaynaklar **paralel** aranır (`PerSourceTopK`).
2. Hata veren kaynak loglanır ve atlanır; diğerleri devam eder. **Hepsi** hata verirse `AllSourcesFailed=true` olur ve orkestratör "bilgi yok" yerine "kaynaklara erişemiyorum" der.
3. Sonuçlar **ağırlıklı Reciprocal Rank Fusion** ile birleştirilir: `skor += ağırlık / (RrfK + sıra + 1)`.
4. Aynı sonuç (aynı `Url`, yoksa `kaynak:id`) tekilleştirilir; en uzun snippet tutulur.
5. `AllowedSpaces` doluysa bu space'ler dışındaki sonuçlar atılır. `SpaceKey` boş olanlar kalır.
6. İlk `FinalTopK` sonuç agent'lara gider.

## 3. Confluence MCP kaynağı — ✅ başlangıç kodunda var

**MCP server'ın kendisi bu modülün işi değildir.** Agents yalnızca şu portu kullanır:

```csharp
public interface IMcpToolCaller
{
    Task<string> CallToolAsync(string? server, string toolName,
        IReadOnlyDictionary<string, object?> arguments, CancellationToken ct);
}
```

### 3.1 Config ile tool eşleme
```json
"McpSources": [
  {
    "Name": "confluence",
    "Server": "confluence",
    "ToolName": "confluence_search",
    "QueryMode": "Keywords",          // Keywords | Question | Cql
    "QueryArgument": "query",
    "LimitArgument": "limit",
    "SpacesArgument": "spaces",        // tool space filtresi alıyorsa; almıyorsa null
    "Spaces": ["FIN", "IT"],
    "StaticArguments": { },
    "Weight": 1.0
  }
]
```
- `Keywords`: Planner'ın anahtar kelimeleri boşlukla birleştirilir (kelime tabanlı arama için).
- `Question`: Soru metni olduğu gibi gider (tool anlamsal arama yapıyorsa).
- `Cql`: `CqlBuilder` ile güvenli CQL üretilir: `type = page AND (text ~ "x" OR title ~ "x" …) AND space in (…)`. Özel karakterler temizlenir.

### 3.2 MCP tool çıktısı için önerilen sözleşme
`McpResultParser` esnektir, ama MCP server'ınız aşağıdaki biçimde dönerse ek kod gerekmez. Bu yapı Expert Router (Faz D) için gereken kişi bilgisini de taşır:
```json
{
  "results": [
    {
      "id": "123456",
      "title": "Fatura Modülü Sorumluları",
      "url": "https://confluence.kurum.local/pages/viewpage.action?pageId=123456",
      "spaceKey": "FIN",
      "excerpt": "Sayfanın ilgili kısmı veya tam içerik (HTML de olabilir, temizlenir)",
      "lastModified": "2026-03-12T10:15:00Z",
      "author":     { "email": null, "username": "u012345", "displayName": "Ayşe Kaya" },
      "lastEditor": { "email": "mehmet.demir@kurum.com.tr", "username": "u054321", "displayName": "Mehmet Demir" },
      "score": 12.4
    }
  ]
}
```
Desteklenen alternatif alan adları: `pageId`, `name`, `link`, `_links.webui`, `space.key`, `snippet/content/body/text`, `body.storage.value`, `updated/history.lastUpdated.when`, `createdBy`, `lastModifiedBy`… Tool JSON dönmezse metnin tamamı tek sonuç sayılır.

**Öneri:** Excerpt olarak sayfanın tamamını değil, eşleşen bölümü (veya ilk ~3000 karakteri) döndürün. Agents tarafında `SnippetSelector` en ilgili pencereyi `MaxSnippetChars` (1500) kadar keser.

### 3.3 Core tarafında `IMcpToolCaller` implementasyonu
Resmi C# MCP SDK'sı (`ModelContextProtocol` NuGet) ile, `Flower.Core/Agents/McpToolCaller.cs`. **SDK sürümüne göre tip adları değişebilir (`McpClientFactory`/`McpClient`, `SseClientTransport`/`HttpClientTransport`). Kurulu sürümün API'sine göre uyarlayın.**

```csharp
public sealed class McpToolCaller(IOptions<McpClientOptionsConfig> options, ILogger<McpToolCaller> log)
    : IMcpToolCaller, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<Task<IMcpClient>>> _clients = new();

    public async Task<string> CallToolAsync(string? server, string toolName,
        IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
    {
        var client = await GetClientAsync(server ?? "default", ct);
        var result = await client.CallToolAsync(toolName, arguments.ToDictionary(k => k.Key, v => v.Value), cancellationToken: ct);

        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        if (result.IsError == true)
            throw new McpToolException(toolName, text);
        return text;
    }

    private Task<IMcpClient> GetClientAsync(string server, CancellationToken ct) =>
        _clients.GetOrAdd(server, s => new Lazy<Task<IMcpClient>>(() => CreateAsync(s))).Value;

    private Task<IMcpClient> CreateAsync(string server)
    {
        var cfg = options.Value.Servers[server];
        IClientTransport transport = cfg.Url is not null
            ? new SseClientTransport(new SseClientTransportOptions { Endpoint = new Uri(cfg.Url) })
            : new StdioClientTransport(new StdioClientTransportOptions { Command = cfg.Command!, Arguments = cfg.Arguments });
        return McpClientFactory.CreateAsync(transport);
    }

    public async ValueTask DisposeAsync() { /* oluşturulan client'ları dispose et */ }
}
```
- Singleton olarak kaydedilir; bağlantı ilk çağrıda açılır ve yeniden kullanılır.
- Bağlantı koparsa: `Lazy` girdisini sil, bir kez yeniden dene.
- Server bilgisi Flower config'inde (`Flower:Mcp:Servers:confluence:Url` veya `Command/Arguments`).

## 4. Doğrulanmış Bilgi Deposu (RAG yazma tarafı) — ⬜ yazılacak

### 4.1 Neden ayrı bir depo
- Confluence'ı değiştirmez. Confluence "kaynak doküman", depo ise "onaylanmış cevap hafızası"dır.
- Kullanıcıların onayladığı cevaplar bir sonraki soruda **önce** buradan bulunur. Bu, LLM maliyetini ve "bulamadım" oranını düşürür.
- Kayıtlar güven seviyesi, durum ve tarih taşır. Eskiyen veya itiraz edilen bilgi ayırt edilir.
- İleride `IKnowledgePublisher` ile Confluence'a (MCP yazma tool'u üzerinden) taslak olarak yayınlanabilir.

### 4.2 `KnowledgeEntry` (tablo: `agents.KnowledgeEntries`)
```csharp
public enum KnowledgeStatus { Confirmed, Verified, Disputed, Superseded, Retracted }

public sealed class KnowledgeEntry
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string CanonicalQuestion { get; set; } = "";
    public string Answer { get; set; } = "";
    public string Keywords { get; set; } = "";            // ';' ile ayrılmış (taşınabilirlik için)
    public string Aliases { get; set; } = "";             // ';' ile ayrılmış
    public string SourceRefs { get; set; } = "";          // JSON: [{title,url,sourceName,spaceKey}]
    public string? SourceSpaces { get; set; }             // ';' ile; yetki filtresi için
    public byte[]? Embedding { get; set; }                // VectorMath.ToBytes; Embedding kapalıysa null
    public string? EmbeddingModel { get; set; }
    public KnowledgeStatus Status { get; set; }
    public double Confidence { get; set; }
    public int ConfirmationCount { get; set; }
    public int DisputeCount { get; set; }
    public string CreatedByEmail { get; set; } = "";
    public string ConfirmedByEmails { get; set; } = "";   // ';' ile, tekrarsız
    public Guid? CreatedFromRunId { get; set; }
    public Guid? SupersededById { get; set; }
    public string? LastDisputeNote { get; set; }
    public DateTime CreatedAt { get; set; }               // UTC
    public DateTime UpdatedAt { get; set; }
    public DateTime LastConfirmedAt { get; set; }
}
```

### 4.3 `IKnowledgeStore`
```csharp
public interface IKnowledgeStore
{
    Task<KnowledgeEntry?> GetAsync(Guid id, CancellationToken ct);
    Task AddAsync(KnowledgeEntry entry, CancellationToken ct);
    Task UpdateAsync(KnowledgeEntry entry, CancellationToken ct);

    /// Yalnızca Confirmed/Verified kayıtlar. Embedding varsa cosine, yoksa anahtar kelime puanı.
    Task<IReadOnlyList<(KnowledgeEntry Entry, double Score)>> SearchAsync(KnowledgeSearch search, CancellationToken ct);
}

public sealed record KnowledgeSearch(
    string Question, IReadOnlyList<string> Keywords, float[]? Embedding,
    int TopK, double MinSimilarity, IReadOnlyList<string>? AllowedSpaces);
```
**Puanlama** (InMemory ve EF aynı yardımcıyı kullanır: `KnowledgeScoring`):
- Anahtar kelime puanı: `KeywordExtractor.Overlap(keywords, Title + CanonicalQuestion + Aliases + Keywords)`
- Embedding varsa: `skor = 0.7 * cosine + 0.3 * kelimePuanı`; `cosine < MinSimilarity` olanlar atılır.
- Embedding yoksa: `skor = kelimePuanı`; `< 0.34` olanlar atılır.
- `AllowedSpaces` doluysa `SourceSpaces` bu kümenin dışına taşan kayıtlar atılır (kısıtlı space'ten türeyen bilginin sızmaması için).
- EF implementasyonu adayları önce SQL'de daraltır (Status filtresi; kelime için `ILIKE` ile ön eleme), sonra puanlamayı C#'ta yapar. Pilot hacminde (≤ 10 bin kayıt) tüm aktif kayıtları çekip C#'ta puanlamak da kabul edilebilir.

### 4.4 Kayıt akışı: `#Dogru [not]`
`KnowledgeCurator.CaptureAsync(userEmail, note, ct)`:

1. Kullanıcının **son AgentRun**'ını al. Koşullar:
   - Kind `Answered` olmalı (Partial dahil).
   - Feedback henüz verilmemiş olmalı.
   - `ConfirmWindowHours` (varsayılan 24) içinde olmalı.

   Koşul sağlanmazsa: "Onaylanacak bir cevap bulamadım."
2. **PII ön kontrolü** (`PiiGuard`): TCKN (11 hane), IBAN (`TR\d{24}`), kart numarası, telefon. Bulunursa kaydetme; kullanıcıya nedenini söyle.
3. **KnowledgeWriter** (Background öncelik) → `KnowledgeNote`.
   - `ShouldStore=false` → Run.Feedback = `Confirmed`, kayıt yok. Kullanıcıya: "Teşekkürler. Bu cevap kişisel/geçici göründüğü için bilgi deposuna eklemedim: {SkipReason}".
4. Embedding açıksa `CanonicalQuestion + "\n" + Answer` için embedding üretilir.
5. **Tekrar kontrolü:** Depoda `DuplicateSimilarity` (0.90) üstü benzer kayıt (embedding yoksa: normalize edilmiş CanonicalQuestion eşitliği veya kelime örtüşmesi ≥ 0.8) var mı?
   - **Var, ve bu cevap o kayda dayanıyorsa** (run'ın atıf yaptığı kaynaklarda o kayıt varsa) → **yeniden onay**:
     - `ConfirmationCount++`, `LastConfirmedAt` güncellenir.
     - Onaylayan kişi öncekilerden farklıysa → `Status=Verified`, `Confidence=VerifiedConfidence`.
   - **Var, ama cevap başka kaynaktan geldiyse** → **yenileme**: yeni kayıt oluşturulur, eski kayıt `Superseded` (`SupersededById`) olur.
   - **Yok** → yeni kayıt: `Status=Confirmed`, `Confidence=ConfirmedConfidence`, `ConfirmationCount=1`.
6. `SourceRefs` ve `SourceSpaces` run'ın atıf yaptığı kaynaklardan doldurulur.
7. `IKnowledgePublisher.PublishAsync(entry)` çağrılır (bu fazda Noop).
8. Run.Feedback = `Confirmed`. Kullanıcıya: "Kaydettim: «{Title}». Bundan sonra bu soruya buradan cevap vereceğim."

### 4.5 İtiraz akışı: `#Yanlis <açıklama>`
`KnowledgeCurator.DisputeAsync(userEmail, note, ct)`:
1. Son AgentRun → `Feedback=Disputed`, `FeedbackNote=note`.
2. Run'ın atıf yaptığı **bilgi deposu kayıtları** için:
   - `DisputeCount++`, `LastDisputeNote=note`, `Status=Disputed`, `Confidence *= 0.5`.
   - Disputed kayıtlar aramaya girmez.
3. Confluence kaynaklıysa depoda değişiklik olmaz. Run, değerlendirme seti için işaretlenir (`AgentRun.Feedback=Disputed` sorgusu).
4. Kullanıcıya: "Not aldım, bu cevabı hatalı olarak işaretledim." Kayıt varsa: "«{Title}» bilgisini askıya aldım."
5. (Faz D) Disputed kayıt için uzmana doğrulama sorusu.

### 4.6 `#Kaynak`
Son run'ın atıf yapılan kaynakları:
```
Son cevabın kaynakları (karar: sufficient, güven 0.82):
[1] Confluence · FIN · 12.03.2026 — Fatura Modülü Sorumluları — https://...
[2] Doğrulanmış bilgi · onaylı (2 onay) · 05.10.2026 — Fatura modülünün sahibi
```

### 4.7 Yaşam döngüsü özeti
```
            #Dogru (ilk)            #Dogru (farklı kişi)
  (yok) ───────────────► Confirmed ─────────────────────► Verified
                           │  ▲                              │
                 #Yanlis   │  │ yeniden onay                 │ #Yanlis
                           ▼  │                              ▼
                         Disputed ◄──────────────────────────┘
  Yeni kaynakla aynı soru onaylanırsa: eski kayıt → Superseded
  Elle geri çekme (ileride UI/komut): → Retracted
  ReviewAfterDays geçmişse: cevapta "(eskimiş olabilir)" etiketi
```

## 5. Güvenlik notları
- Bilgi deposu Confluence yetkilerini bilmez. Bu yüzden `SourceSpaces` ile kaydın hangi space'lerden türediği tutulur ve aramada `AllowedSpaces` filtresi uygulanır. Kullanıcı bazlı Confluence yetkisi MCP server'da uygulanıyorsa (kullanıcı adına arama), bilgi deposu kayıtları da **en kısıtlı kaynak space'i** kadar kısıtlı kabul edilmelidir. Bu konuda karar `docs/08`'de.
- Kişisel veri kaydedilmez (KnowledgeWriter talimatı + `PiiGuard`).
- HTTP debug log'ları artık Confluence içeriği taşıyacak: `HttpDebug:MaskSecrets=true` ve saklama süresi şart.

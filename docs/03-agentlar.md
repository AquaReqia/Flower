# 03 — Agent'lar ve Orkestrasyon

## Genel kurallar

- **Orkestrasyon deterministik C#'tır.** "Hangi agent çalışsın" kararını LLM vermez. LLM yalnızca adımın içindeki işi yapar.
- **Karar veren agent'lar** (Router, Planner, Judge, Knowledge Writer) `StructuredOutputInvoker` ile çağrılır:
  - JSON şeması C# tipinden üretilir (`System.Text.Json.Schema`).
  - Şema hem `response_format` olarak gönderilir hem system prompt'a yazılır.
  - Geçersiz JSON'da bir kez düzeltme istenir, yine olmazsa güvenli varsayılan döner (`IsFallback=true`).
  - **HTTP/ağ hataları varsayılana çevrilmez**, yukarı fırlatılır. Model kapalıyken "bilgi yok" denmemeli.
- **Synthesizer** serbest metin üretir (şemasız).
- **Agent başına profil:** `Flower:Agents:Profiles:<AgentAdı>` altında model, sıcaklık ve max token tanımlanır. Tanımsızsa `DefaultProfile` kullanılır.
- **Prompt'lar** `Prompts/Templates/<ad>.md` dosyalarında, DLL'e gömülü durur. `PromptOverrideDirectory` altında aynı adlı dosya varsa o kullanılır; böylece prompt'u derlemeden değiştirebilirsiniz.
- **Öncelik:** Reaktif akıştaki tüm çağrılar `ModelPriority.Interactive` ile yapılır. Knowledge Writer `#Dogru` sonrası çalıştığı için `Background` kullanır.

## Agent listesi (bu faz)

| Agent (AgentName) | Arayüz | Girdi | Çıktı | Fallback | Önerilen model |
|---|---|---|---|---|---|
| `Router` | `IIntentRouter` | soru + konuşma bağlamı | `IntentResult` | `Knowledge` | hafif |
| `Planner` | `IQueryPlanner` | soru | `QueryPlan` | `KeywordExtractor` çıktısı | hafif |
| `Judge` | `ISufficiencyJudge` | soru + numaralı kaynaklar | `SufficiencyVerdict` | `Insufficient`, 0 güven | **güçlü** |
| `Synthesizer` | `IAnswerSynthesizer` | soru + kaynaklar + karar | metin + kullanılan kaynak numaraları | — (hata fırlatır) | **güçlü** |
| `KnowledgeWriter` | `IKnowledgeWriter` | soru + onaylı cevap + kaynaklar | `KnowledgeNote` | kaydetme (`ShouldStore=false`) | orta/güçlü |

## Girdi/çıktı tipleri (`Agents/AgentModels.cs`)

Varsayılanlı sınıflar kullanılır. `RespectNullableAnnotations=true` olduğu için modelin `null` dönmesi ayrıştırma hatası sayılır ve düzeltme istenir. Enum'lar `snake_case` string'tir.

```csharp
public enum IntentKind { Knowledge, SmallTalk, OutOfScope }

public sealed class IntentResult
{
    public IntentKind Intent { get; init; } = IntentKind.Knowledge;
    public string Reason { get; init; } = "";
}

public sealed class QueryPlan
{
    public List<string> SubQuestions { get; init; } = [];
    public List<string> Keywords { get; init; } = [];          // Türkçe, kök/kısa hâl
    public List<string> EnglishKeywords { get; init; } = [];   // teknik terimlerin İngilizcesi
}

public enum SufficiencyLevel { Sufficient, Partial, Insufficient }

public sealed class SufficiencyVerdict
{
    public SufficiencyLevel Verdict { get; init; } = SufficiencyLevel.Insufficient;
    public double Confidence { get; init; }                    // 0–1
    public List<int> UsedSources { get; init; } = [];          // [1], [2] numaraları
    public List<string> Missing { get; init; } = [];           // eksik bilgi maddeleri
    public string Reason { get; init; } = "";
}

public sealed record AnswerDraft(string Text, IReadOnlyList<int> CitedSources);

public sealed class KnowledgeNote
{
    public bool ShouldStore { get; init; }        // genel ve kalıcı kurum bilgisi mi?
    public string SkipReason { get; init; } = ""; // ShouldStore=false ise neden
    public string Title { get; init; } = "";
    public string CanonicalQuestion { get; init; } = "";
    public string Answer { get; init; } = "";     // bağımsız okunabilir, kaynak numarasız
    public List<string> Keywords { get; init; } = [];
    public List<string> Aliases { get; init; } = []; // sorunun farklı soruluş biçimleri
}
```

## Orkestrasyon akışı (`AgentOrchestrator.HandleAsync`)

```
AgentRequest(RequestId=JobId, UserEmail, UserDisplayName, Question, ConversationContext?, AllowedSpaces?)
│
├─ Enabled=false ──────────────────────────────► PassThrough
├─ Router
│    ├─ SmallTalk ─────────────────────────────► PassThrough  (Flower bugünkü AgentService ile cevaplar)
│    └─ OutOfScope ────────────────────────────► OutOfScope   (kibar ret mesajı)
├─ Planner ─► QueryPlan
├─ HybridRetriever (KnowledgeBase + MCP kaynakları, paralel)
│    └─ AllSourcesFailed ──────────────────────► Failed ("Bilgi kaynaklarına şu an erişemiyorum")
├─ Hit yoksa Judge çağrılmaz ─► Insufficient
├─ Judge ─► SufficiencyVerdict
│    ├─ Sufficient && Confidence ≥ MinAnswerConfidence ─► Synthesizer ─► Answered
│    ├─ Partial ──────────────────► Synthesizer (eksikleri açıkça belirterek) ─► Answered (Partial)
│    └─ Insufficient ─────────────► NotFound ("Kaynaklarda bu konuda bilgi bulamadım" + aranan konu)
│                                     (Faz D'de: NeedsExpert)
└─ Her durumda AgentRun kaydı yazılır (adımlar, süreler, karar, kaynaklar, cevap).
```

```csharp
public enum AgentOutcomeKind { Answered, PassThrough, OutOfScope, NotFound, Failed, NeedsExpert /* Faz D */ }

public sealed record AgentRequest(
    Guid RequestId, string UserEmail, string? UserDisplayName, string Question,
    string? ConversationContext = null, IReadOnlyList<string>? AllowedSpaces = null);

public sealed record AgentOutcome
{
    public required AgentOutcomeKind Kind { get; init; }
    public string? Message { get; init; }                 // kullanıcıya gidecek düz metin (Flower TextToHtml uygular)
    public IReadOnlyList<KnowledgeHit> Sources { get; init; } = [];
    public SufficiencyVerdict? Verdict { get; init; }
    public Guid? RunId { get; init; }
}

public interface IAgentOrchestrator
{
    Task<AgentOutcome> HandleAsync(AgentRequest request, CancellationToken ct);
}
```

**Cevap metni biçimi** (`Answered`):
```
<cevap gövdesi, iddialarda [1], [2] atıfları>

Kaynaklar:
[1] Sayfa başlığı — https://...  (Confluence, 12.03.2026)
[2] Doğrulanmış bilgi: "X sisteminin sahibi" — onaylı, 05.10.2026

<FeedbackHint>
```
- Partial'da gövdenin sonuna "Eksik kalan: …" maddesi eklenir.
- Bilgi deposu kaydı `ReviewAfterDays`'i geçmişse kaynak satırına "(eskimiş olabilir)" eklenir.
- Synthesizer'ın atıf yaptığı numaralar ile kaynak listesi eşleştirilir; atıf yapılmayan kaynak listede gösterilmez.

## Prompt şablonları

Aşağıdaki metinler `Prompts/Templates/*.md` olarak başlangıç koduna eklendi. `{{...}}` yer tutucuları `PromptLibrary.Render` ile doldurulur. JSON şeması invoker tarafından otomatik eklenir, prompt'a yazılmaz.

### `router.md` (system)
```
Sen bir kurum içi asistanın yönlendiricisisin. Kullanıcının mesajını sınıflandır:
- knowledge: Kurum, ekip, sistem, süreç, uygulama, doküman, kural, sorumlu kişi vb. hakkında bilgi isteyen sorular.
- small_talk: Selamlaşma, teşekkür, sohbet, asistanın kendisi hakkında sorular, metin düzeltme/çeviri gibi bilgi gerektirmeyen istekler.
- out_of_scope: Kurumla ilgisi olmayan ve kurum kaynaklarından cevaplanamayacak genel istekler (hava durumu, genel kültür, kişisel tavsiye).
Emin değilsen knowledge seç.
Konuşma bağlamı (önceki mesajlar, olabilir boş):
{{context}}
```

### `planner.md` (system)
```
Kurum içi Confluence ve doğrulanmış bilgi deposunda arama yapılacak. Kullanıcının sorusu için arama planı çıkar:
- subQuestions: Soruyu cevaplamak için gereken en fazla 3 alt soru (soru basitse tek eleman: sorunun kendisi).
- keywords: En fazla 6 Türkçe anahtar kelime. Ekleri at, kök/yalın hâli yaz (örn. "faturalama", "sahip", "ekip"). Özel isim, sistem adı, kısaltma varsa mutlaka ekle.
- englishKeywords: Teknik terimlerin İngilizce karşılıkları (en fazla 4). Yoksa boş liste.
Soru dışında bilgi uydurma.
```

### `judge.md` (system)
```
Sen bir doğrulayıcısın. Görevin: verilen kaynakların kullanıcının sorusunu cevaplamaya yetip yetmediğine karar vermek. Kendi bilgini KULLANMA; yalnızca kaynaklara bak.
- sufficient: Sorunun tamamı kaynaklarla açıkça cevaplanabiliyor.
- partial: Sorunun bir kısmı cevaplanabiliyor; eksik kısımları "missing" listesine yaz.
- insufficient: Kaynaklar soruyu cevaplamıyor veya yalnızca dolaylı ilgili.
confidence: Kararına güvenin (0–1). Kaynaklar birbiriyle çelişiyorsa confidence düşük olsun ve reason'da belirt.
usedSources: Cevap için gerçekten gerekli kaynak numaraları.
"Doğrulanmış bilgi" etiketli kaynaklar kişiler tarafından onaylanmıştır; tarihi eskiyse bunu dikkate al.
```

### `judge-user.md` (user)
```
Soru: {{question}}

Kaynaklar:
{{sources}}
```

### `synthesizer.md` (system)
```
Sen kurum içi bir asistansın. Kullanıcının sorusunu YALNIZCA verilen kaynaklara dayanarak Türkçe cevapla.
Kurallar:
- Her bilgi cümlesinin sonuna dayandığı kaynağın numarasını köşeli parantezle yaz: [1], [2].
- Kaynaklarda olmayan hiçbir bilgiyi ekleme, tahmin yürütme. Genel bilgini kullanma.
- Kaynaklar çelişiyorsa bunu açıkça söyle ve iki tarafı da numarasıyla göster.
- Kısa ve net yaz: önce doğrudan cevap (1–3 cümle), gerekirse ardından madde işaretli detay.
- "Kaynaklar" listesi YAZMA; sistem ekleyecek.
{{partialInstruction}}
Kullanıcı: {{userName}}
Konuşma bağlamı (olabilir boş):
{{context}}
```
`partialInstruction` (Partial ise): `Kaynaklar soruyu kısmen cevaplıyor. Cevaplayabildiğin kısmı yaz, ardından "Eksik kalan:" başlığıyla şu maddeleri belirt: {{missing}}`

### `synthesizer-user.md` (user)
```
Soru: {{question}}

Kaynaklar:
{{sources}}
```

### `knowledge-writer.md` (system)
```
Bir kullanıcının sorusu ve kullanıcının DOĞRU olarak onayladığı cevap verilecek. Bunu kurumun bilgi deposuna kaydedilecek bağımsız bir bilgi notuna dönüştür.
Önce karar ver (shouldStore):
- true: Başka çalışanların da işine yarayacak, kalıcı kurum bilgisi (sistem sahibi, süreç adımı, kural, doküman yeri, sorumlu ekip vb.).
- false: Kişiye özel, geçici (bugünkü toplantı, tek seferlik durum), sohbet, görüş veya hassas kişisel veri içeren cevaplar. skipReason'a nedenini yaz.
shouldStore=true ise:
- title: Kısa başlık (en fazla 10 kelime).
- canonicalQuestion: Sorunun genel, tek cümlelik hâli.
- answer: Bağlam gerektirmeden okunabilen cevap. Kaynak numaralarını [1] KALDIR; gerekiyorsa kaynağın adını metne yaz. Cevapta olmayan bilgi ekleme.
- keywords: En fazla 8 arama anahtar kelimesi (Türkçe, yalın hâl; varsa sistem adları ve kısaltmalar).
- aliases: Aynı sorunun en fazla 3 farklı soruluş biçimi.
```

### `knowledge-writer-user.md` (user)
```
Soru: {{question}}

Onaylanan cevap:
{{answer}}

Kullanıcı notu (olabilir boş): {{note}}

Cevabın dayandığı kaynaklar:
{{sources}}
```

## Kaynak bağlamı biçimi (Judge ve Synthesizer'a giden)

`ContextFormatter.FormatHits(hits, maxCharsPerHit)`:
```
[1] (Confluence · space: FIN · 12.03.2026) Fatura Modülü Sorumluları
https://confluence.../pages/123
<snippet>

[2] (Doğrulanmış bilgi · onaylı · güven 0.70 · 05.10.2026) Fatura modülünün sahibi
<answer>
```

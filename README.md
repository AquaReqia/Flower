# Flower.Agents — Tasarım Paketi

Flower (Teams üzerinden çalışan C# agent kabuğu) projesine **5. modül olarak eklenecek agent katmanının** tasarımı, kodlama talimatı ve başlangıç kodu.

## Klasör içeriği

| Yol | Ne var |
|---|---|
| `CODING_PROMPT.md` | **Kodlama aracına verilecek hazır prompt.** Önce bunu verin, sonra dokümanları referans gösterin. |
| `docs/01-vizyon-ve-kapsam.md` | Sistemin amacı, iki çalışma modu, bu fazın kapsamı ve kapsam dışı kalanlar |
| `docs/02-mimari.md` | 5. modülün yeri, bağımlılık yönü, klasör yapısı, port/adaptör kuralları |
| `docs/03-agentlar.md` | Agent rolleri, girdi/çıktı şemaları, model profilleri, orkestrasyon akışı, prompt şablonları |
| `docs/04-bilgi-kaynaklari-ve-rag.md` | MCP (Confluence) kaynağı, hibrit arama (RRF), doğrulanmış bilgi deposu (RAG yazma), #Dogru / #Yanlis akışı |
| `docs/05-flower-entegrasyonu.md` | Flower tarafında değişecek dosyalar ve kod parçaları (HostSetup, JobProcessorService, CommandDispatcher, AgentsDbContext) |
| `docs/06-konfigurasyon.md` | `Flower:Agents` config bölümü, örnek `appsettings` ve secret'lar |
| `docs/07-yol-haritasi-ve-kabul.md` | Fazlar, iş sırası, kabul kriterleri, test listesi |
| `docs/08-acik-kararlar-ve-kontrol-listesi.md` | Kurumda doğrulanması gerekenler, riskler |
| `context/PROJECT_CONTEXT_PROMPT_v2.md` | Mevcut Flower projesinin tam bağlamı (Bölüm 1–17 mevcut sistem, 18–26 hedef mimari) |
| `starter-code/src/Flower.Agents/` | Başlangıç kodu (derleniyor, 0 warning). Tamamlanmış ve tamamlanmamış parçalar `docs/07`'de listeli. |

## Temel kararlar (özet)

1. **Ayrı modül:** `Flower.Agents` hiçbir Flower projesine referans vermez. Bağlantıyı `Flower.Core` kurar.
2. **Confluence'a dokunulmaz:** Confluence erişimi kullanıcının yazdığı **MCP server** üzerinden yapılır. Agent modülü yalnızca `IMcpToolCaller` portu ile tool çağırır.
3. **RAG yazma:** Cevap kullanıcı tarafından `#Dogru` ile onaylanınca bir Knowledge Writer agent'ı cevabı bağımsız bir bilgi notuna çevirir ve doğrulanmış bilgi deposuna yazar. Benzer kayıt varsa günceller. Sonraki sorularda ilk bakılan kaynak bu depodur.
4. **Deterministik orkestrasyon:** Agent sırasını C# kodu belirler, LLM değil. LLM kararları JSON şemasıyla alınır.
5. **Güvenli kapatma:** `Flower:Agents:Enabled = false` ile Flower bugünkü hâline döner.
6. **Ayrı DbContext:** Agent tabloları `AgentsDbContext` içinde, aynı veritabanında `agents` şemasında.

## Kullanım

1. `starter-code/src/Flower.Agents` klasörünü Flower reposunda `src/Flower.Agents` olarak kopyalayın.
2. Kodlama aracına `CODING_PROMPT.md` içeriğini verin; `docs/` ve `context/` klasörlerini erişilebilir tutun.
3. İş sırası `docs/07-yol-haritasi-ve-kabul.md` dosyasındadır.

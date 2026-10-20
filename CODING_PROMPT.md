# Kodlama Talimatı — Flower.Agents (Faz A + B + RAG yazma)

## Rol
Sen kıdemli bir .NET/C# geliştiricisi ve LLM agent sistemleri mühendisisin. Flower projesine **5. modül** olarak `Flower.Agents` agent katmanını ekleyeceksin.

## Önce oku (sırayla)
1. `context/PROJECT_CONTEXT_PROMPT_v2.md`: Mevcut Flower projesi. Bölüm 1–17 çalışan sistem, 16 kod tarzı.
2. `docs/02-mimari.md`: Modülün yeri, bağımlılık yönü, klasör yapısı.
3. `docs/03-agentlar.md`: Agent'lar, tipler, orkestrasyon akışı, prompt'lar.
4. `docs/04-bilgi-kaynaklari-ve-rag.md`: MCP kaynağı, hibrit arama, bilgi deposu, #Dogru / #Yanlis / #Kaynak.
5. `docs/05-flower-entegrasyonu.md`: Flower'da değişecek yerler.
6. `docs/06-konfigurasyon.md` ve `docs/07-yol-haritasi-ve-kabul.md`: Ayarlar, iş sırası, testler, kabul kriterleri.
7. `docs/08-acik-kararlar-ve-kontrol-listesi.md`: Karar verilmemiş noktalarda varsayılanlar.

## Başlangıç noktası
`src/Flower.Agents/` altında başlangıç kodu var (docs/07 §1'deki ✅ dosyalar, 0 warning ile derleniyor). **Bu dosyaları yeniden yazma, üzerine inşa et.** API'lerini değiştirmen gerekirse nedenini belirt.

## Kesin kurallar
1. **Bağımlılık:** `Flower.Agents` hiçbir Flower projesine referans vermez. Flower'la bağlantı yalnızca `Flower.Core` üzerinden ve port arayüzleriyle kurulur.
2. **Confluence MCP server'a dokunma.** Onu kullanıcı yazıyor. Sadece `IMcpToolCaller` portunu kullan; Core'daki implementasyon resmi `ModelContextProtocol` SDK'sı ile yazılır.
3. **Deterministik orkestrasyon:** Agent sırasını C# belirler. LLM kararları `StructuredOutputInvoker` ile JSON şemalı alınır. Ağ hataları fallback'e çevrilmez.
4. **Kaynaksız cevap yok:** Synthesizer yalnızca verilen kaynaklardan cevap verir, `[n]` atıfı yapar. Kaynak yoksa "bulamadım" der.
5. **Mevcut davranış korunur:** `Flower:Agents:Enabled=false` iken Flower birebir bugünkü gibi çalışır, mevcut 89 test değişmeden geçer. Sohbet soruları (`PassThrough`) bugünkü `AgentService` yolundan cevaplanır.
6. **Kalıcılık:** Ayrı `AgentsDbContext`, aynı veritabanı, `agents` şeması. `DateTime` UTC. Embedding `byte[]`. pgvector kullanma.
7. **Polling döngüsü bloklanmaz:** LLM çağrısı içeren her iş (`#Dogru` dahil) `JobProcessorService` üzerinden yürür.
8. **Kod tarzı:** Flower'ınki (context §16): file-scoped namespace, nullable, `record` tercihi, Türkçe kısa XML doc, `Async` soneki, `ct` parametresi, structured logging, `TIMING ...` ön eki, `catch (OperationCanceledException) when (ct.IsCancellationRequested)`.
9. **Testler:** Her yeni davranış için xUnit testi (`tests/Flower.Tests/Agents/`). SQLite in-memory veya InMemory store kullan. Gerçek LLM/MCP/ağ çağrısı yok. `FakeChatModelClient` `AgentName`'e göre cevap verir.
10. **Build:** `dotnet build Flower.slnx` → 0 warning, 0 error. `dotnet test` → tümü yeşil.
11. **Flower'ın gerçek imzaları esastır:** Dokümanlardaki Flower kod parçaları bağlamdan çıkarıldı. Gerçek dosyada isim veya imza farklıysa gerçek olanı kullan ve farkı bildir.
12. **Tutarsızlık:** Bir talimat mevcut bir Flower kararıyla çelişirse dur, belirt ve alternatif öner.

## İş sırası
docs/07 §2'deki 10 adımı sırayla uygula. Her adım sonunda:
- Build ve testleri çalıştır.
- Kısa özet ver: eklenen/değişen dosyalar, yeni testler, varsa açık soru.
- Commit mesajı Flower kuralıyla: `feat: agent orkestratoru eklendi` (Türkçe, diakritiksiz) + boş satır + detay.

## Bitti tanımı
docs/07 §4'teki 8 kabul kriterinin tamamı sağlanıyor ve docs/07 §3'teki testler yazılmış ve geçiyor.

Şimdi 1. adımla başla: `Agents/AgentModels.cs` ve `ContextFormatter`.

# 01 — Vizyon ve Kapsam

## Amaç

Flower, Teams üzerinden 1:1 mesajları alan ve Bifrost/vLLM üzerinden cevap üreten bir arka plan uygulamasıdır (bkz. `context/PROJECT_CONTEXT_PROMPT_v2.md`). Bir sonraki adım, Flower'ı **kurumun yapısını öğrenen bir ajana** dönüştürmektir:

- Cevaplar **kurumun kendi içinden** gelir: Confluence ve çalışanlar. Model genel bilgisinden cevap uydurmaz.
- Doğrulanan her cevap kalıcı bilgiye dönüşür. Sistem kullanıldıkça öğrenir.

## İki çalışma modu (uzun vadeli)

| Mod | Tetikleyici | Ne yapar |
|---|---|---|
| **Reaktif** | Kullanıcı `#ASK` ile soru sorar | Arar → yeterliyse kaynaklı cevap verir → yetersizse (ileride) uzmana sorar |
| **Proaktif** | Zamanlayıcı / olay | Eksik, çelişkili, eskimiş bilgiyi bulur, soru üretir, ilgili kişiye sorar |

İki mod ortak çekirdeği paylaşır: aynı agent'lar, aynı bilgi deposu, aynı soru havuzu. Aynı anda çalışabilirler; reaktif işler önceliklidir.

## Bu fazın kapsamı (Faz A + B + RAG yazma)

Kimseye proaktif mesaj atmayan, bu yüzden kurum onayı gerektirmeyen ilk sürüm:

1. **Agent altyapısı:** Model istemcisi, JSON şemalı çağrı, prompt yönetimi, agent başına model profili, öncelik zamanlayıcısı, telemetri.
2. **Agent'lar:** Router, Planner, Judge, Synthesizer, Knowledge Writer.
3. **Bilgi kaynakları:** Confluence MCP server (kullanıcı yazıyor) + doğrulanmış bilgi deposu. Hibrit arama.
4. **Kaynaklı cevap:** Her iddiaya kaynak. Kaynak yoksa "bilmiyorum".
5. **Geri bildirim ve RAG yazma:**
   - `#Dogru`: Son cevabı onayla, bilgi deposuna yaz.
   - `#Yanlis <açıklama>`: Son cevaba itiraz et, ilgili kaydı itirazlı işaretle.
   - `#Kaynak`: Son cevabın kaynaklarını ve güven seviyesini göster.
6. **Çalışma kaydı (AgentRun):** Her soru için agent adımları, kararlar, kaynaklar, süreler, token'lar.
7. **Flower entegrasyonu:** `JobProcessorService`, `CommandDispatcher`, `HostSetup`.

## Kapsam dışı (sonraki fazlar)

- Uzmana Teams'ten soru sorma (Inquiry), hatırlatma, kota, sessiz saat — **Faz D**
- Kurum modeli (varlık/ilişki), eksik tespiti, proaktif soru — **Faz E**
- Confluence'a geri yazma, WinForms agent sekmeleri — **Faz F**
- Confluence MCP server'ın kendisi — **kullanıcı yazıyor, bu modülün işi değil**

Tasarım bu fazları engellemeyecek şekilde yapılır. Örneğin `AgentOutcomeKind.NeedsExpert` değeri şimdiden tanımlıdır, ancak bu fazda üretilmez.

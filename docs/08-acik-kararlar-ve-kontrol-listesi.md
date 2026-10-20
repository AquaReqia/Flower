# 08 — Açık Kararlar ve Kontrol Listesi

## Kodlamadan önce netleşmesi gerekenler
| # | Konu | Neden önemli | Varsayılan (karar verilmezse) |
|---|---|---|---|
| K1 | **Confluence MCP tool adı, argümanları ve çıktı biçimi** | `McpSources` config'i ve parser buna göre ayarlanır | `confluence_search(query, limit)`, çıktı docs/04 §3.2 sözleşmesi |
| K2 | **MCP bağlantı tipi** (stdio / SSE / HTTP) | `McpToolCaller` transport'u | SSE/HTTP URL |
| K3 | **MCP arama kullanıcı adına mı, servis hesabıyla mı?** | Yetki sızıntısı: servis hesabıyla aranıyorsa kullanıcı göremeyeceği sayfanın içeriğini görebilir | Servis hesabı + `Spaces` ile sınırlı space listesi |
| K4 | **Bilgi deposu kayıtlarının görünürlüğü** | Kısıtlı space'ten türeyen onaylı bilgi herkese açılmamalı | `SourceSpaces` kaydedilir; kısıtlı space listesi config'de, bu space'lerden türeyen kayıtlar yalnızca o space'e erişimi olanlara (pilotta: hiç kaydedilmez) |
| K5 | **Judge/Synthesizer modeli** | Kalitenin büyük kısmı buradan gelir | Qwen3.8-Flash-Next; eval sonrası güçlü modele geçiş |
| K6 | **Bifrost `response_format` desteği** | Desteklemiyorsa 400 dönebilir | `UseJsonSchemaResponseFormat=true`; smoke testte 400 alınırsa false |
| K7 | **Embedding modeli Bifrost'ta var mı?** | Bilgi deposunda anlamsal arama ve tekrar kontrolü | `Embedding.Enabled=false` (anahtar kelime ile çalışır) |
| K8 | **`#Dogru` kimin onayı sayılır?** | Tek kişinin onayı "doğru" değil "beyan"dır | Soranın onayı → Confirmed (0.7); farklı ikinci kişi → Verified (0.9) |
| K9 | **`#Dogru` işleme şekli** | Dispatcher'ı bloklamamak | Feedback job'u olarak kuyruğa (docs/05 §3 Seçenek A) |

## Kurumda kontrol edilecekler (bu faz için)
- [ ] Bilgi deposunda (yerel PostgreSQL) Confluence türevi içerik tutulması için bilgi güvenliği onayı (disk şifreleme, erişim).
- [ ] HTTP debug log'larının üretimde maskeli ve süreli tutulması.
- [ ] Model çıktılarının (AgentRun) saklama süresi.
- [ ] Kullanılan modellerin lisanslarının kurumsal kullanıma uygunluğu.
- [ ] `ModelContextProtocol` NuGet paketinin kurum NuGet feed'inde/proxy'sinde erişilebilir olması.

## Bilinen riskler
| Risk | Önlem |
|---|---|
| Model yanlış "sufficient" der, kaynaksız cevap üretir | Synthesizer'da kaynak zorunluluğu; atıfsız cevapta uyarı; `MinAnswerConfidence`; eval seti |
| Yanlış cevap `#Dogru` ile depoya girer | Confirmed (0.7) ≠ Verified; `#Yanlis` ile askıya alma; Faz D'de uzman doğrulaması |
| Depo zamanla eskir | `ReviewAfterDays` etiketi; Faz E'de yeniden doğrulama |
| Polling döngüsünün LLM çağrısıyla bloklanması | Tüm LLM işleri JobProcessor'da; dispatcher yalnızca kuyruğa alır |
| GPU paylaşımı | `ModelCallScheduler` (Background=1) |
| Qwen "System message must be at the beginning" | Agent'lar tek system mesajı + user mesajı kullanır; düzeltme turunda system tekrar eklenmez (✅ invoker böyle) |

## Değerlendirme seti (paralel olarak hazırlanmalı)
- 50–100 gerçek Türkçe kurum sorusu; her biri için beklenen cevap ve doğru Confluence sayfası.
- `#Yanlis` alan run'lar otomatik olarak aday sete eklenir.
- Ölçülecekler: Judge doğruluğu (sufficient/insufficient), kaynak isabeti (doğru sayfa ilk 3'te mi), cevap doğruluğu, "bulamadım" oranı, ortalama süre.

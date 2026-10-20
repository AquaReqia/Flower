# 06 — Konfigürasyon

Tüm ayarlar `Flower:Agents` bölümünde (`AgentOptions`, ✅ başlangıç kodunda). Flower'ın mevcut flat notation kuralına uyun (duplicate key hatası için).

## `appsettings.json` örneği
```json
{
  "Flower": {
    "Agents": {
      "Enabled": true,
      "PromptOverrideDirectory": "prompts",

      "Model": {
        "BaseUrl": "https://<bifrost>/v1",
        "TimeoutSeconds": 120,
        "StripThinkBlocks": true,
        "UseJsonSchemaResponseFormat": true
      },

      "DefaultProfile": { "Model": "vllm-qwen38-flash-next/Qwen/Qwen3.8-Flash-Next-FP8", "Temperature": 0.2 },
      "Profiles": {
        "Router":          { "Temperature": 0.0, "MaxTokens": 200 },
        "Planner":         { "Temperature": 0.0, "MaxTokens": 400 },
        "Judge":           { "Model": "<güçlü model>", "Temperature": 0.0, "MaxTokens": 600 },
        "Synthesizer":     { "Model": "<güçlü model>", "Temperature": 0.3, "MaxTokens": 1500 },
        "KnowledgeWriter": { "Temperature": 0.0, "MaxTokens": 1200 }
      },

      "Retrieval": {
        "PerSourceTopK": 10,
        "FinalTopK": 8,
        "RrfK": 60,
        "MaxSnippetChars": 1500,
        "MinAnswerConfidence": 0.6
      },

      "McpSources": [
        {
          "Name": "confluence",
          "Server": "confluence",
          "ToolName": "confluence_search",
          "QueryMode": "Keywords",
          "QueryArgument": "query",
          "LimitArgument": "limit",
          "SpacesArgument": null,
          "Spaces": [],
          "Weight": 1.0
        }
      ],

      "Embedding": { "Enabled": false, "Model": "<bge-m3 vb.>", "BatchSize": 32 },

      "KnowledgeBase": {
        "Enabled": true,
        "Weight": 1.5,
        "TopK": 5,
        "MinSimilarity": 0.55,
        "DuplicateSimilarity": 0.90,
        "ConfirmedConfidence": 0.7,
        "VerifiedConfidence": 0.9,
        "ReviewAfterDays": 180,
        "ConfirmWindowHours": 24,
        "FeedbackHint": "Cevap doğruysa #Dogru, hatalıysa #Yanlis <açıklama> yaz. Kaynaklar için #Kaynak."
      },

      "Scheduler": { "InteractiveConcurrency": 8, "BackgroundConcurrency": 1 }
    },

    "Mcp": {
      "Servers": {
        "confluence": { "Url": "http://localhost:5100/sse" }
      }
    }
  }
}
```
`Mcp:Servers` Flower.Core'un ayarıdır (MCP bağlantısı Core'da kurulur); `Agents` bölümü yalnızca server adını (`McpSources[].Server`) bilir.

## Secret'lar (user-secrets)
| Anahtar | Açıklama |
|---|---|
| `Flower:Agents:Model:ApiKey` | Bifrost `Authorization: Bearer` (mevcut `Flower:Model:ApiKey` ile aynı olabilir) |
| `Flower:Agents:Model:ExtraHeaders:x-bf-vk` | Bifrost virtual key |
| MCP server kimlik bilgileri | MCP server'ınızın kendi ayarı (Agents bilmez) |

**Tekrar tanımı önleme:** Model `BaseUrl`/`ApiKey` zaten `Flower:Model` altında var. `AddFlowerAgents` içinde `Flower:Agents:Model:BaseUrl` boşsa `Flower:Model:BaseUrl`'e, `ApiKey` boşsa `Flower:Model:ApiKey`'e düşen bir `PostConfigure` yazılması önerilir.

## Ayar açıklamaları (kritik olanlar)
| Ayar | Etkisi |
|---|---|
| `Enabled=false` | Orkestratör her soruya `PassThrough` döner, Flower bugünkü gibi çalışır. |
| `UseJsonSchemaResponseFormat=false` | Bifrost `response_format`'ı reddederse kapatın; şema prompt'ta olduğu için yapılandırılmış çıktı devam eder. |
| `MinAnswerConfidence` | Judge "sufficient" dese de güven bunun altındaysa cevap Partial muamelesi görür. |
| `KnowledgeBase.Weight` | > 1 ise onaylı bilgiler Confluence sonuçlarının önüne geçer. |
| `Embedding.Enabled=false` | Bilgi deposu anahtar kelimeyle aranır; tekrar kontrolü metin benzerliğiyle yapılır. |
| `Scheduler.BackgroundConcurrency` | KnowledgeWriter gibi arka plan çağrılarının GPU'yu ne kadar paylaşacağı. |

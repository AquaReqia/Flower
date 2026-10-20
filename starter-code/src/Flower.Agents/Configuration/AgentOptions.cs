namespace Flower.Agents.Configuration;

/// <summary>
/// Agent katmanı ayarları. Varsayılan bölüm: <c>Flower:Agents</c>.
/// Secret'lar (ApiKey, Confluence PAT) user-secrets'ta tutulmalı.
/// </summary>
public sealed class AgentOptions
{
    public const string SectionName = "Flower:Agents";

    /// <summary>false ise Flower bugünkü (agent'sız) akışla çalışır; orkestratör her soruyu PassThrough döndürür.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Model endpoint'i (Bifrost / vLLM, OpenAI uyumlu <c>/v1</c>).</summary>
    public ModelEndpointOptions Model { get; set; } = new();

    /// <summary>Profili tanımlanmamış agent'ların kullandığı varsayılan profil.</summary>
    public AgentProfileOptions DefaultProfile { get; set; } = new();

    /// <summary>Agent adına göre profil (Router, Planner, Judge, Synthesizer, QuestionWriter).</summary>
    public Dictionary<string, AgentProfileOptions> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Doluysa prompt şablonları önce bu klasörden (<c>{ad}.md</c>) okunur; yoksa DLL içindeki kullanılır.</summary>
    public string? PromptOverrideDirectory { get; set; }

    public RetrievalOptions Retrieval { get; set; } = new();
    /// <summary>MCP server üzerinden aranan bilgi kaynakları (örn. Confluence MCP).</summary>
    public List<McpSourceOptions> McpSources { get; set; } = [];

    public EmbeddingOptions Embedding { get; set; } = new();
    public KnowledgeBaseOptions KnowledgeBase { get; set; } = new();
    public SchedulerOptions Scheduler { get; set; } = new();

    /// <summary>Agent için çözümlenmiş profili döndürür (agent profili → varsayılan profil).</summary>
    public ResolvedAgentProfile GetProfile(string agentName)
    {
        Profiles.TryGetValue(agentName, out var p);
        var model = Coalesce(p?.Model, DefaultProfile.Model)
            ?? throw new InvalidOperationException(
                $"'{agentName}' agent'ı için model tanımlı değil. {SectionName}:DefaultProfile:Model veya {SectionName}:Profiles:{agentName}:Model ayarlayın.");

        return new ResolvedAgentProfile(
            model,
            p?.Temperature ?? DefaultProfile.Temperature,
            p?.MaxTokens ?? DefaultProfile.MaxTokens);
    }

    private static string? Coalesce(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a : !string.IsNullOrWhiteSpace(b) ? b : null;
}

public sealed record ResolvedAgentProfile(string Model, double? Temperature, int? MaxTokens);

public sealed class AgentProfileOptions
{
    public string? Model { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
}

public sealed class ModelEndpointOptions
{
    /// <summary>Örn. <c>https://bifrost.../v1</c>.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary><c>Authorization: Bearer</c> değeri. Secret.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Ek header'lar (örn. Bifrost virtual key: <c>x-bf-vk</c>). Değerler secret olabilir.</summary>
    public Dictionary<string, string> ExtraHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Reasoning modellerinin cevap içine yazdığı <c>&lt;think&gt;</c> bloklarını temizle.</summary>
    public bool StripThinkBlocks { get; set; } = true;

    /// <summary>
    /// true ise <c>response_format: json_schema</c> gönderilir. Proxy desteklemiyorsa false yapın;
    /// şema yine de system prompt'a yazıldığı için model JSON üretmeye devam eder.
    /// </summary>
    public bool UseJsonSchemaResponseFormat { get; set; } = true;
}

public sealed class RetrievalOptions
{
    /// <summary>Her kaynaktan alınacak sonuç sayısı.</summary>
    public int PerSourceTopK { get; set; } = 10;

    /// <summary>Birleştirme (RRF) sonrası agent'lara verilecek sonuç sayısı.</summary>
    public int FinalTopK { get; set; } = 8;

    /// <summary>Reciprocal Rank Fusion sabiti.</summary>
    public int RrfK { get; set; } = 60;

    /// <summary>Bir sonucun bağlama girecek en fazla karakter sayısı.</summary>
    public int MaxSnippetChars { get; set; } = 1500;

    /// <summary>Judge "sufficient" dese bile bu güvenin altındaysa uzmana sorulur.</summary>
    public double MinAnswerConfidence { get; set; } = 0.6;
}

public enum McpQueryMode
{
    /// <summary>Planner'ın anahtar kelimeleri boşlukla birleştirilip gönderilir (kelime tabanlı arama için).</summary>
    Keywords,

    /// <summary>Soru metni olduğu gibi gönderilir (anlamsal arama yapan tool'lar için).</summary>
    Question,

    /// <summary>Anahtar kelimelerden CQL üretilip gönderilir (Confluence CQL kabul eden tool'lar için).</summary>
    Cql,
}

/// <summary>
/// Bir MCP tool'unu bilgi kaynağı olarak tanımlar. MCP server'ın kendisi Flower dışındadır;
/// burada yalnızca hangi tool'un hangi argümanlarla çağrılacağı ve sonucun nasıl okunacağı tanımlanır.
/// </summary>
public sealed class McpSourceOptions
{
    /// <summary>Kaynak adı (telemetri, AgentRun, kaynak listesi). Örn. "confluence".</summary>
    public string Name { get; set; } = "confluence";

    public bool Enabled { get; set; } = true;

    /// <summary><see cref="Abstractions.IMcpToolCaller"/> birden çok server yönetiyorsa server anahtarı.</summary>
    public string? Server { get; set; }

    /// <summary>Arama tool'unun adı. Örn. "confluence_search".</summary>
    public string ToolName { get; set; } = "confluence_search";

    public McpQueryMode QueryMode { get; set; } = McpQueryMode.Keywords;

    /// <summary>Sorgunun yazılacağı argüman adı.</summary>
    public string QueryArgument { get; set; } = "query";

    /// <summary>Sonuç sayısı argümanı. Boşsa gönderilmez.</summary>
    public string? LimitArgument { get; set; } = "limit";

    /// <summary>Space filtresi argümanı (string dizisi). Boşsa gönderilmez.</summary>
    public string? SpacesArgument { get; set; }

    /// <summary>Her çağrıya eklenecek sabit argümanlar.</summary>
    public Dictionary<string, string> StaticArguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Aranacak space'ler (QueryMode=Cql ise CQL'e, SpacesArgument doluysa argümana yazılır).</summary>
    public List<string> Spaces { get; set; } = [];

    /// <summary>RRF birleştirmesinde bu kaynağın ağırlığı.</summary>
    public double Weight { get; set; } = 1.0;
}

public sealed class EmbeddingOptions
{
    /// <summary>true ise vektör kaynağı devreye girer (embedding modeli ve vektör deposu gerekir).</summary>
    public bool Enabled { get; set; }

    /// <summary>Boşsa <see cref="ModelEndpointOptions.BaseUrl"/> kullanılır.</summary>
    public string? BaseUrl { get; set; }

    public string? Model { get; set; }
    public int BatchSize { get; set; } = 32;
}

public sealed class KnowledgeBaseOptions
{
    /// <summary>Doğrulanmış bilgi deposu arama kaynağı olarak kullanılsın.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>RRF birleştirmesinde depo ağırlığı (Confluence'tan önce gelmesi için &gt; 1).</summary>
    public double Weight { get; set; } = 1.5;

    /// <summary>Aramada dönecek en fazla kayıt.</summary>
    public int TopK { get; set; } = 5;

    /// <summary>Embedding varsa bu benzerliğin altındaki kayıtlar sonuca girmez.</summary>
    public double MinSimilarity { get; set; } = 0.55;

    /// <summary>Yeni kayıt bu benzerliğin üstünde bir kayda denk gelirse yeni kayıt açılmaz, mevcut güncellenir.</summary>
    public double DuplicateSimilarity { get; set; } = 0.90;

    /// <summary>Soran kişinin #Dogru onayıyla oluşan kaydın güveni.</summary>
    public double ConfirmedConfidence { get; set; } = 0.7;

    /// <summary>İkinci bir kişinin (veya uzman/sahip) onayıyla oluşan güven.</summary>
    public double VerifiedConfidence { get; set; } = 0.9;

    /// <summary>Kayıt bu kadar gün sonra yeniden doğrulama gerektirir (cevapta "eskimiş olabilir" uyarısı).</summary>
    public int ReviewAfterDays { get; set; } = 180;

    /// <summary>#Dogru / #Yanlis son cevaba en fazla bu kadar saat sonra uygulanabilir.</summary>
    public int ConfirmWindowHours { get; set; } = 24;

    /// <summary>Cevap altına eklenen geri bildirim ipucu. Boşsa eklenmez.</summary>
    public string? FeedbackHint { get; set; } = "Cevap doğruysa #Dogru, hatalıysa #Yanlis <açıklama> yaz. Kaynaklar için #Kaynak.";
}

public sealed class SchedulerOptions
{
    /// <summary>Aynı anda en fazla kaç etkileşimli (kullanıcı bekleyen) model çağrısı.</summary>
    public int InteractiveConcurrency { get; set; } = 8;

    /// <summary>Aynı anda en fazla kaç arka plan model çağrısı.</summary>
    public int BackgroundConcurrency { get; set; } = 1;
}

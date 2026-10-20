using System.Globalization;
using System.Text;
using System.Text.Json;
using Flower.Agents.KnowledgeBase;
using Flower.Agents.Orchestration;
using Flower.Agents.Runs;
using Flower.Agents.Structured;

namespace Flower.Agents.Feedback;

/// <summary>#Dogru, #Yanlis ve #Kaynak komutları. Flower JobProcessor üzerinden çağırır.</summary>
public interface IAgentFeedbackService
{
    Task<FeedbackResult> ConfirmAsync(string userEmail, string? note, CancellationToken ct);
    Task<FeedbackResult> DisputeAsync(string userEmail, string? note, CancellationToken ct);
    Task<FeedbackResult> DescribeSourcesAsync(string userEmail, CancellationToken ct);
}

/// <inheritdoc />
public sealed class AgentFeedbackService(KnowledgeCurator curator, IAgentRunStore runs, TimeProvider time) : IAgentFeedbackService
{
    public const string NoRun = "Kaynaklarını gösterebileceğim bir cevap bulamadım.";

    public Task<FeedbackResult> ConfirmAsync(string userEmail, string? note, CancellationToken ct) =>
        curator.CaptureAsync(userEmail, note, ct);

    public Task<FeedbackResult> DisputeAsync(string userEmail, string? note, CancellationToken ct) =>
        curator.DisputeAsync(userEmail, note, ct);

    public async Task<FeedbackResult> DescribeSourcesAsync(string userEmail, CancellationToken ct)
    {
        var run = await KnowledgeCurator.FindLatestRunAsync(runs, KnowledgeCurator.Normalize(userEmail), 0, time, ct).ConfigureAwait(false);
        if (run is null)
        {
            return new FeedbackResult(false, NoRun);
        }

        var cited = run.GetCitedSources();
        if (cited.Count == 0)
        {
            return new FeedbackResult(true, $"Son cevapta kullanılan kaynak yok (sonuç: {Describe(run.Kind)}).");
        }

        var sb = new StringBuilder("Son cevabın kaynakları");
        if (run.Verdict is { } v)
        {
            var verdict = JsonSerializer.Serialize(v, StructuredOutputInvoker.Json).Trim('"');
            sb.Append(" (karar: ").Append(verdict);
            if (run.Confidence is { } c)
            {
                sb.Append(", güven ").Append(c.ToString("0.00", CultureInfo.InvariantCulture));
            }

            sb.Append(')');
        }

        sb.Append(':');
        foreach (var c in cited)
        {
            sb.Append('\n').Append(AnswerFormatter.DescribeLine(c));
        }

        return new FeedbackResult(true, sb.ToString());
    }

    private static string Describe(AgentOutcomeKind kind) => kind switch
    {
        AgentOutcomeKind.NotFound => "bilgi bulunamadı",
        AgentOutcomeKind.OutOfScope => "kapsam dışı",
        AgentOutcomeKind.Failed => "hata",
        _ => kind.ToString(),
    };
}

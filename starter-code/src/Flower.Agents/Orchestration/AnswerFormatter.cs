using System.Text;
using Flower.Agents.Agents;
using Flower.Agents.Knowledge;
using Flower.Agents.Runs;

namespace Flower.Agents.Orchestration;

/// <summary>Kullanıcıya giden cevap metnini ve kaynak satırlarını biçimlendirir.</summary>
public static class AnswerFormatter
{
    public const string NoCitationWarning = "(Bu cevapta kaynak atfı bulunamadı; lütfen dikkatle değerlendirin.)";

    /// <summary>Cevap gövdesi; Partial ise ve model eklemediyse "Eksik kalan:" satırı eklenir.</summary>
    public static string BuildBody(string draftText, SufficiencyVerdict verdict)
    {
        var body = draftText.Trim();
        if (verdict.Verdict == SufficiencyLevel.Partial
            && verdict.Missing.Count > 0
            && !body.Contains("Eksik kalan", StringComparison.OrdinalIgnoreCase))
        {
            body += "\n\nEksik kalan: " + string.Join("; ", verdict.Missing);
        }

        return body;
    }

    /// <summary>Gövde + "Kaynaklar:" listesi (yalnızca atıf yapılanlar) + geri bildirim ipucu.</summary>
    public static string BuildMessage(string body, IReadOnlyList<CitedSource> cited, string? feedbackHint, DateTimeOffset now, int reviewAfterDays)
    {
        var sb = new StringBuilder(body);
        if (cited.Count == 0)
        {
            sb.Append("\n\n").Append(NoCitationWarning);
        }
        else
        {
            sb.Append("\n\nKaynaklar:");
            foreach (var c in cited)
            {
                sb.Append('\n').Append(SourceLine(c, now, reviewAfterDays));
            }
        }

        if (!string.IsNullOrWhiteSpace(feedbackHint))
        {
            sb.Append("\n\n").Append(feedbackHint);
        }

        return sb.ToString();
    }

    /// <summary><c>[1] Başlık — url  (Confluence, 12.03.2026)</c> veya <c>[2] Doğrulanmış bilgi: "Başlık" — onaylı, 05.10.2026</c>.</summary>
    public static string SourceLine(CitedSource c, DateTimeOffset now, int reviewAfterDays)
    {
        var h = c.Hit;
        var date = h.LastModified is { } d ? ContextFormatter.FormatDate(d) : null;
        if (h.Kind == KnowledgeSourceKind.KnowledgeBase)
        {
            var line = $"[{c.Number}] Doğrulanmış bilgi: \"{h.Title}\" — {Join(h.StatusLabel, date)}";
            return IsStale(h, now, reviewAfterDays) ? line + " (eskimiş olabilir)" : line;
        }

        var url = string.IsNullOrWhiteSpace(h.Url) ? "" : " — " + h.Url;
        return $"[{c.Number}] {h.Title}{url}  ({Join(ContextFormatter.SourceLabel(h), date)})";
    }

    /// <summary><c>#Kaynak</c> çıktısındaki tek satır.</summary>
    public static string DescribeLine(CitedSource c)
    {
        var h = c.Hit;
        var parts = new List<string> { ContextFormatter.SourceLabel(h) };
        if (h.Kind == KnowledgeSourceKind.KnowledgeBase)
        {
            AddIf(parts, h.StatusLabel);
        }
        else
        {
            AddIf(parts, h.SpaceKey);
        }

        if (h.LastModified is { } d)
        {
            parts.Add(ContextFormatter.FormatDate(d));
        }

        var url = string.IsNullOrWhiteSpace(h.Url) ? "" : " — " + h.Url;
        return $"[{c.Number}] {string.Join(" · ", parts)} — {h.Title}{url}";
    }

    internal static bool IsStale(KnowledgeHit hit, DateTimeOffset now, int reviewAfterDays) =>
        hit.Kind == KnowledgeSourceKind.KnowledgeBase
        && reviewAfterDays > 0
        && hit.LastModified is { } d
        && now - d > TimeSpan.FromDays(reviewAfterDays);

    private static string Join(string? a, string? b) =>
        string.Join(", ", new[] { a, b }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static void AddIf(List<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value!);
        }
    }
}

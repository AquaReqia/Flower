using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Flower.Agents.Knowledge;

namespace Flower.Agents.Agents;

/// <summary>Kaynakları Judge/Synthesizer'a giden numaralı bağlam metnine ve kullanıcıya giden kaynak satırlarına çevirir.</summary>
public static partial class ContextFormatter
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Numaralı kaynak bağlamı ([1] … [n]); snippet'ler <paramref name="maxCharsPerHit"/> ile kesilir.</summary>
    public static string FormatHits(IReadOnlyList<KnowledgeHit> hits, int maxCharsPerHit)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < hits.Count; i++)
        {
            var h = hits[i];
            if (i > 0)
            {
                sb.Append("\n\n");
            }

            sb.Append('[').Append(i + 1).Append("] (").Append(Header(h)).Append(") ").Append(h.Title);
            if (!string.IsNullOrWhiteSpace(h.Url))
            {
                sb.Append('\n').Append(h.Url);
            }

            var snippet = h.Snippet.Length > maxCharsPerHit ? h.Snippet[..maxCharsPerHit] + "…" : h.Snippet;
            sb.Append('\n').Append(snippet.Trim());
        }

        return sb.ToString();
    }

    /// <summary>Kullanıcıya gösterilen kaynak etiketi: "Confluence" veya "Doğrulanmış bilgi".</summary>
    public static string SourceLabel(KnowledgeHit hit) =>
        hit.Kind == KnowledgeSourceKind.KnowledgeBase
            ? "Doğrulanmış bilgi"
            : hit.SourceName.Length == 0 ? "Kaynak" : char.ToUpper(hit.SourceName[0], Tr) + hit.SourceName[1..];

    /// <summary>dd.MM.yyyy biçimi (yerel tarih).</summary>
    public static string FormatDate(DateTimeOffset date) => date.ToString("dd.MM.yyyy", Tr);

    /// <summary>Metindeki [n] atıflarını (geliş sırasıyla, tekrarsız) döndürür; [1, 3] gibi çoklu atıfları da okur.</summary>
    public static IReadOnlyList<int> ExtractCitations(string text, int maxSource)
    {
        var result = new List<int>();
        foreach (Match m in CitationGroup().Matches(text))
        {
            foreach (var part in m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                    && n >= 1 && n <= maxSource && !result.Contains(n))
                {
                    result.Add(n);
                }
            }
        }

        return result;
    }

    private static string Header(KnowledgeHit h)
    {
        var parts = new List<string> { SourceLabel(h) };
        if (h.Kind == KnowledgeSourceKind.KnowledgeBase)
        {
            if (!string.IsNullOrWhiteSpace(h.StatusLabel))
            {
                parts.Add(h.StatusLabel!);
            }

            if (h.Confidence is { } c)
            {
                parts.Add("güven " + c.ToString("0.00", CultureInfo.InvariantCulture));
            }
        }
        else if (!string.IsNullOrWhiteSpace(h.SpaceKey))
        {
            parts.Add("space: " + h.SpaceKey);
        }

        if (h.LastModified is { } d)
        {
            parts.Add(FormatDate(d));
        }

        return string.Join(" · ", parts);
    }

    [GeneratedRegex(@"\[(\d+(?:\s*,\s*\d+)*)\]")]
    private static partial Regex CitationGroup();
}

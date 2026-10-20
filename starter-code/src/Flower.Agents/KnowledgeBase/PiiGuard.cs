using System.Text.RegularExpressions;

namespace Flower.Agents.KnowledgeBase;

/// <summary>Bilgi deposuna kişisel veri yazılmasını engelleyen basit kontrol (TCKN, IBAN, kart, telefon).</summary>
public static partial class PiiGuard
{
    /// <summary>Kişisel veri bulunursa türünü, yoksa null döndürür.</summary>
    public static string? Detect(string text)
    {
        if (Iban().IsMatch(text))
        {
            return "IBAN";
        }

        if (Card().IsMatch(text))
        {
            return "kart numarası";
        }

        if (Tckn().Matches(text).Any(m => IsValidTckn(m.Value)))
        {
            return "T.C. kimlik numarası";
        }

        if (Phone().IsMatch(text))
        {
            return "telefon numarası";
        }

        return null;
    }

    // Rastgele 11 haneli sayıları (sipariş no vb.) TCKN sanmamak için resmi kontrol haneleri doğrulanır.
    internal static bool IsValidTckn(string s)
    {
        if (s.Length != 11 || s[0] == '0')
        {
            return false;
        }

        var d = s.Select(c => c - '0').ToArray();
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        var d10 = (((odd * 7) - even) % 10 + 10) % 10;
        var d11 = d.Take(10).Sum() % 10;
        return d[9] == d10 && d[10] == d11;
    }

    [GeneratedRegex(@"\bTR\s?\d{2}(?:\s?\d{4}){5}\s?\d{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex Iban();

    [GeneratedRegex(@"(?<!\d)(?:\d{4}[ -]?){3}\d{4}(?!\d)")]
    private static partial Regex Card();

    [GeneratedRegex(@"(?<!\d)[1-9]\d{10}(?!\d)")]
    private static partial Regex Tckn();

    [GeneratedRegex(@"(?<!\d)(?:\+90[\s-]?|0)?5\d{2}[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2}(?!\d)")]
    private static partial Regex Phone();
}

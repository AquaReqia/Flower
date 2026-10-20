using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Flower.Agents.Text;

/// <summary>Türkçe metin normalizasyonu ve basit anahtar kelime çıkarımı (LLM'siz yedek yol).</summary>
public static partial class KeywordExtractor
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "ve", "veya", "ile", "ama", "fakat", "ancak", "da", "de", "ki", "mi", "mı", "mu", "mü", "bu", "şu", "o",
        "bir", "iki", "çok", "az", "en", "daha", "gibi", "için", "kadar", "göre", "olan", "olarak", "olur", "oldu",
        "nedir", "neler", "nasıl", "neden", "niye", "kim", "kime", "kimin", "kimdir", "hangi", "hangisi", "ne", "nerede",
        "nereden", "ne zaman", "var", "yok", "mı", "midir", "mıdır", "dir", "dır", "tir", "tır", "ise", "yani",
        "ben", "sen", "biz", "siz", "onlar", "bana", "sana", "bize", "size", "benim", "bizim", "sizin",
        "lütfen", "acaba", "hakkında", "ilgili", "selam", "merhaba", "teşekkürler",
        "the", "a", "an", "of", "to", "in", "is", "are", "what", "who", "how", "which", "for", "and", "or",
    };

    /// <summary>Küçük harfe çevirir (Türkçe kurallarıyla), noktalamayı temizler.</summary>
    public static string Normalize(string text)
    {
        var lower = text.ToLower(Tr);
        var sb = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return MultiSpace().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Durak kelimeleri atılmış, tekrarsız anahtar kelimeler (geliş sırasıyla).</summary>
    public static IReadOnlyList<string> Extract(string text, int max = 8)
    {
        var result = new List<string>();
        foreach (var token in Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 2 || StopWords.Contains(token) || result.Contains(token))
            {
                continue;
            }

            result.Add(token);
            if (result.Count >= max)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>Anahtar kelimelerin metinde geçme oranı (0–1). Türkçe ekler için kelime başı eşleşmesi yeterli sayılır.</summary>
    public static double Overlap(IReadOnlyCollection<string> keywords, string text)
    {
        if (keywords.Count == 0)
        {
            return 0;
        }

        var normalized = " " + Normalize(text) + " ";
        var hits = keywords.Count(k => normalized.Contains(" " + Stem(k), StringComparison.Ordinal));
        return (double)hits / keywords.Count;
    }

    /// <summary>Kaba kök: 5 harften uzun kelimelerin ilk 5 harfi (Türkçe ekleri tolere etmek için).</summary>
    internal static string Stem(string word) => word.Length > 5 ? word[..5] : word;

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpace();
}

/// <summary>HTML / Confluence storage formatından düz metin.</summary>
public static partial class HtmlText
{
    public static string ToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        var s = BlockTags().Replace(html, "\n");
        s = Tags().Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = Spaces().Replace(s, " ");
        s = BlankLines().Replace(s, "\n\n");
        return s.Trim();
    }

    /// <summary>Teams HTML mesajı için kaçış.</summary>
    public static string Escape(string text) => WebUtility.HtmlEncode(text);

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/tr|/h[1-6]|/table)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTags();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t\r\f\v]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex BlankLines();
}

/// <summary>Uzun metinden anahtar kelimelere en yoğun pencereyi seçer.</summary>
public static class SnippetSelector
{
    public static string Select(string text, IReadOnlyCollection<string> keywords, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        var paragraphs = text.Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (paragraphs.Length == 0)
        {
            return text[..maxChars];
        }

        // Her paragraftan başlayan pencereyi puanla, en iyisini al.
        var bestStart = 0;
        var bestScore = -1.0;
        for (var i = 0; i < paragraphs.Length; i++)
        {
            var window = new StringBuilder();
            for (var j = i; j < paragraphs.Length && window.Length < maxChars; j++)
            {
                window.Append(paragraphs[j]).Append('\n');
            }

            var score = KeywordExtractor.Overlap(keywords, window.ToString());
            if (score > bestScore)
            {
                bestScore = score;
                bestStart = i;
            }
        }

        var sb = new StringBuilder();
        for (var j = bestStart; j < paragraphs.Length; j++)
        {
            if (sb.Length + paragraphs[j].Length + 1 > maxChars)
            {
                if (sb.Length == 0)
                {
                    sb.Append(paragraphs[j].AsSpan(0, maxChars));
                }

                break;
            }

            sb.Append(paragraphs[j]).Append('\n');
        }

        return (bestStart > 0 ? "… " : "") + sb.ToString().TrimEnd();
    }
}

/// <summary>Vektör yardımcıları.</summary>
public static class VectorMath
{
    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>float[] ↔ byte[] (veritabanında taşınabilir saklama; SQLite ve PostgreSQL'de çalışır).</summary>
    public static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] FromBytes(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, vector.Length * sizeof(float));
        return vector;
    }
}

/// <summary>Anahtar kelimelerden güvenli Confluence CQL üretir.</summary>
public static partial class CqlBuilder
{
    public static string? Build(IReadOnlyList<string> keywords, IReadOnlyCollection<string>? spaces, int maxKeywords = 8)
    {
        var terms = keywords
            .Select(Sanitize)
            .Where(k => k.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxKeywords)
            .ToList();

        if (terms.Count == 0)
        {
            return null;
        }

        var text = string.Join(" OR ", terms.Select(t => $"text ~ \"{Quote(t)}\""));
        var title = string.Join(" OR ", terms.Select(t => $"title ~ \"{Quote(t)}\""));
        var cql = $"type = page AND ({text} OR {title})";

        if (spaces is { Count: > 0 })
        {
            cql += " AND space in (" + string.Join(",", spaces.Select(s => $"\"{Quote(Sanitize(s))}\"")) + ")";
        }

        return cql;
    }

    /// <summary>Lucene/CQL özel karakterlerini atar.</summary>
    internal static string Sanitize(string term) => Special().Replace(term, " ").Trim();

    private static string Quote(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    [GeneratedRegex(@"[+\-&|!(){}\[\]^~*?\\:""/]+")]
    private static partial Regex Special();
}

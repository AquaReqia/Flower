using System.Globalization;
using System.Text.Json;
using Flower.Agents.Abstractions;
using Flower.Agents.Text;

namespace Flower.Agents.Knowledge.Mcp;

/// <summary>
/// MCP tool çıktısını <see cref="KnowledgeHit"/> listesine çevirir. Alan adlarında esnektir:
/// <c>id/pageId</c>, <c>title</c>, <c>url/link/_links.webui</c>, <c>space/spaceKey/space.key</c>,
/// <c>excerpt/snippet/content/body/text</c> (HTML temizlenir), <c>lastModified/updated</c>,
/// <c>author/createdBy</c>, <c>lastEditor/lastModifiedBy</c> (metin veya {email, username, displayName}).
/// JSON değilse tüm metin tek bir sonuç sayılır.
/// MCP server'ınızın çıktısı bu alanları içerirse ek kod gerekmez.
/// </summary>
public static class McpResultParser
{
    private static readonly string[] ArrayKeys = ["results", "items", "pages", "data", "hits", "documents"];
    private static readonly string[] IdKeys = ["id", "pageId", "page_id", "contentId", "content_id"];
    private static readonly string[] TitleKeys = ["title", "name", "pageTitle"];
    private static readonly string[] UrlKeys = ["url", "link", "webUrl", "web_url", "webui", "href"];
    private static readonly string[] SpaceKeys = ["spaceKey", "space_key", "space"];
    private static readonly string[] TextKeys = ["excerpt", "snippet", "summary", "content", "body", "text", "markdown"];
    private static readonly string[] DateKeys = ["lastModified", "last_modified", "updated", "updatedAt", "updated_at", "lastUpdated", "modified", "when"];
    private static readonly string[] AuthorKeys = ["author", "createdBy", "created_by", "creator", "owner"];
    private static readonly string[] EditorKeys = ["lastEditor", "last_editor", "lastModifiedBy", "last_modified_by", "updatedBy", "modifier"];

    public static IReadOnlyList<KnowledgeHit> Parse(string raw, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var trimmed = raw.Trim();
        if (trimmed[0] is '[' or '{')
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var items = FindItems(doc.RootElement);
                if (items is not null)
                {
                    return items.Value.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.Object)
                        .Select((e, i) => ToHit(e, sourceName, i))
                        .Where(h => h is not null)
                        .Select(h => h!)
                        .ToList();
                }

                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var single = ToHit(doc.RootElement, sourceName, 0);
                    return single is null ? [] : [single];
                }
            }
            catch (JsonException)
            {
                // JSON değil; düz metin olarak devam.
            }
        }

        return
        [
            new KnowledgeHit
            {
                SourceName = sourceName,
                Kind = KnowledgeSourceKind.Mcp,
                SourceId = "text-0",
                Title = sourceName,
                Snippet = HtmlText.ToPlainText(trimmed),
            },
        ];
    }

    private static JsonElement? FindItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in ArrayKeys)
        {
            if (TryGet(root, key, out var v) && v.ValueKind == JsonValueKind.Array)
            {
                return v;
            }
        }

        return null;
    }

    private static KnowledgeHit? ToHit(JsonElement e, string sourceName, int index)
    {
        var title = FirstString(e, TitleKeys);
        var text = FirstString(e, TextKeys);
        if (title is null && text is null)
        {
            return null;
        }

        var url = FirstString(e, UrlKeys);
        if (url is null && TryGet(e, "_links", out var links) && links.ValueKind == JsonValueKind.Object)
        {
            var webui = FirstString(links, ["webui", "tinyui"]);
            var baseUrl = FirstString(links, ["base"]);
            url = webui is null ? null : (baseUrl ?? "") + webui;
        }

        return new KnowledgeHit
        {
            SourceName = sourceName,
            Kind = KnowledgeSourceKind.Mcp,
            SourceId = FirstString(e, IdKeys) ?? url ?? $"{index}",
            Title = title ?? "(başlıksız)",
            Snippet = HtmlText.ToPlainText(text ?? ""),
            Url = url,
            SpaceKey = ReadSpace(e),
            LastModified = ReadDate(e),
            Author = ReadPerson(e, AuthorKeys),
            LastEditor = ReadPerson(e, EditorKeys),
            Score = TryGet(e, "score", out var s) && s.TryGetDouble(out var sv) ? sv : 0,
        };
    }

    private static string? ReadSpace(JsonElement e)
    {
        foreach (var key in SpaceKeys)
        {
            if (!TryGet(e, key, out var v))
            {
                continue;
            }

            if (v.ValueKind == JsonValueKind.String)
            {
                return v.GetString();
            }

            if (v.ValueKind == JsonValueKind.Object)
            {
                return FirstString(v, ["key", "spaceKey", "name"]);
            }
        }

        return null;
    }

    private static DateTimeOffset? ReadDate(JsonElement e)
    {
        var s = FirstString(e, DateKeys);
        if (s is null)
        {
            if (TryGet(e, "history", out var h) && h.ValueKind == JsonValueKind.Object &&
                TryGet(h, "lastUpdated", out var lu) && lu.ValueKind == JsonValueKind.Object)
            {
                s = FirstString(lu, ["when"]);
            }
        }

        return s is not null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }

    private static PersonRef? ReadPerson(JsonElement e, string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGet(e, key, out var v))
            {
                continue;
            }

            if (v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (string.IsNullOrWhiteSpace(s))
                {
                    continue;
                }

                return s.Contains('@') ? new PersonRef(s) : new PersonRef(null, s);
            }

            if (v.ValueKind == JsonValueKind.Object)
            {
                var p = new PersonRef(
                    FirstString(v, ["email", "mail", "emailAddress"]),
                    FirstString(v, ["username", "userName", "name", "accountId", "userKey"]),
                    FirstString(v, ["displayName", "display_name", "fullName"]));
                if (!p.IsEmpty)
                {
                    return p;
                }
            }
        }

        return null;
    }

    private static string? FirstString(JsonElement e, string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGet(e, key, out var v))
            {
                if (v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                {
                    return v.GetString();
                }

                if (v.ValueKind == JsonValueKind.Number)
                {
                    return v.GetRawText();
                }

                // Confluence REST tarzı: body.storage.value / body.view.value
                if (v.ValueKind == JsonValueKind.Object)
                {
                    foreach (var inner in new[] { "storage", "view", "export_view" })
                    {
                        if (TryGet(v, inner, out var iv) && iv.ValueKind == JsonValueKind.Object &&
                            TryGet(iv, "value", out var val) && val.ValueKind == JsonValueKind.String)
                        {
                            return val.GetString();
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Büyük/küçük harf duyarsız property araması.</summary>
    private static bool TryGet(JsonElement e, string key, out JsonElement value)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty(key, out value))
            {
                return true;
            }

            foreach (var p in e.EnumerateObject())
            {
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using JekyllNet.Core.Models;
using JekyllNet.Core.Plugins;
using Markdig;

namespace JekyllNet.Core.Rendering;

public sealed partial class TemplateRenderer
{
    private string RenderCustomTag(
        string template,
        string tagName,
        string tagContent,
        Dictionary<string, object?> scope,
        IReadOnlyDictionary<string, string>? includes,
        ref int index)
    {
        if (PluginRegistry is null)
            return string.Empty;

        var markup = tagContent.Length > tagName.Length
            ? tagContent[tagName.Length..].Trim()
            : string.Empty;

        if (string.Equals(tagName, "bibliography", StringComparison.OrdinalIgnoreCase))
        {
            return RenderBibliographyTag(markup, scope);
        }

        if (string.Equals(tagName, "cite", StringComparison.OrdinalIgnoreCase))
        {
            return RenderCiteTag(markup, scope);
        }

        if (string.Equals(tagName, "reference", StringComparison.OrdinalIgnoreCase))
        {
            return RenderReferenceTag(markup, scope);
        }

        // Check block plugins first (they consume content until endXXX)
        if (PluginRegistry.Blocks.TryGetValue(tagName, out var block))
        {
            index = ExtractBlockBody(template, index, tagName, "end" + tagName, out var blockBody);
            return block.Render(markup, blockBody, MakePluginContext(scope));
        }

        // Inline tag plugins
        if (PluginRegistry.Tags.TryGetValue(tagName, out var tag))
            return tag.Render(markup, MakePluginContext(scope));

        return string.Empty;
    }

    private string RenderBibliographyTag(string markup, Dictionary<string, object?> scope)
    {
        var entries = GetBibliographyEntries(scope);
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        if (markup.Contains("--cited_in_order", StringComparison.OrdinalIgnoreCase))
        {
            var citedKeys = ResolveCitedKeys(scope);
            if (citedKeys.Count > 0)
            {
                var byKey = entries
                    .Where(entry => entry.TryGetValue("key", out var key) && !string.IsNullOrWhiteSpace(key?.ToString()))
                    .ToDictionary(entry => entry["key"]!.ToString()!, StringComparer.OrdinalIgnoreCase);

                entries = citedKeys
                    .Where(key => byKey.ContainsKey(key))
                    .Select(key => byKey[key])
                    .ToList();
            }
        }

        var html = new System.Text.StringBuilder();
        html.Append("<ol class=\"bibliography\">");
        foreach (var entry in entries)
        {
            var key = entry.TryGetValue("key", out var keyValue) ? keyValue?.ToString() ?? string.Empty : string.Empty;
            var title = HtmlEncode(entry.TryGetValue("title", out var titleValue) ? titleValue?.ToString() ?? string.Empty : string.Empty);
            var author = HtmlEncode(entry.TryGetValue("author", out var authorValue) ? authorValue?.ToString() ?? string.Empty : string.Empty);
            var year = HtmlEncode(entry.TryGetValue("year", out var yearValue) ? yearValue?.ToString() ?? string.Empty : string.Empty);

            html.Append("<li");
            if (!string.IsNullOrWhiteSpace(key))
            {
                html.Append(" id=\"").Append(HtmlEncode(key)).Append("\"");
            }

            html.Append("><span class=\"author\">").Append(author)
                .Append("</span>. <span class=\"title\">").Append(title)
                .Append("</span>");

            if (!string.IsNullOrWhiteSpace(year))
            {
                html.Append(" (<span class=\"year\">").Append(year).Append("</span>)");
            }

            html.Append(".</li>");
        }

        html.Append("</ol>");
        return html.ToString();
    }

    private string RenderCiteTag(string markup, Dictionary<string, object?> scope)
    {
        var keys = ParseCitationKeys(markup);
        if (keys.Count == 0)
        {
            return string.Empty;
        }

        var rendered = new List<string>();
        foreach (var normalizedKey in keys)
        {
            AddCitedKey(scope, normalizedKey);

            if (!TryGetBibliographyEntryByKey(scope, normalizedKey, out var entry))
            {
                rendered.Add("[" + HtmlEncode(normalizedKey) + "]");
                continue;
            }

            var author = entry.TryGetValue("author", out var authorValue) ? authorValue?.ToString() ?? string.Empty : string.Empty;
            var year = entry.TryGetValue("year", out var yearValue) ? yearValue?.ToString() ?? string.Empty : string.Empty;
            var label = string.IsNullOrWhiteSpace(author)
                ? normalizedKey
                : author.Split(" and ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? normalizedKey;

            var encodedKey = HtmlEncode(normalizedKey);
            var encodedLabel = HtmlEncode(label);
            var encodedYear = HtmlEncode(year);
            rendered.Add(string.IsNullOrWhiteSpace(encodedYear)
                ? "<a class=\"citation\" href=\"#" + encodedKey + "\">" + encodedLabel + "</a>"
                : "<a class=\"citation\" href=\"#" + encodedKey + "\">" + encodedLabel + ", " + encodedYear + "</a>");
        }

        return string.Join("; ", rendered);
    }

    private string RenderReferenceTag(string markup, Dictionary<string, object?> scope)
    {
        var key = ParseCitationKeys(markup).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        AddCitedKey(scope, key);
        if (!TryGetBibliographyEntryByKey(scope, key, out var entry))
        {
            return "[" + HtmlEncode(key) + "]";
        }

        var author = HtmlEncode(entry.TryGetValue("author", out var authorValue) ? authorValue?.ToString() ?? string.Empty : string.Empty);
        var title = HtmlEncode(entry.TryGetValue("title", out var titleValue) ? titleValue?.ToString() ?? string.Empty : string.Empty);
        var year = HtmlEncode(entry.TryGetValue("year", out var yearValue) ? yearValue?.ToString() ?? string.Empty : string.Empty);
        if (string.IsNullOrWhiteSpace(year))
        {
            return author + ". " + title + ".";
        }

        return author + ". " + title + " (" + year + ").";
    }

    private static List<string> ParseCitationKeys(string markup)
    {
        return markup
            .Split([' ', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim('"', '\''))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<Dictionary<string, object?>> ResolveCitedKeysFromSource(Dictionary<string, object?> scope)
    {
        if (!scope.TryGetValue("page", out var pageObj)
            || pageObj is not IReadOnlyDictionary<string, object?> page
            || !page.TryGetValue("path", out var pathObj)
            || string.IsNullOrWhiteSpace(pathObj?.ToString())
            || string.IsNullOrWhiteSpace(SourceDirectory))
        {
            return [];
        }

        var sourcePath = Path.Combine(SourceDirectory, pathObj!.ToString()!.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(sourcePath))
        {
            return [];
        }

        var content = File.ReadAllText(sourcePath);
        var matches = Regex.Matches(content, "\\{\\%\\s*cite\\s+([^%]+?)\\s*\\%\\}", RegexOptions.IgnoreCase);
        var byKey = GetBibliographyEntries(scope)
            .Where(entry => entry.TryGetValue("key", out var key) && !string.IsNullOrWhiteSpace(key?.ToString()))
            .ToDictionary(entry => entry["key"]!.ToString()!, StringComparer.OrdinalIgnoreCase);

        var result = new List<Dictionary<string, object?>>();
        foreach (Match match in matches)
        {
            var key = match.Groups[1].Value.Trim().Trim('"', '\'');
            if (byKey.TryGetValue(key, out var entry))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private List<string> ResolveCitedKeys(Dictionary<string, object?> scope)
    {
        if (scope.TryGetValue("__cited_keys", out var citedObj) && citedObj is List<string> cited && cited.Count > 0)
        {
            return cited;
        }

        var fromSource = ResolveCitedKeysFromSource(scope)
            .Select(entry => entry.TryGetValue("key", out var key) ? key?.ToString() ?? string.Empty : string.Empty)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToList();

        scope["__cited_keys"] = fromSource;
        return fromSource;
    }

    private static string HtmlEncode(string value) => WebUtility.HtmlEncode(value);

    private static void AddCitedKey(Dictionary<string, object?> scope, string key)
    {
        if (!scope.TryGetValue("__cited_keys", out var obj) || obj is not List<string> citedKeys)
        {
            citedKeys = [];
            scope["__cited_keys"] = citedKeys;
        }

        if (!citedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            citedKeys.Add(key);
        }
    }

    private static bool TryGetBibliographyEntryByKey(
        IReadOnlyDictionary<string, object?> scope,
        string key,
        out Dictionary<string, object?> entry)
    {
        entry = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!scope.TryGetValue("site", out var siteObj)
            || siteObj is not IReadOnlyDictionary<string, object?> site
            || !site.TryGetValue("bibliography_by_key", out var byKeyObj)
            || byKeyObj is not IReadOnlyDictionary<string, object?> byKey
            || !byKey.TryGetValue(key, out var entryObj)
            || entryObj is not IReadOnlyDictionary<string, object?> entryReadonly)
        {
            return false;
        }

        entry = new Dictionary<string, object?>(entryReadonly, StringComparer.OrdinalIgnoreCase);
        return true;
    }

    private static List<Dictionary<string, object?>> GetBibliographyEntries(IReadOnlyDictionary<string, object?> scope)
    {
        if (!scope.TryGetValue("site", out var siteObj)
            || siteObj is not IReadOnlyDictionary<string, object?> site
            || !site.TryGetValue("bibliography_entries", out var entriesObj)
            || entriesObj is not IEnumerable<object?> entries)
        {
            return [];
        }

        return entries
            .OfType<IReadOnlyDictionary<string, object?>>()
            .Select(entry => new Dictionary<string, object?>(entry, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private JekyllPluginContext MakePluginContext(IReadOnlyDictionary<string, object?> scope)
    {
        var siteConfig = scope.TryGetValue("site", out var site)
                         && site is IReadOnlyDictionary<string, object?> sd
            ? sd
            : (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        return new JekyllPluginContext
        {
            Variables = scope,
            SiteConfig = siteConfig,
            SourceDirectory = SourceDirectory
        };
    }

}

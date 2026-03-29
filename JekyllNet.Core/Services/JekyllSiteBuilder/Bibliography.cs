using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DartSassHost;
using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.Jint;
using Markdig;
using JekyllNet.Core.Models;
using JekyllNet.Core.Parsers;
using JekyllNet.Core.Plugins;
using JekyllNet.Core.Plugins.Loading;
using JekyllNet.Core.Rendering;
using JekyllNet.Core.Translation;
using YamlDotNet.Serialization;

namespace JekyllNet.Core.Services;

public sealed partial class JekyllSiteBuilder
{
    private static List<Dictionary<string, object?>> LoadBibliographyEntries(string sourceDirectory)
    {
        var bibliographyPath = Path.Combine(sourceDirectory, "_bibliography", "papers.bib");
        if (!File.Exists(bibliographyPath))
        {
            return [];
        }

        var content = File.ReadAllText(bibliographyPath);
        var entries = new List<Dictionary<string, object?>>();
        var index = 0;

        while (index < content.Length)
        {
            var at = content.IndexOf('@', index);
            if (at < 0)
            {
                break;
            }

            var brace = content.IndexOf('{', at);
            if (brace < 0)
            {
                break;
            }

            var type = content[(at + 1)..brace].Trim();
            if (string.Equals(type, "string", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "comment", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "preamble", StringComparison.OrdinalIgnoreCase))
            {
                index = brace + 1;
                continue;
            }

            var bodyStart = brace + 1;
            var depth = 1;
            var i = bodyStart;
            for (; i < content.Length; i++)
            {
                var ch = content[i];
                if (ch == '{')
                {
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }

            if (depth != 0)
            {
                break;
            }

            var body = content[bodyStart..i];
            index = i + 1;

            var firstComma = body.IndexOf(',');
            if (firstComma <= 0)
            {
                continue;
            }

            var key = body[..firstComma].Trim();
            var fieldsText = body[(firstComma + 1)..];
            var fields = ParseBibFields(fieldsText);

            var entry = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = type,
                ["key"] = key,
                ["bibtex"] = "@" + type + "{" + body + "}",
                ["title"] = fields.TryGetValue("title", out var title) ? title : key,
                ["author"] = fields.TryGetValue("author", out var author) ? author : string.Empty,
                ["year"] = fields.TryGetValue("year", out var year) ? year : string.Empty
            };

            foreach (var field in fields)
            {
                entry[field.Key] = field.Value;
            }

            entries.Add(entry);
        }

        return entries
            .OrderByDescending(entry => ParseYear(entry.TryGetValue("year", out var year) ? year?.ToString() : null))
            .ThenBy(entry => entry.TryGetValue("key", out var key) ? key?.ToString() : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> ParseBibFields(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var current = new System.Text.StringBuilder();
        var parts = new List<string>();
        var braceDepth = 0;
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                var escaped = i > 0 && text[i - 1] == '\\';
                if (!escaped)
                {
                    inQuotes = !inQuotes;
                }
            }

            if (!inQuotes)
            {
                if (ch == '{')
                {
                    braceDepth++;
                }
                else if (ch == '}')
                {
                    braceDepth = Math.Max(0, braceDepth - 1);
                }
            }

            if (ch == ',' && braceDepth == 0 && !inQuotes)
            {
                var part = current.ToString().Trim();
                if (part.Length > 0)
                {
                    parts.Add(part);
                }

                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        var tail = current.ToString().Trim();
        if (tail.Length > 0)
        {
            parts.Add(tail);
        }

        foreach (var part in parts)
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            result[key] = StripBibValueDelimiters(value);
        }

        return result;
    }

    private static string StripBibValueDelimiters(string value)
    {
        var result = value.Trim();
        while (result.Length >= 2)
        {
            if ((result[0] == '{' && result[^1] == '}') || (result[0] == '"' && result[^1] == '"'))
            {
                result = result[1..^1].Trim();
                continue;
            }

            break;
        }

        return result;
    }

    private static int ParseYear(string? value)
        => int.TryParse(value, out var year) ? year : int.MinValue;

}

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
    private static IEnumerable<object?> ConvertToSequence(object? value)
    {
        return value switch
        {
            null => Array.Empty<object?>(),
            string => Array.Empty<object?>(),
            IEnumerable<JekyllContentItem> items => items.Cast<object?>(),
            IEnumerable<object?> items => items,
            System.Collections.IEnumerable items => items.Cast<object?>(),
            _ => Array.Empty<object?>()
        };
    }

    private static bool WhereMatch(object? item, string propertyPath, object? expectedValue)
    {
        var actualValue = ResolveObjectPath(item, propertyPath);
        if (expectedValue is null)
        {
            return actualValue switch
            {
                null => false,
                bool boolValue => boolValue,
                string text => !string.IsNullOrWhiteSpace(text),
                _ => true
            };
        }

        return LiquidValueComparer.Instance.Compare(actualValue, expectedValue) == 0;
    }

    private static object? ResolveObjectPath(object? item, string propertyPath)
    {
        if (item is null || string.IsNullOrWhiteSpace(propertyPath))
        {
            return item;
        }

        object? current = item is JekyllContentItem contentItem
            ? ToLiquidObject(contentItem)
            : item;

        foreach (var segment in propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryGetDictionaryValue(current, segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static object? NormalizeJsonValue(object? value)
    {
        return value switch
        {
            null => null,
            JekyllContentItem item => NormalizeJsonValue(ToLiquidObject(item)),
            IReadOnlyDictionary<string, object?> readOnlyDictionary => readOnlyDictionary.ToDictionary(
                pair => pair.Key,
                pair => NormalizeJsonValue(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            IEnumerable<object?> sequence => sequence.Select(NormalizeJsonValue).ToList(),
            System.Collections.IEnumerable sequence when value is not string => sequence.Cast<object?>().Select(NormalizeJsonValue).ToList(),
            _ => value
        };
    }

    private static bool TryResolveObject(IReadOnlyDictionary<string, object?> variables, string path, out object? value)
    {
        object? current = variables;

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryGetDictionaryValue(current, segment, out var next))
            {
                current = next;
                continue;
            }

            value = null;
            return false;
        }

        value = current;
        return true;
    }

    private static bool TryGetDictionaryValue(object? current, string key, out object? value)
    {
        switch (current)
        {
            case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                if (readOnlyDictionary.TryGetValue(key, out var readOnlyNext))
                {
                    value = readOnlyNext;
                    return true;
                }

                foreach (var pair in readOnlyDictionary)
                {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = pair.Value;
                        return true;
                    }
                }

                break;

            case IEnumerable<KeyValuePair<string, object?>> pairs:
                foreach (var pair in pairs)
                {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = pair.Value;
                        return true;
                    }
                }

                break;
        }

        value = null;
        return false;
    }

    private static Dictionary<string, object?> ToLiquidObject(JekyllContentItem item)
        => new(item.FrontMatter, StringComparer.OrdinalIgnoreCase)
        {
            ["content"] = item.RenderedContent,
            ["url"] = item.Url,
            ["path"] = item.RelativePath.Replace('\\', '/'),
            ["collection"] = item.Collection,
            ["date"] = item.Date,
            ["title"] = item.FrontMatter.TryGetValue("title", out var title) ? title : Path.GetFileNameWithoutExtension(item.RelativePath)
        };

}

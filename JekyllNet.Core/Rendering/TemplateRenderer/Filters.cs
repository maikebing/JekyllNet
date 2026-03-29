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
    private object? ResolveExpression(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        var parts = expression.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        object? current = ResolveBase(parts[0], variables);

        foreach (var filterPart in parts.Skip(1))
        {
            var filterTokens = filterPart.Split(':', 2, StringSplitOptions.TrimEntries);
            current = ApplyFilter(
                current,
                filterTokens[0],
                filterTokens.Length > 1 ? filterTokens[1] : null,
                variables);
        }

        return current;
    }

    private static object? ResolveBase(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        if (expression.Length >= 2
            && ((expression.StartsWith('"') && expression.EndsWith('"')) || (expression.StartsWith('\'') && expression.EndsWith('\''))))
        {
            return expression[1..^1];
        }

        if (int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        if (bool.TryParse(expression, out var boolValue))
        {
            return boolValue;
        }

        return TryResolveObject(variables, expression, out var value) ? value : null;
    }

    private object? ApplyFilter(object? value, string filterName, string? argument, IReadOnlyDictionary<string, object?> variables)
    {
        return filterName switch
        {
            "upcase" => value?.ToString()?.ToUpperInvariant(),
            "downcase" => value?.ToString()?.ToLowerInvariant(),
            "default" => string.IsNullOrWhiteSpace(value?.ToString()) ? ResolveSingleArgument(argument, variables)?.ToString() : value,
            "date" => ApplyDateFilter(value, ResolveSingleArgument(argument, variables)?.ToString()),
            "size" => ApplySizeFilter(value),
            "join" => ApplyJoinFilter(value, ResolveSingleArgument(argument, variables)?.ToString()),
            "split" => ApplySplitFilter(value, ResolveSingleArgument(argument, variables)?.ToString()),
            "strip" => value?.ToString()?.Trim(),
            "strip_html" => ApplyStripHtmlFilter(value),
            "strip_newlines" => ApplyStripNewlinesFilter(value),
            "append" => (value?.ToString() ?? string.Empty) + (ResolveSingleArgument(argument, variables)?.ToString() ?? string.Empty),
            "prepend" => (ResolveSingleArgument(argument, variables)?.ToString() ?? string.Empty) + (value?.ToString() ?? string.Empty),
            "replace" => ApplyReplaceFilter(value, argument),
            "replace_first" => ApplyReplaceFirstFilter(value, argument),
            "remove" => ApplyRemoveFilter(value, argument, variables),
            "remove_first" => ApplyRemoveFirstFilter(value, argument, variables),
            "first" => ApplyFirstFilter(value),
            "last" => ApplyLastFilter(value),
            "where" => ApplyWhereFilter(value, argument, variables),
            "sort" => ApplySortFilter(value, argument, variables),
            "map" => ApplyMapFilter(value, argument, variables),
            "compact" => ApplyCompactFilter(value),
            "jsonify" => ApplyJsonifyFilter(value),
            "slugify" => ApplySlugifyFilter(value),
            "relative_url" => ApplyRelativeUrlFilter(value, variables),
            "absolute_url" => ApplyAbsoluteUrlFilter(value, variables),
            "markdownify" => ApplyMarkdownifyFilter(value),
            "newline_to_br" => ApplyNewlineToBrFilter(value),
            "escape" => ApplyEscapeFilter(value),
            "escape_once" => ApplyEscapeFilter(value),
            _ when PluginRegistry?.FiltersByName.TryGetValue(filterName, out var customFilter) == true
                => customFilter!.Apply(filterName, value, argument, MakePluginContext(variables)),
            _ => value
        };
    }

    private static object ApplySizeFilter(object? value)
    {
        return value switch
        {
            null => 0,
            string text => text.Length,
            IEnumerable<object?> sequence => sequence.Count(),
            _ => value.ToString()?.Length ?? 0
        };
    }

    private static object ApplyJoinFilter(object? value, string? argument)
    {
        if (value is IEnumerable<object?> sequence)
        {
            return string.Join(argument ?? string.Empty, sequence.Select(x => x?.ToString() ?? string.Empty));
        }

        return value?.ToString() ?? string.Empty;
    }

    private static object ApplySplitFilter(object? value, string? argument)
    {
        return (value?.ToString() ?? string.Empty)
            .Split(argument ?? string.Empty, StringSplitOptions.None)
            .Cast<object?>()
            .ToList();
    }

    private static object ApplyReplaceFilter(object? value, string? argument)
    {
        var source = value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(argument))
        {
            return source;
        }

        var parts = argument.Split(',', 2, StringSplitOptions.TrimEntries);
        var oldValue = TrimQuotes(parts.ElementAtOrDefault(0) ?? string.Empty);
        var newValue = TrimQuotes(parts.ElementAtOrDefault(1) ?? string.Empty);
        return source.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    private static object ApplyReplaceFirstFilter(object? value, string? argument)
    {
        var source = value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(argument))
        {
            return source;
        }

        var parts = argument.Split(',', 2, StringSplitOptions.TrimEntries);
        var oldValue = TrimQuotes(parts.ElementAtOrDefault(0) ?? string.Empty);
        var newValue = TrimQuotes(parts.ElementAtOrDefault(1) ?? string.Empty);
        var index = source.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0)
        {
            return source;
        }

        return source[..index] + newValue + source[(index + oldValue.Length)..];
    }

    private static object ApplyRemoveFilter(object? value, string? argument, IReadOnlyDictionary<string, object?> variables)
    {
        var source = value?.ToString() ?? string.Empty;
        var target = ResolveSingleArgument(argument, variables)?.ToString() ?? string.Empty;
        return string.IsNullOrEmpty(target)
            ? source
            : source.Replace(target, string.Empty, StringComparison.Ordinal);
    }

    private static object ApplyRemoveFirstFilter(object? value, string? argument, IReadOnlyDictionary<string, object?> variables)
    {
        var source = value?.ToString() ?? string.Empty;
        var target = ResolveSingleArgument(argument, variables)?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(target))
        {
            return source;
        }

        var index = source.IndexOf(target, StringComparison.Ordinal);
        return index < 0
            ? source
            : source[..index] + source[(index + target.Length)..];
    }

    private static object? ApplyFirstFilter(object? value)
    {
        return value switch
        {
            string text when text.Length > 0 => text[0].ToString(),
            IEnumerable<object?> sequence => sequence.FirstOrDefault(),
            _ => value
        };
    }

    private static object? ApplyLastFilter(object? value)
    {
        return value switch
        {
            string text when text.Length > 0 => text[^1].ToString(),
            IEnumerable<object?> sequence => sequence.LastOrDefault(),
            _ => value
        };
    }

    private static string? ApplyDateFilter(object? value, string? argument)
    {
        if (value is null)
        {
            return null;
        }

        if (value is DateTimeOffset dto)
        {
            return dto.ToString(NormalizeDateFormat(argument), CultureInfo.InvariantCulture);
        }

        if (DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
        {
            return dto.ToString(NormalizeDateFormat(argument), CultureInfo.InvariantCulture);
        }

        return value.ToString();
    }

    private static string NormalizeDateFormat(string? liquidFormat)
    {
        if (string.IsNullOrWhiteSpace(liquidFormat))
        {
            return "yyyy-MM-dd";
        }

        return liquidFormat
            .Replace("%Y", "yyyy", StringComparison.Ordinal)
            .Replace("%m", "MM", StringComparison.Ordinal)
            .Replace("%d", "dd", StringComparison.Ordinal)
            .Replace("%H", "HH", StringComparison.Ordinal)
            .Replace("%M", "mm", StringComparison.Ordinal)
            .Replace("%S", "ss", StringComparison.Ordinal)
            .Replace("%b", "MMM", StringComparison.Ordinal);
    }

    private static string ApplyRelativeUrlFilter(object? value, IReadOnlyDictionary<string, object?> variables)
    {
        var path = value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (IsAbsoluteUrl(path))
        {
            return path;
        }

        var baseUrl = TryResolveObject(variables, "site.baseurl", out var baseUrlValue)
            ? baseUrlValue?.ToString() ?? string.Empty
            : string.Empty;

        return CombineUrlParts(baseUrl, path);
    }

    private static string ApplyAbsoluteUrlFilter(object? value, IReadOnlyDictionary<string, object?> variables)
    {
        var path = value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (IsAbsoluteUrl(path))
        {
            return path;
        }

        var siteUrl = TryResolveObject(variables, "site.url", out var siteUrlValue)
            ? siteUrlValue?.ToString() ?? string.Empty
            : string.Empty;
        var relativeUrl = ApplyRelativeUrlFilter(path, variables);

        if (string.IsNullOrWhiteSpace(siteUrl))
        {
            return relativeUrl;
        }

        return CombineUrlParts(siteUrl, relativeUrl);
    }

    private static string ApplyMarkdownifyFilter(object? value)
        => Markdown.ToHtml(value?.ToString() ?? string.Empty, MarkdownPipeline).TrimEnd();

    private static string ApplyStripHtmlFilter(object? value)
        => System.Text.RegularExpressions.Regex.Replace(value?.ToString() ?? string.Empty, "<.*?>", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

    private static string ApplyStripNewlinesFilter(object? value)
        => (value?.ToString() ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

    private static string ApplyNewlineToBrFilter(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("\r\n", "<br />\n", StringComparison.Ordinal)
            .Replace("\n", "<br />\n", StringComparison.Ordinal)
            .Replace("\r", "<br />\n", StringComparison.Ordinal);
    }

    private static string ApplyEscapeFilter(object? value)
        => System.Net.WebUtility.HtmlEncode(value?.ToString() ?? string.Empty);

    private static object ApplyWhereFilter(object? value, string? argument, IReadOnlyDictionary<string, object?> variables)
    {
        var arguments = ParseFilterArguments(argument, variables);
        var propertyPath = arguments.ElementAtOrDefault(0)?.ToString() ?? string.Empty;
        var expectedValue = arguments.Count > 1 ? arguments[1] : null;
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return ConvertToSequence(value).ToList();
        }

        return ConvertToSequence(value)
            .Where(item => WhereMatch(item, propertyPath, expectedValue))
            .ToList();
    }

    private static object ApplySortFilter(object? value, string? argument, IReadOnlyDictionary<string, object?> variables)
    {
        var propertyPath = ResolveSingleArgument(argument, variables)?.ToString();
        var sequence = ConvertToSequence(value);

        return string.IsNullOrWhiteSpace(propertyPath)
            ? sequence.OrderBy(item => item, LiquidValueComparer.Instance).ToList()
            : sequence.OrderBy(item => ResolveObjectPath(item, propertyPath), LiquidValueComparer.Instance).ToList();
    }

    private static object ApplyMapFilter(object? value, string? argument, IReadOnlyDictionary<string, object?> variables)
    {
        var propertyPath = ResolveSingleArgument(argument, variables)?.ToString();
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return ConvertToSequence(value).ToList();
        }

        return ConvertToSequence(value)
            .Select(item => ResolveObjectPath(item, propertyPath))
            .ToList();
    }

    private static object ApplyCompactFilter(object? value)
    {
        return ConvertToSequence(value)
            .Where(item => item is not null)
            .ToList();
    }

    private static string ApplyJsonifyFilter(object? value)
        => JsonSerializer.Serialize(NormalizeJsonValue(value));

    private static string ApplySlugifyFilter(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var slug = new System.Text.StringBuilder();
        var previousWasHyphen = false;

        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                slug.Append(character);
                previousWasHyphen = false;
                continue;
            }

            if (previousWasHyphen)
            {
                continue;
            }

            slug.Append('-');
            previousWasHyphen = true;
        }

        return slug.ToString().Trim('-');
    }

    private static string CombineUrlParts(string left, string right)
    {
        var normalizedLeft = (left ?? string.Empty).Trim();
        var normalizedRight = (right ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedLeft))
        {
            return EnsureLeadingSlash(normalizedRight);
        }

        if (string.IsNullOrWhiteSpace(normalizedRight))
        {
            return normalizedLeft;
        }

        return $"{normalizedLeft.TrimEnd('/')}/{normalizedRight.TrimStart('/')}";
    }

    private static string EnsureLeadingSlash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "/";
        }

        return value.StartsWith('/') ? value : "/" + value;
    }

    private static bool IsAbsoluteUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile;

    private static object? ResolveSingleArgument(string? argument, IReadOnlyDictionary<string, object?> variables)
        => ParseFilterArguments(argument, variables).FirstOrDefault();

    private static List<object?> ParseFilterArguments(string? argument, IReadOnlyDictionary<string, object?> variables)
    {
        var results = new List<object?>();
        if (string.IsNullOrWhiteSpace(argument))
        {
            return results;
        }

        var current = new System.Text.StringBuilder();
        char? quote = null;

        foreach (var character in argument)
        {
            if (quote is null && character is '\'' or '"')
            {
                quote = character;
                current.Append(character);
                continue;
            }

            if (quote is not null)
            {
                current.Append(character);
                if (character == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (character == ',')
            {
                AddArgument(results, current, variables);
                continue;
            }

            current.Append(character);
        }

        AddArgument(results, current, variables);
        return results;
    }

    private static void AddArgument(List<object?> results, System.Text.StringBuilder current, IReadOnlyDictionary<string, object?> variables)
    {
        if (current.Length == 0)
        {
            return;
        }

        var token = current.ToString().Trim();
        current.Clear();
        if (token.Length == 0)
        {
            return;
        }

        results.Add(ResolveBase(token, variables));
    }

    private static string TrimQuotes(string value)
    {
        if (value.Length >= 2
            && ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            return value[1..^1];
        }

        return value;
    }

}

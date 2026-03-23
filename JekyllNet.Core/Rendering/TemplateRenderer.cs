using System.Globalization;
using System.Text.RegularExpressions;
using JekyllNet.Core.Models;

namespace JekyllNet.Core.Rendering;

public sealed partial class TemplateRenderer
{
    public string Render(string template, IReadOnlyDictionary<string, object?> variables, IReadOnlyDictionary<string, string>? includes = null)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var rendered = template;
        rendered = ProcessIncludes(rendered, variables, includes);
        rendered = ProcessForLoops(rendered, variables, includes);
        rendered = ProcessIfBlocks(rendered, variables, includes);
        rendered = ProcessAssignBlocks(rendered, variables, includes);
        rendered = ReplaceVariables(rendered, variables);

        return rendered;
    }

    private string ProcessIncludes(string template, IReadOnlyDictionary<string, object?> variables, IReadOnlyDictionary<string, string>? includes)
    {
        if (includes is null || includes.Count == 0)
        {
            return template;
        }

        return IncludePattern().Replace(template, match =>
        {
            var includeName = match.Groups[1].Value.Trim().Trim('"', '\'');
            if (!includes.TryGetValue(includeName, out var includeTemplate))
            {
                return string.Empty;
            }

            var includeVariables = new Dictionary<string, object?>(variables, StringComparer.OrdinalIgnoreCase);
            var includeArgs = ParseNamedArguments(match.Groups[2].Value, variables);
            includeVariables["include"] = includeArgs;
            return Render(includeTemplate, includeVariables, includes);
        });
    }

    private string ProcessForLoops(string template, IReadOnlyDictionary<string, object?> variables, IReadOnlyDictionary<string, string>? includes)
    {
        return ForPattern().Replace(template, match =>
        {
            var itemName = match.Groups[1].Value.Trim();
            var collectionPath = match.Groups[2].Value.Trim();
            var body = match.Groups[3].Value;

            if (!TryResolveObject(variables, collectionPath, out var resolved))
            {
                return string.Empty;
            }

            var sequence = resolved switch
            {
                IEnumerable<JekyllContentItem> typedEnumerable => typedEnumerable.Cast<object?>(),
                IEnumerable<object?> enumerable => enumerable,
                _ => Array.Empty<object?>()
            };

            var parts = new List<string>();
            foreach (var item in sequence)
            {
                var scope = new Dictionary<string, object?>(variables, StringComparer.OrdinalIgnoreCase)
                {
                    [itemName] = item switch
                    {
                        JekyllContentItem contentItem => ToLiquidObject(contentItem),
                        _ => item
                    }
                };

                parts.Add(Render(body, scope, includes));
            }

            return string.Join(string.Empty, parts);
        });
    }

    private string ProcessIfBlocks(string template, IReadOnlyDictionary<string, object?> variables, IReadOnlyDictionary<string, string>? includes)
    {
        return IfPattern().Replace(template, match =>
        {
            var condition = match.Groups[1].Value.Trim();
            var trueContent = match.Groups[2].Value;
            var falseContent = match.Groups[4].Success ? match.Groups[4].Value : string.Empty;

            return EvaluateCondition(condition, variables)
                ? Render(trueContent, variables, includes)
                : Render(falseContent, variables, includes);
        });
    }

    private string ProcessAssignBlocks(string template, IReadOnlyDictionary<string, object?> variables, IReadOnlyDictionary<string, string>? includes)
    {
        var scoped = new Dictionary<string, object?>(variables, StringComparer.OrdinalIgnoreCase);

        var withoutAssign = AssignPattern().Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();
            var valueExpression = match.Groups[2].Value.Trim();
            scoped[key] = ResolveExpression(valueExpression, scoped);
            return string.Empty;
        });

        return ReplaceVariables(withoutAssign, scoped);
    }

    private string ReplaceVariables(string template, IReadOnlyDictionary<string, object?> variables)
    {
        return VariablePattern().Replace(template, match =>
        {
            var expression = match.Groups[1].Value.Trim();
            var value = ResolveExpression(expression, variables);
            return value?.ToString() ?? string.Empty;
        });
    }

    private static Dictionary<string, object?> ParseNamedArguments(string input, IReadOnlyDictionary<string, object?> variables)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input))
        {
            return result;
        }

        var matches = NamedArgumentPattern().Matches(input);
        foreach (Match match in matches)
        {
            result[match.Groups[1].Value.Trim()] = ResolveExpression(match.Groups[2].Value.Trim(), variables);
        }

        return result;
    }

    private static object? ResolveExpression(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        var parts = expression.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        object? current = ResolveBase(parts[0], variables);

        foreach (var filterPart in parts.Skip(1))
        {
            var filterTokens = filterPart.Split(':', 2, StringSplitOptions.TrimEntries);
            current = ApplyFilter(current, filterTokens[0], filterTokens.Length > 1 ? ResolveBase(filterTokens[1], variables)?.ToString() : null);
        }

        return current;
    }

    private static object? ResolveBase(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        if ((expression.StartsWith('"') && expression.EndsWith('"')) || (expression.StartsWith('\'') && expression.EndsWith('\'')))
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

        return TryResolveObject(variables, expression, out var value) ? value : expression;
    }

    private static object? ApplyFilter(object? value, string filterName, string? argument)
    {
        return filterName switch
        {
            "upcase" => value?.ToString()?.ToUpperInvariant(),
            "downcase" => value?.ToString()?.ToLowerInvariant(),
            "default" => string.IsNullOrWhiteSpace(value?.ToString()) ? argument : value,
            "date" => ApplyDateFilter(value, argument),
            "size" => ApplySizeFilter(value),
            "join" => ApplyJoinFilter(value, argument),
            "split" => ApplySplitFilter(value, argument),
            "strip" => value?.ToString()?.Trim(),
            "append" => (value?.ToString() ?? string.Empty) + (argument ?? string.Empty),
            "prepend" => (argument ?? string.Empty) + (value?.ToString() ?? string.Empty),
            "replace" => ApplyReplaceFilter(value, argument),
            "first" => ApplyFirstFilter(value),
            "last" => ApplyLastFilter(value),
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

    private static string TrimQuotes(string value)
    {
        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static bool EvaluateCondition(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        if (expression.Contains("==", StringComparison.Ordinal))
        {
            var parts = expression.Split("==", 2, StringSplitOptions.TrimEntries);
            return string.Equals(ResolveExpression(parts[0], variables)?.ToString(), ResolveExpression(parts[1], variables)?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        if (expression.Contains("!=", StringComparison.Ordinal))
        {
            var parts = expression.Split("!=", 2, StringSplitOptions.TrimEntries);
            return !string.Equals(ResolveExpression(parts[0], variables)?.ToString(), ResolveExpression(parts[1], variables)?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        var value = ResolveExpression(expression, variables);
        return value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            IEnumerable<object?> e => e.Any(),
            _ => true
        };
    }

    private static bool TryResolveObject(IReadOnlyDictionary<string, object?> variables, string path, out object? value)
    {
        object? current = variables;

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is IReadOnlyDictionary<string, object?> dict && dict.TryGetValue(segment, out var next))
            {
                current = next;
                continue;
            }

            if (current is Dictionary<string, object?> raw && raw.TryGetValue(segment, out next))
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

    [GeneratedRegex(@"\{\%\s*include\s+([^\s]+)(.*?)\%\}", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex IncludePattern();

    [GeneratedRegex(@"\{\%\s*for\s+(\w+)\s+in\s+(.+?)\s*\%\}(.*?)\{\%\s*endfor\s*\%\}", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex ForPattern();

    [GeneratedRegex(@"\{\%\s*if\s+(.+?)\s*\%\}(.*?)(\{\%\s*else\s*\%\}(.*?))?\{\%\s*endif\s*\%\}", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex IfPattern();

    [GeneratedRegex(@"\{\%\s*assign\s+(\w+)\s*=\s*(.+?)\s*\%\}", RegexOptions.Compiled)]
    private static partial Regex AssignPattern();

    [GeneratedRegex("(\\w+)\\s*=\\s*(\".*?\"|'.*?'|[^\\s]+)", RegexOptions.Compiled)]
    private static partial Regex NamedArgumentPattern();

    [GeneratedRegex(@"\{\{\s*(.*?)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex VariablePattern();
}
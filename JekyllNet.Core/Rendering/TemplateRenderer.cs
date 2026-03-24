using System.Globalization;
using System.Text.Json;
using JekyllNet.Core.Models;
using Markdig;

namespace JekyllNet.Core.Rendering;

public sealed partial class TemplateRenderer
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public string Render(string template, IReadOnlyDictionary<string, object?> variables, IReadOnlyDictionary<string, string>? includes = null)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var scope = new Dictionary<string, object?>(variables, StringComparer.OrdinalIgnoreCase);
        return RenderSegment(template, scope, includes);
    }

    private string RenderSegment(string template, Dictionary<string, object?> scope, IReadOnlyDictionary<string, string>? includes)
    {
        var output = new System.Text.StringBuilder();
        var index = 0;

        while (index < template.Length)
        {
            var variableStart = template.IndexOf("{{", index, StringComparison.Ordinal);
            var tagStart = template.IndexOf("{%", index, StringComparison.Ordinal);
            var nextStart = NextTokenStart(variableStart, tagStart);

            if (nextStart < 0)
            {
                output.Append(template[index..]);
                break;
            }

            output.Append(template[index..nextStart]);

            if (nextStart == variableStart)
            {
                var variableEnd = template.IndexOf("}}", variableStart + 2, StringComparison.Ordinal);
                if (variableEnd < 0)
                {
                    output.Append(template[variableStart..]);
                    break;
                }

                var expression = template[(variableStart + 2)..variableEnd].Trim();
                output.Append(ResolveExpression(expression, scope)?.ToString() ?? string.Empty);
                index = variableEnd + 2;
                continue;
            }

            var tagEnd = template.IndexOf("%}", tagStart + 2, StringComparison.Ordinal);
            if (tagEnd < 0)
            {
                output.Append(template[tagStart..]);
                break;
            }

            var tagContent = template[(tagStart + 2)..tagEnd].Trim();
            var tagName = GetTagName(tagContent);
            index = tagEnd + 2;

            switch (tagName)
            {
                case "assign":
                    ExecuteAssign(tagContent, scope);
                    break;

                case "include":
                    output.Append(RenderInclude(tagContent, scope, includes));
                    break;

                case "if":
                    output.Append(RenderIfBlock(template, tagContent, scope, includes, ref index));
                    break;

                case "unless":
                    output.Append(RenderUnlessBlock(template, tagContent, scope, includes, ref index));
                    break;

                case "capture":
                    ExecuteCaptureBlock(template, tagContent, scope, includes, ref index);
                    break;

                case "case":
                    output.Append(RenderCaseBlock(template, tagContent, scope, includes, ref index));
                    break;

                case "for":
                    output.Append(RenderForBlock(template, tagContent, scope, includes, ref index));
                    break;
            }
        }

        return output.ToString();
    }

    private string RenderInclude(string tagContent, Dictionary<string, object?> scope, IReadOnlyDictionary<string, string>? includes)
    {
        if (includes is null || includes.Count == 0)
        {
            return string.Empty;
        }

        var includeExpression = tagContent["include".Length..].Trim();
        if (string.IsNullOrWhiteSpace(includeExpression))
        {
            return string.Empty;
        }

        var parts = includeExpression.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var includeName = parts[0].Trim('"', '\'');
        if (!includes.TryGetValue(includeName, out var includeTemplate))
        {
            return string.Empty;
        }

        var includeScope = new Dictionary<string, object?>(scope, StringComparer.OrdinalIgnoreCase)
        {
            ["include"] = parts.Length > 1
                ? ParseNamedArguments(parts[1], scope)
                : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        };

        return RenderSegment(includeTemplate, includeScope, includes);
    }

    private string RenderIfBlock(
        string template,
        string tagContent,
        Dictionary<string, object?> scope,
        IReadOnlyDictionary<string, string>? includes,
        ref int index)
    {
        var condition = tagContent["if".Length..].Trim();
        var bodyStart = index;
        index = ExtractConditionalBranches(template, bodyStart, "if", "endif", out var trueBranch, out var falseBranch);

        return EvaluateCondition(condition, scope)
            ? RenderSegment(trueBranch, scope, includes)
            : RenderSegment(falseBranch, scope, includes);
    }

    private string RenderUnlessBlock(
        string template,
        string tagContent,
        Dictionary<string, object?> scope,
        IReadOnlyDictionary<string, string>? includes,
        ref int index)
    {
        var condition = tagContent["unless".Length..].Trim();
        var bodyStart = index;
        index = ExtractConditionalBranches(template, bodyStart, "unless", "endunless", out var trueBranch, out var falseBranch);

        return !EvaluateCondition(condition, scope)
            ? RenderSegment(trueBranch, scope, includes)
            : RenderSegment(falseBranch, scope, includes);
    }

    private string RenderForBlock(
        string template,
        string tagContent,
        Dictionary<string, object?> scope,
        IReadOnlyDictionary<string, string>? includes,
        ref int index)
    {
        var expression = tagContent["for".Length..].Trim();
        var tokens = expression.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3 || !string.Equals(tokens[1], "in", StringComparison.OrdinalIgnoreCase))
        {
            index = ExtractForBody(template, index, out _);
            return string.Empty;
        }

        var itemName = tokens[0];
        var collectionPath = string.Join(' ', tokens.Skip(2));
        index = ExtractForBody(template, index, out var body);

        if (!TryResolveObject(scope, collectionPath, out var resolved))
        {
            return string.Empty;
        }

        var sequence = resolved switch
        {
            IEnumerable<JekyllContentItem> typedEnumerable => typedEnumerable.Cast<object?>(),
            IEnumerable<object?> enumerable => enumerable,
            _ => Array.Empty<object?>()
        };

        var output = new System.Text.StringBuilder();
        foreach (var item in sequence)
        {
            var iterationScope = new Dictionary<string, object?>(scope, StringComparer.OrdinalIgnoreCase)
            {
                [itemName] = item switch
                {
                    JekyllContentItem contentItem => ToLiquidObject(contentItem),
                    _ => item
                }
            };

            output.Append(RenderSegment(body, iterationScope, includes));
        }

        return output.ToString();
    }

    private void ExecuteCaptureBlock(
        string template,
        string tagContent,
        Dictionary<string, object?> scope,
        IReadOnlyDictionary<string, string>? includes,
        ref int index)
    {
        var variableName = tagContent["capture".Length..].Trim();
        index = ExtractBlockBody(template, index, "capture", "endcapture", out var body);
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return;
        }

        scope[variableName] = RenderSegment(body, scope, includes);
    }

    private string RenderCaseBlock(
        string template,
        string tagContent,
        Dictionary<string, object?> scope,
        IReadOnlyDictionary<string, string>? includes,
        ref int index)
    {
        var targetExpression = tagContent["case".Length..].Trim();
        var targetValue = ResolveExpression(targetExpression, scope)?.ToString();
        index = ExtractCaseBranches(template, index, out var branches, out var elseBranch);

        foreach (var branch in branches)
        {
            if (BranchMatches(targetValue, branch.WhenValues, scope))
            {
                return RenderSegment(branch.Body, scope, includes);
            }
        }

        return RenderSegment(elseBranch, scope, includes);
    }

    private static int ExtractForBody(string template, int startIndex, out string body)
    {
        var depth = 0;
        var cursor = startIndex;

        while (TryFindTag(template, cursor, out var tagStart, out var tagEnd, out var tagContent))
        {
            var tagName = GetTagName(tagContent);
            if (string.Equals(tagName, "for", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
            }
            else if (string.Equals(tagName, "endfor", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                {
                    body = template[startIndex..tagStart];
                    return tagEnd + 2;
                }

                depth--;
            }

            cursor = tagEnd + 2;
        }

        body = template[startIndex..];
        return template.Length;
    }

    private static int ExtractBlockBody(string template, int startIndex, string openingTagName, string closingTagName, out string body)
    {
        var depth = 0;
        var cursor = startIndex;

        while (TryFindTag(template, cursor, out var tagStart, out var tagEnd, out var tagContent))
        {
            var tagName = GetTagName(tagContent);
            if (string.Equals(tagName, openingTagName, StringComparison.OrdinalIgnoreCase))
            {
                depth++;
            }
            else if (string.Equals(tagName, closingTagName, StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                {
                    body = template[startIndex..tagStart];
                    return tagEnd + 2;
                }

                depth--;
            }

            cursor = tagEnd + 2;
        }

        body = template[startIndex..];
        return template.Length;
    }

    private static int ExtractConditionalBranches(
        string template,
        int startIndex,
        string openingTagName,
        string closingTagName,
        out string trueBranch,
        out string falseBranch)
    {
        var depth = 0;
        var cursor = startIndex;
        var elseContentStart = -1;
        var elseTagStart = -1;

        while (TryFindTag(template, cursor, out var tagStart, out var tagEnd, out var tagContent))
        {
            var tagName = GetTagName(tagContent);
            if (string.Equals(tagName, openingTagName, StringComparison.OrdinalIgnoreCase))
            {
                depth++;
            }
            else if (string.Equals(tagName, closingTagName, StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                {
                    trueBranch = elseTagStart >= 0
                        ? template[startIndex..elseTagStart]
                        : template[startIndex..tagStart];
                    falseBranch = elseContentStart >= 0
                        ? template[elseContentStart..tagStart]
                        : string.Empty;
                    return tagEnd + 2;
                }

                depth--;
            }
            else if (string.Equals(tagName, "else", StringComparison.OrdinalIgnoreCase) && depth == 0 && elseTagStart < 0)
            {
                elseTagStart = tagStart;
                elseContentStart = tagEnd + 2;
            }

            cursor = tagEnd + 2;
        }

        trueBranch = template[startIndex..];
        falseBranch = string.Empty;
        return template.Length;
    }

    private static int ExtractCaseBranches(
        string template,
        int startIndex,
        out List<CaseBranch> branches,
        out string elseBranch)
    {
        branches = [];
        elseBranch = string.Empty;

        var depth = 0;
        var cursor = startIndex;
        var activeWhenExpression = string.Empty;
        var activeContentStart = startIndex;
        var elseStart = -1;

        while (TryFindTag(template, cursor, out var tagStart, out var tagEnd, out var tagContent))
        {
            var tagName = GetTagName(tagContent);
            if (string.Equals(tagName, "case", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
            }
            else if (string.Equals(tagName, "endcase", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                {
                    if (!string.IsNullOrWhiteSpace(activeWhenExpression))
                    {
                        branches.Add(new CaseBranch(activeWhenExpression, template[activeContentStart..tagStart]));
                    }
                    else if (elseStart >= 0)
                    {
                        elseBranch = template[elseStart..tagStart];
                    }

                    return tagEnd + 2;
                }

                depth--;
            }
            else if (depth == 0 && string.Equals(tagName, "when", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(activeWhenExpression))
                {
                    branches.Add(new CaseBranch(activeWhenExpression, template[activeContentStart..tagStart]));
                }
                else if (elseStart >= 0)
                {
                    elseBranch = template[elseStart..tagStart];
                }

                activeWhenExpression = tagContent["when".Length..].Trim();
                activeContentStart = tagEnd + 2;
                elseStart = -1;
            }
            else if (depth == 0 && string.Equals(tagName, "else", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(activeWhenExpression))
                {
                    branches.Add(new CaseBranch(activeWhenExpression, template[activeContentStart..tagStart]));
                    activeWhenExpression = string.Empty;
                }

                elseStart = tagEnd + 2;
            }

            cursor = tagEnd + 2;
        }

        if (!string.IsNullOrWhiteSpace(activeWhenExpression))
        {
            branches.Add(new CaseBranch(activeWhenExpression, template[activeContentStart..]));
        }
        else if (elseStart >= 0)
        {
            elseBranch = template[elseStart..];
        }

        return template.Length;
    }

    private static void ExecuteAssign(string tagContent, Dictionary<string, object?> scope)
    {
        var expression = tagContent["assign".Length..].Trim();
        var separator = expression.IndexOf('=');
        if (separator < 0)
        {
            return;
        }

        var key = expression[..separator].Trim();
        var valueExpression = expression[(separator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        scope[key] = ResolveExpression(valueExpression, scope);
    }

    private static Dictionary<string, object?> ParseNamedArguments(string input, IReadOnlyDictionary<string, object?> variables)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(input))
        {
            return result;
        }

        var matches = NamedArgumentPattern().Matches(input);
        foreach (System.Text.RegularExpressions.Match match in matches)
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

        return TryResolveObject(variables, expression, out var value) ? value : null;
    }

    private static object? ApplyFilter(object? value, string filterName, string? argument, IReadOnlyDictionary<string, object?> variables)
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
            "append" => (value?.ToString() ?? string.Empty) + (ResolveSingleArgument(argument, variables)?.ToString() ?? string.Empty),
            "prepend" => (ResolveSingleArgument(argument, variables)?.ToString() ?? string.Empty) + (value?.ToString() ?? string.Empty),
            "replace" => ApplyReplaceFilter(value, argument),
            "replace_first" => ApplyReplaceFirstFilter(value, argument),
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

        if (Uri.TryCreate(path, UriKind.Absolute, out _))
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

        if (Uri.TryCreate(path, UriKind.Absolute, out _))
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
        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static bool EvaluateCondition(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        if (expression.Contains(" contains ", StringComparison.Ordinal))
        {
            var parts = expression.Split(" contains ", 2, StringSplitOptions.TrimEntries);
            var left = ResolveExpression(parts[0], variables);
            var right = ResolveExpression(parts[1], variables)?.ToString() ?? string.Empty;

            return left switch
            {
                string text => text.Contains(right, StringComparison.Ordinal),
                IEnumerable<object?> sequence => sequence.Any(item => string.Equals(item?.ToString(), right, StringComparison.Ordinal)),
                _ => false
            };
        }

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
            string s when bool.TryParse(s, out var boolString) => boolString,
            string s => !string.IsNullOrWhiteSpace(s),
            IEnumerable<object?> e => e.Any(),
            _ => true
        };
    }

    private static bool BranchMatches(string? targetValue, string whenExpression, IReadOnlyDictionary<string, object?> variables)
    {
        foreach (var token in SplitWhenValues(whenExpression))
        {
            var candidate = ResolveExpression(token, variables)?.ToString();
            if (string.Equals(targetValue, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitWhenValues(string expression)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;

        foreach (var ch in expression)
        {
            if (quote is null && ch is '\'' or '"')
            {
                quote = ch;
                current.Append(ch);
                continue;
            }

            if (quote is not null)
            {
                current.Append(ch);
                if (ch == quote)
                {
                    quote = null;
                }

                continue;
            }

            if (ch == ',' || ch == ' ' || ch == '\t')
            {
                if (current.Length > 0)
                {
                    values.Add(current.ToString().Trim());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            values.Add(current.ToString().Trim());
        }

        return values.Where(static value => !string.IsNullOrWhiteSpace(value));
    }

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
            switch (current)
            {
                case IReadOnlyDictionary<string, object?> readOnlyDictionary when readOnlyDictionary.TryGetValue(segment, out var readOnlyNext):
                    current = readOnlyNext;
                    break;

                case Dictionary<string, object?> dictionary when dictionary.TryGetValue(segment, out var dictionaryNext):
                    current = dictionaryNext;
                    break;

                default:
                    return null;
            }
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

    private static bool TryFindTag(string template, int startIndex, out int tagStart, out int tagEnd, out string tagContent)
    {
        tagStart = template.IndexOf("{%", startIndex, StringComparison.Ordinal);
        if (tagStart < 0)
        {
            tagEnd = -1;
            tagContent = string.Empty;
            return false;
        }

        tagEnd = template.IndexOf("%}", tagStart + 2, StringComparison.Ordinal);
        if (tagEnd < 0)
        {
            tagContent = string.Empty;
            return false;
        }

        tagContent = template[(tagStart + 2)..tagEnd].Trim();
        return true;
    }

    private static string GetTagName(string tagContent)
    {
        var firstSpace = tagContent.IndexOf(' ');
        return firstSpace >= 0 ? tagContent[..firstSpace] : tagContent;
    }

    private static int NextTokenStart(int variableStart, int tagStart)
    {
        if (variableStart < 0)
        {
            return tagStart;
        }

        if (tagStart < 0)
        {
            return variableStart;
        }

        return Math.Min(variableStart, tagStart);
    }

    [System.Text.RegularExpressions.GeneratedRegex("(\\w+)\\s*=\\s*(\".*?\"|'.*?'|[^\\s]+)", System.Text.RegularExpressions.RegexOptions.Compiled)]
    private static partial System.Text.RegularExpressions.Regex NamedArgumentPattern();

    private sealed record CaseBranch(string WhenValues, string Body);

    private sealed class LiquidValueComparer : IComparer<object?>
    {
        public static LiquidValueComparer Instance { get; } = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return 1;
            }

            if (y is null)
            {
                return -1;
            }

            if (TryConvertToDecimal(x, out var leftNumber) && TryConvertToDecimal(y, out var rightNumber))
            {
                return leftNumber.CompareTo(rightNumber);
            }

            if (x is DateTimeOffset leftDate && y is DateTimeOffset rightDate)
            {
                return leftDate.CompareTo(rightDate);
            }

            return string.Compare(x.ToString(), y.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryConvertToDecimal(object value, out decimal result)
        {
            return value switch
            {
                byte byteValue => (result = byteValue) >= 0,
                sbyte sbyteValue => (result = sbyteValue) >= sbyte.MinValue,
                short shortValue => (result = shortValue) >= short.MinValue,
                ushort ushortValue => (result = ushortValue) >= ushort.MinValue,
                int intValue => (result = intValue) >= int.MinValue,
                uint uintValue => (result = uintValue) >= uint.MinValue,
                long longValue => (result = longValue) >= long.MinValue,
                ulong ulongValue => (result = ulongValue) >= ulong.MinValue,
                float floatValue => decimal.TryParse(floatValue.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, out result),
                double doubleValue => decimal.TryParse(doubleValue.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, out result),
                decimal decimalValue => (result = decimalValue) >= decimal.MinValue,
                string text => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out result),
                _ => decimal.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out result)
            };
        }
    }
}

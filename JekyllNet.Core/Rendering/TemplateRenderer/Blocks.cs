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
        if (!TryParseForExpression(expression, out var itemName, out var collectionPath, out var parameters))
        {
            index = ExtractForBody(template, index, out _);
            return string.Empty;
        }

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
        var items = sequence.ToList();

        if (parameters.Reversed)
        {
            items.Reverse();
        }

        if (parameters.Offset > 0)
        {
            items = items.Skip(parameters.Offset).ToList();
        }

        if (parameters.Limit is > 0)
        {
            items = items.Take(parameters.Limit.Value).ToList();
        }

        var output = new System.Text.StringBuilder();
        var savedItemVariable = SaveScopedValue(scope, itemName);
        var savedForloopVariable = SaveScopedValue(scope, "forloop");
        try
        {
            for (var index0 = 0; index0 < items.Count; index0++)
            {
                var item = items[index0];
                scope[itemName] = item switch
                {
                    JekyllContentItem contentItem => ToLiquidObject(contentItem),
                    _ => item
                };
                scope["forloop"] = BuildForloopObject(index0, items.Count);

                output.Append(RenderSegment(body, scope, includes));
            }
        }
        finally
        {
            RestoreScopedValue(scope, itemName, savedItemVariable);
            RestoreScopedValue(scope, "forloop", savedForloopVariable);
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
                    return AdvancePastTag(template, tagEnd);
                }

                depth--;
            }

            cursor = AdvancePastTag(template, tagEnd);
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
                    return AdvancePastTag(template, tagEnd);
                }

                depth--;
            }

            cursor = AdvancePastTag(template, tagEnd);
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
            if (string.Equals(tagName, "if", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tagName, "unless", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
            }
            else if (string.Equals(tagName, "endif", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tagName, "endunless", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0 && string.Equals(tagName, closingTagName, StringComparison.OrdinalIgnoreCase))
                {
                    trueBranch = elseTagStart >= 0
                        ? template[startIndex..elseTagStart]
                        : template[startIndex..tagStart];
                    falseBranch = elseContentStart >= 0
                        ? template[elseContentStart..tagStart]
                        : string.Empty;
                    return AdvancePastTag(template, tagEnd);
                }

                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (string.Equals(tagName, "else", StringComparison.OrdinalIgnoreCase) && depth == 0 && elseTagStart < 0)
            {
                elseTagStart = tagStart;
                elseContentStart = AdvancePastTag(template, tagEnd);
            }

            cursor = AdvancePastTag(template, tagEnd);
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

                    return AdvancePastTag(template, tagEnd);
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
                activeContentStart = AdvancePastTag(template, tagEnd);
                elseStart = -1;
            }
            else if (depth == 0 && string.Equals(tagName, "else", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(activeWhenExpression))
                {
                    branches.Add(new CaseBranch(activeWhenExpression, template[activeContentStart..tagStart]));
                    activeWhenExpression = string.Empty;
                }

                elseStart = AdvancePastTag(template, tagEnd);
            }

            cursor = AdvancePastTag(template, tagEnd);
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

    private void ExecuteAssign(string tagContent, Dictionary<string, object?> scope)
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

    private static bool TryParseForExpression(
        string expression,
        out string itemName,
        out string collectionPath,
        out ForLoopParameters parameters)
    {
        itemName = string.Empty;
        collectionPath = string.Empty;
        parameters = new ForLoopParameters(null, 0, false);

        var tokens = expression.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3 || !string.Equals(tokens[1], "in", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        itemName = tokens[0];
        var collectionTokens = new List<string>();
        string? limitToken = null;
        var offset = 0;
        var reversed = false;

        for (var tokenIndex = 2; tokenIndex < tokens.Length; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            if (token.StartsWith("limit:", StringComparison.OrdinalIgnoreCase))
            {
                limitToken = token["limit:".Length..];
                continue;
            }

            if (token.StartsWith("offset:", StringComparison.OrdinalIgnoreCase))
            {
                var offsetToken = token["offset:".Length..];
                if (string.Equals(offsetToken, "continue", StringComparison.OrdinalIgnoreCase))
                {
                    offset = 0;
                    continue;
                }

                _ = int.TryParse(offsetToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
                continue;
            }

            if (string.Equals(token, "reversed", StringComparison.OrdinalIgnoreCase))
            {
                reversed = true;
                continue;
            }

            collectionTokens.Add(token);
        }

        if (collectionTokens.Count == 0)
        {
            return false;
        }

        int? limit = null;
        if (!string.IsNullOrWhiteSpace(limitToken)
            && int.TryParse(limitToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit)
            && parsedLimit >= 0)
        {
            limit = parsedLimit;
        }

        collectionPath = string.Join(' ', collectionTokens);
        parameters = new ForLoopParameters(limit, Math.Max(offset, 0), reversed);
        return !string.IsNullOrWhiteSpace(itemName) && !string.IsNullOrWhiteSpace(collectionPath);
    }

    private static Dictionary<string, object?> BuildForloopObject(int index0, int length)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "loop",
            ["index"] = index0 + 1,
            ["index0"] = index0,
            ["rindex"] = length - index0,
            ["rindex0"] = length - index0 - 1,
            ["first"] = index0 == 0,
            ["last"] = index0 == length - 1,
            ["length"] = length
        };

    private static ScopedValueSnapshot SaveScopedValue(Dictionary<string, object?> scope, string key)
        => scope.TryGetValue(key, out var existingValue)
            ? new ScopedValueSnapshot(true, existingValue)
            : new ScopedValueSnapshot(false, null);

    private static void RestoreScopedValue(Dictionary<string, object?> scope, string key, ScopedValueSnapshot snapshot)
    {
        if (snapshot.Exists)
        {
            scope[key] = snapshot.Value;
            return;
        }

        scope.Remove(key);
    }

    private Dictionary<string, object?> ParseNamedArguments(string input, IReadOnlyDictionary<string, object?> variables)
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

}

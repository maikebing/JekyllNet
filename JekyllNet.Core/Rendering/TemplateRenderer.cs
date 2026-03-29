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
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>
    /// Optional plugin registry consulted for custom Liquid tags, blocks, and filters.
    /// Set this before calling <see cref="Render"/> to enable plugin support.
    /// </summary>
    public JekyllPluginRegistry? PluginRegistry { get; set; }

    /// <summary>
    /// The absolute path to the site source directory, forwarded to plugins that
    /// need file-system access (e.g. the <c>file_exists</c> tag).
    /// </summary>
    public string SourceDirectory { get; set; } = string.Empty;

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

            var isVariable = nextStart == variableStart;
            var trimLeft = nextStart + 2 < template.Length && template[nextStart + 2] == '-';
            var leadingText = template[index..nextStart];
            output.Append(trimLeft ? TrimTrailingWhitespace(leadingText) : leadingText);

            if (isVariable)
            {
                var variableEnd = template.IndexOf("}}", variableStart + 2, StringComparison.Ordinal);
                if (variableEnd < 0)
                {
                    output.Append(template[variableStart..]);
                    break;
                }

                var trimVariableRight = variableEnd > variableStart + 2 && template[variableEnd - 1] == '-';
                var expressionStart = variableStart + (trimLeft ? 3 : 2);
                var expressionEnd = trimVariableRight ? variableEnd - 1 : variableEnd;
                var expression = NormalizeLiquidMarkup(template[expressionStart..expressionEnd]);
                output.Append(ResolveExpression(expression, scope)?.ToString() ?? string.Empty);
                index = trimVariableRight ? SkipLeadingWhitespace(template, variableEnd + 2) : variableEnd + 2;
                continue;
            }

            var tagEnd = template.IndexOf("%}", tagStart + 2, StringComparison.Ordinal);
            if (tagEnd < 0)
            {
                output.Append(template[tagStart..]);
                break;
            }

            var trimTagRight = tagEnd > tagStart + 2 && template[tagEnd - 1] == '-';
            var tagContentStart = tagStart + (trimLeft ? 3 : 2);
            var tagContentEnd = trimTagRight ? tagEnd - 1 : tagEnd;
            var tagContent = NormalizeLiquidMarkup(template[tagContentStart..tagContentEnd]);
            var tagName = GetTagName(tagContent);
            index = trimTagRight ? SkipLeadingWhitespace(template, tagEnd + 2) : tagEnd + 2;

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

                case "comment":
                    index = ExtractBlockBody(template, index, "comment", "endcomment", out _);
                    break;

                case "case":
                    output.Append(RenderCaseBlock(template, tagContent, scope, includes, ref index));
                    break;

                case "for":
                    output.Append(RenderForBlock(template, tagContent, scope, includes, ref index));
                    break;

                default:
                    output.Append(RenderCustomTag(template, tagName, tagContent, scope, includes, ref index));
                    break;
            }
        }

        return output.ToString();
    }

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

    private bool EvaluateCondition(string expression, IReadOnlyDictionary<string, object?> variables)
    {
        var orSegments = SplitConditionByOperator(expression, "or");
        if (orSegments.Count > 1)
        {
            return orSegments.Any(segment => EvaluateCondition(segment, variables));
        }

        var andSegments = SplitConditionByOperator(expression, "and");
        if (andSegments.Count > 1)
        {
            return andSegments.All(segment => EvaluateCondition(segment, variables));
        }

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

    private static List<string> SplitConditionByOperator(string expression, string operatorToken)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(expression))
        {
            return parts;
        }

        var current = new System.Text.StringBuilder();
        char? quote = null;
        var index = 0;
        var marker = $" {operatorToken} ";

        while (index < expression.Length)
        {
            var ch = expression[index];
            if (quote is null && ch is '\'' or '"')
            {
                quote = ch;
                current.Append(ch);
                index++;
                continue;
            }

            if (quote is not null)
            {
                current.Append(ch);
                if (ch == quote)
                {
                    quote = null;
                }

                index++;
                continue;
            }

            if (index + marker.Length <= expression.Length
                && string.Equals(expression.Substring(index, marker.Length), marker, StringComparison.OrdinalIgnoreCase))
            {
                var segment = current.ToString().Trim();
                if (segment.Length > 0)
                {
                    parts.Add(segment);
                }

                current.Clear();
                index += marker.Length;
                continue;
            }

            current.Append(ch);
            index++;
        }

        var tail = current.ToString().Trim();
        if (tail.Length > 0)
        {
            parts.Add(tail);
        }

        return parts;
    }

    private bool BranchMatches(string? targetValue, string whenExpression, IReadOnlyDictionary<string, object?> variables)
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

        tagContent = NormalizeLiquidMarkup(template[(tagStart + 2)..tagEnd]);
        return true;
    }

    private static string GetTagName(string tagContent)
    {
        var firstSpace = tagContent.IndexOf(' ');
        return firstSpace >= 0 ? tagContent[..firstSpace] : tagContent;
    }

    private static string NormalizeLiquidMarkup(string markup)
    {
        var normalized = markup.Trim();

        if (normalized.Length > 0 && normalized[0] == '-')
        {
            normalized = normalized[1..].TrimStart();
        }

        if (normalized.Length > 0 && normalized[^1] == '-')
        {
            normalized = normalized[..^1].TrimEnd();
        }

        return normalized;
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

    private static int SkipLeadingWhitespace(string template, int index)
    {
        while (index < template.Length && char.IsWhiteSpace(template[index]))
        {
            index++;
        }

        return index;
    }

    private static int AdvancePastTag(string template, int tagEnd)
    {
        var index = tagEnd + 2;
        return tagEnd > 0 && template[tagEnd - 1] == '-'
            ? SkipLeadingWhitespace(template, index)
            : index;
    }

    private static string TrimTrailingWhitespace(string text)
    {
        var end = text.Length;
        while (end > 0 && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return end == text.Length ? text : text[..end];
    }

    [System.Text.RegularExpressions.GeneratedRegex("(\\w+)\\s*=\\s*(\".*?\"|'.*?'|[^\\s]+)", System.Text.RegularExpressions.RegexOptions.Compiled)]
    private static partial System.Text.RegularExpressions.Regex NamedArgumentPattern();

    private sealed record CaseBranch(string WhenValues, string Body);

    private sealed record ForLoopParameters(int? Limit, int Offset, bool Reversed);

    private sealed record ScopedValueSnapshot(bool Exists, object? Value);

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

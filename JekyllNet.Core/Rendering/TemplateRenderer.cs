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

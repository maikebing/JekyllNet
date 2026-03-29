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

}

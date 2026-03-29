using System.Text;
using System.Text.RegularExpressions;

namespace JekyllNet.Core.Plugins.Loading;

/// <summary>
/// Performs a best-effort structural translation from a Jekyll Ruby plugin to
/// valid C# source implementing the corresponding <see cref="IJekyllPlugin"/> interface.
/// Simple patterns are translated directly; complex logic becomes stubs.
/// </summary>
internal static partial class RubyPluginTranspiler
{
    // ── Detection patterns ───────────────────────────────────────────────────

    [GeneratedRegex(@"class\s+\w+\s*<\s*Liquid::Block", RegexOptions.IgnoreCase)]
    private static partial Regex IsLiquidBlockPattern();

    [GeneratedRegex(@"class\s+\w+\s*<\s*Liquid::Tag", RegexOptions.IgnoreCase)]
    private static partial Regex IsLiquidTagPattern();

    [GeneratedRegex(@"class\s+\w+\s*<\s*Jekyll::Generator", RegexOptions.IgnoreCase)]
    private static partial Regex IsJekyllGeneratorPattern();

    [GeneratedRegex(@"Liquid::Template\.register_filter\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex IsLiquidFilterPattern();

    [GeneratedRegex(@"Liquid::Template\.register_tag\s*\(\s*'(\w+)'\s*,\s*[\w:]+\)", RegexOptions.IgnoreCase)]
    private static partial Regex RegisterTagPattern();

    [GeneratedRegex(@"def\s+(\w+)\s*\((?:input|string)[\w\s,]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex FilterMethodPattern();

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Translates <paramref name="rubySource"/> into compilable C# source, or
    /// returns <see langword="null"/> if the file is not a recognised extension point.
    /// </summary>
    public static string? Transpile(string rubySource, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var className = ToClassName(baseName);

        if (IsLiquidBlockPattern().IsMatch(rubySource))
            return TranspileBlock(rubySource, fileName, className);

        if (IsLiquidTagPattern().IsMatch(rubySource))
            return TranspileTag(rubySource, fileName, className);

        if (IsJekyllGeneratorPattern().IsMatch(rubySource))
            return TranspileGenerator(fileName, className);

        if (IsLiquidFilterPattern().IsMatch(rubySource))
            return TranspileFilter(rubySource, fileName, className);

        return null;
    }

    // ── Block ────────────────────────────────────────────────────────────────

    private static string TranspileBlock(string rubySource, string fileName, string className)
    {
        var tagName = ExtractTagName(rubySource) ?? ToSnakeCase(className);
        var isDetailsLike = rubySource.Contains("<details>", StringComparison.OrdinalIgnoreCase)
                            || rubySource.Contains("details", StringComparison.OrdinalIgnoreCase);
        string memberBody;
        if (isDetailsLike)
        {
            memberBody = string.Join("\n",
                "    public string TagName => \"" + tagName + "\";",
                "",
                "    public string Render(string markup, string body, JekyllPluginContext context)",
                "    {",
                "        // Translated from Ruby: wraps content in HTML <details>/<summary>.",
                "        var caption = markup.Trim(' ', '\"', '\\'');",
                "        return \"<details><summary>\" + caption + \"</summary>\" + body + \"</details>\";",
                "    }");
        }
        else
        {
            memberBody = string.Join("\n",
                "    public string TagName => \"" + tagName + "\";",
                "",
                "    public string Render(string markup, string body, JekyllPluginContext context)",
                "    {",
                "        // Auto-transpiled from Ruby. Requires manual implementation.",
                "        // Original file: " + Path.GetFileName(fileName),
                "        throw new NotImplementedException(",
                "            \"Plugin block '\" + TagName + \"' was transpiled from Ruby but requires manual C# implementation.\");",
                "    }");
        }

        return GenerateClassSource(fileName, className + "Block", "ILiquidBlock", memberBody);
    }

    // ── Tag ──────────────────────────────────────────────────────────────────

    private static string TranspileTag(string rubySource, string fileName, string className)
    {
        var tagName = ExtractTagName(rubySource) ?? ToSnakeCase(className);
        var isFileExists = rubySource.Contains("File.exist?", StringComparison.Ordinal)
                           || rubySource.Contains("file_exists", StringComparison.OrdinalIgnoreCase);
        string memberBody;
        if (isFileExists)
        {
            memberBody = string.Join("\n",
                "    public string TagName => \"" + tagName + "\";",
                "",
                "    public string Render(string markup, JekyllPluginContext context)",
                "    {",
                "        // Translated from Ruby: resolves a path and checks whether it exists.",
                "        var path = markup.Trim(' ', '\"', '\\'');",
                "        if (context.Variables.TryGetValue(path, out var resolved))",
                "            path = resolved?.ToString() ?? path;",
                "        var fullPath = System.IO.Path.Combine(context.SourceDirectory, path.TrimStart('/'));",
                "        return System.IO.File.Exists(fullPath) ? \"true\" : \"false\";",
                "    }");
        }
        else
        {
            memberBody = string.Join("\n",
                "    public string TagName => \"" + tagName + "\";",
                "",
                "    public string Render(string markup, JekyllPluginContext context)",
                "    {",
                "        // Auto-transpiled from Ruby. Requires manual implementation.",
                "        // Original file: " + Path.GetFileName(fileName),
                "        // Typical pattern: HTTP request -> parse -> return formatted string.",
                "        return string.Empty;",
                "    }");
        }

        return GenerateClassSource(fileName, className + "Tag", "ILiquidTag", memberBody);
    }

    // ── Generator ────────────────────────────────────────────────────────────

    private static string TranspileGenerator(string fileName, string className)
    {
        var memberBody = string.Join("\n",
            "    public System.Threading.Tasks.Task GenerateAsync(",
            "        JekyllNet.Core.Models.JekyllSiteContext context,",
            "        System.Threading.CancellationToken cancellationToken = default)",
            "    {",
            "        // Auto-transpiled from Ruby. Requires manual implementation.",
            "        // Original file: " + Path.GetFileName(fileName),
            "        // Typical pattern: fetch external data, populate context.ExtraItems.",
            "        return System.Threading.Tasks.Task.CompletedTask;",
            "    }");

        return GenerateClassSource(fileName, className + "Generator", "IJekyllGenerator", memberBody);
    }

    // ── Filter ───────────────────────────────────────────────────────────────

    private static string TranspileFilter(string rubySource, string fileName, string className)
    {
        var filterMethods = FilterMethodPattern()
            .Matches(rubySource)
            .Select(m => m.Groups[1].Value)
            .Where(n => !string.Equals(n, "initialize", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (filterMethods.Count == 0)
        {
            var emptyBody = string.Join("\n",
                "    public System.Collections.Generic.IReadOnlyList<string> FilterNames => [];",
                "",
                "    public object? Apply(string filterName, object? input, string? argument, JekyllPluginContext context)",
                "    {",
                "        // Auto-transpiled from Ruby. No filter methods detected.",
                "        // Original file: " + Path.GetFileName(fileName),
                "        return input;",
                "    }");
            return GenerateClassSource(fileName, className + "Filter", "ILiquidFilter", emptyBody);
        }

        var filterNameList = string.Join(", ", filterMethods.Select(n => "\"" + n + "\""));
        var sb = new StringBuilder();
        sb.AppendLine("    public System.Collections.Generic.IReadOnlyList<string> FilterNames =>");
        sb.AppendLine("        [" + filterNameList + "];");
        sb.AppendLine();
        sb.AppendLine("    public object? Apply(string filterName, object? input, string? argument, JekyllPluginContext context)");
        sb.AppendLine("    {");
        sb.AppendLine("        return filterName switch");
        sb.AppendLine("        {");

        foreach (var method in filterMethods)
        {
            var impl = GetFilterImpl(method);
            sb.AppendLine("            \"" + method + "\" => " + impl + ",");
        }

        sb.AppendLine("            _ => input");
        sb.AppendLine("        };");
        sb.AppendLine("    }");

        var helperMethods = BuildFilterHelpers(filterMethods);

        var memberBody = sb.ToString().TrimEnd() + "\n" + helperMethods;
        return GenerateClassSource(fileName, className + "Filter", "ILiquidFilter", memberBody);
    }

    private static string GetFilterImpl(string methodName)
    {
        if (string.Equals(methodName, "remove_accents", StringComparison.OrdinalIgnoreCase))
            return "(object?)RemoveAccents(input?.ToString())";

        if (string.Equals(methodName, "hideCustomBibtex", StringComparison.OrdinalIgnoreCase)
            || string.Equals(methodName, "hide_custom_bibtex", StringComparison.OrdinalIgnoreCase))
            return "(object?)HideCustomBibtex(input?.ToString(), context)";

        return "input /* TODO: translate Ruby '" + methodName + "' filter */";
    }

    private static string BuildFilterHelpers(List<string> filterMethods)
    {
        var sb = new StringBuilder();

        if (filterMethods.Any(m => m.Equals("remove_accents", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine();
            sb.AppendLine("    private static string? RemoveAccents(string? input)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (string.IsNullOrEmpty(input)) return input;");
            sb.AppendLine("        var normalized = input.Normalize(System.Text.NormalizationForm.FormD);");
            sb.AppendLine("        var sb2 = new System.Text.StringBuilder(normalized.Length);");
            sb.AppendLine("        foreach (var ch in normalized)");
            sb.AppendLine("        {");
            sb.AppendLine("            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);");
            sb.AppendLine("            if (category != System.Globalization.UnicodeCategory.NonSpacingMark)");
            sb.AppendLine("                sb2.Append(ch);");
            sb.AppendLine("        }");
            sb.AppendLine("        return sb2.ToString().Normalize(System.Text.NormalizationForm.FormC);");
            sb.AppendLine("    }");
        }

        if (filterMethods.Any(m =>
                m.Equals("hideCustomBibtex", StringComparison.OrdinalIgnoreCase)
                || m.Equals("hide_custom_bibtex", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine();
            sb.AppendLine("    private static string? HideCustomBibtex(string? input, JekyllPluginContext context)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (string.IsNullOrEmpty(input)) return input;");
            sb.AppendLine("        if (!context.SiteConfig.TryGetValue(\"filtered_bibtex_keywords\", out var kwObj))");
            sb.AppendLine("            return input;");
            sb.AppendLine("        var keywords = kwObj is System.Collections.Generic.IEnumerable<object?> kws");
            sb.AppendLine("            ? kws.Select(k => k?.ToString() ?? string.Empty).Where(k => k.Length > 0)");
            sb.AppendLine("            : new[] { kwObj?.ToString() ?? string.Empty };");
            sb.AppendLine("        foreach (var keyword in keywords)");
            sb.AppendLine("        {");
            sb.AppendLine("            var pattern = @\"(?m)^.*\\b\" + System.Text.RegularExpressions.Regex.Escape(keyword) + @\"\\b\\s*=\\s*\\{.*$\\r?\\n\";");
            sb.AppendLine("            input = System.Text.RegularExpressions.Regex.Replace(input, pattern, string.Empty);");
            sb.AppendLine("        }");
            sb.AppendLine("        input = System.Text.RegularExpressions.Regex.Replace(input,");
            sb.AppendLine("            @\"(?m)^.*\\bauthor\\b\\s*=\\s*\\{.*$\",");
            sb.AppendLine("            m => System.Text.RegularExpressions.Regex.Replace(m.Value, @\"[*†‡§¶‖&^]\", string.Empty));");
            sb.AppendLine("        return input;");
            sb.AppendLine("    }");
        }

        return sb.ToString();
    }

    // ── Code generation helpers ───────────────────────────────────────────────

    private static string GenerateClassSource(
        string fileName,
        string className,
        string interfaceName,
        string memberBody)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using JekyllNet.Core.Plugins;");
        sb.AppendLine();
        sb.AppendLine("namespace JekyllNet.Plugins.Generated;");
        sb.AppendLine();
        sb.AppendLine("// Auto-generated by RubyPluginTranspiler from: " + Path.GetFileName(fileName));
        sb.AppendLine("public sealed class " + className + " : " + interfaceName);
        sb.AppendLine("{");
        sb.AppendLine(memberBody);
        sb.AppendLine("}");
        return sb.ToString();
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static string? ExtractTagName(string rubySource)
    {
        var m = RegisterTagPattern().Match(rubySource);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string ToClassName(string kebabOrSnake)
    {
        return string.Concat(
            kebabOrSnake.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
                        .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    private static string ToSnakeCase(string name)
    {
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c)
                ? "_" + char.ToLowerInvariant(c)
                : char.ToLowerInvariant(c).ToString()));
    }
}

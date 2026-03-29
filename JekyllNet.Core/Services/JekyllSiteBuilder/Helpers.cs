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

    private static void SetIfPresent(Dictionary<string, object?> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
    }

    private static void SetBooleanIfPresent(Dictionary<string, object?> values, string key, bool? value)
    {
        if (value is not null)
        {
            values[key] = value.Value;
        }
    }

    private static FooterLabels ResolveFooterLabels(
        IReadOnlyDictionary<string, object?> siteConfig,
        IReadOnlyDictionary<string, object?>? pageData)
    {
        var language = ResolvePageLanguage(siteConfig, pageData);
        var normalizedLanguage = NormalizeLanguageCode(language);
        if (TryResolveObject(siteConfig, $"_auto_footer_labels.{normalizedLanguage}", out var translatedLabelsValue)
            && translatedLabelsValue is IReadOnlyDictionary<string, object?> translatedLabels)
        {
            return new FooterLabels(
                ReadStringValue(translatedLabels, "icp") ?? "ICP",
                ReadStringValue(translatedLabels, "public_security") ?? "Public Security",
                ReadStringValue(translatedLabels, "telecom_license") ?? "License",
                ReadStringValue(translatedLabels, "terms") ?? "Terms",
                ReadStringValue(translatedLabels, "privacy") ?? "Privacy",
                ReadStringValue(translatedLabels, "report_phone") ?? "Phone",
                ReadStringValue(translatedLabels, "report_email") ?? "Email",
                ":");
        }

        return IsEnglishLanguage(language)
            ? new FooterLabels(
                "ICP Filing No.",
                "Public Security Filing No.",
                "Value-added Telecom License",
                "Terms of Service",
                "Privacy Policy",
                "Report Phone",
                "Report Email",
                ":")
            : new FooterLabels(
                "备案号",
                "公安备案号",
                "增值电信业务经营许可证",
                "服务条款",
                "隐私政策",
                "违法和不良举报电话",
                "举报邮箱",
                "：");
    }

    private static string ResolvePageLanguage(
        IReadOnlyDictionary<string, object?> siteConfig,
        IReadOnlyDictionary<string, object?>? pageData)
    {
        if (pageData is not null)
        {
            if (ReadBooleanValue(pageData, "is_en") is true)
            {
                return "en";
            }

            var pageLanguage = ReadStringValue(pageData, "lang", "language", "locale");
            if (!string.IsNullOrWhiteSpace(pageLanguage))
            {
                return pageLanguage;
            }
        }

        return ReadConfigString(siteConfig, "lang", "language", "locale")
            ?? "zh-CN";
    }

    private static bool IsEnglishLanguage(string language)
        => language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    private static string PrefixLabeledValue(string value, string label, string separator)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (value.Contains(label, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return FormatLabeledValue(label, value, separator);
    }

    private static string FormatLabeledValue(string label, string value, string separator)
    {
        var spacer = separator == ":" ? " " : string.Empty;
        return string.IsNullOrWhiteSpace(value)
            ? $"{label}{separator}{spacer}"
            : $"{label}{separator}{spacer}{value}";
    }

    private static string? ReadStringValue(IReadOnlyDictionary<string, object?> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetDictionaryValue(values, key, out var value) && value?.ToString() is { Length: > 0 } text)
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static bool? ReadBooleanValue(IReadOnlyDictionary<string, object?> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetDictionaryValue(values, key, out var value) && value is not null)
            {
                var parsed = TryConvertToBoolean(value);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static string BuildConfiguredLink(IReadOnlyDictionary<string, object?> siteConfig, string label, params string[] keys)
    {
        var url = ReadConfigString(siteConfig, keys);
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var resolvedUrl = ResolveConfiguredUrl(url, siteConfig);
        return $"<a href=\"{HtmlEncode(resolvedUrl)}\">{HtmlEncode(label)}</a>";
    }

    private static string ResolveConfiguredUrl(string url, IReadOnlyDictionary<string, object?> siteConfig)
    {
        if (string.IsNullOrWhiteSpace(url)
            || IsAbsoluteUrl(url)
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith('#'))
        {
            return url;
        }

        var baseUrl = ReadConfigString(siteConfig, "baseurl") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return EnsureLeadingSlash(url);
        }

        return CombineUrlParts(baseUrl, url);
    }

    private static string BuildExternalLink(string url, string label)
        => $"<a href=\"{HtmlEncode(url)}\" rel=\"nofollow noopener noreferrer\" target=\"_blank\">{HtmlEncode(label)}</a>";

    private static string BuildPublicSecurityRecordUrl(string recordText)
    {
        var digits = Regex.Replace(recordText, @"\D", string.Empty);
        return string.IsNullOrWhiteSpace(digits)
            ? string.Empty
            : $"https://beian.mps.gov.cn/#/query/webSearch?code={digits}";
    }

    private static string InsertBeforeBodyEnd(string html, string snippet)
    {
        var bodyEndIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyEndIndex >= 0
            ? html.Insert(bodyEndIndex, snippet)
            : html + snippet;
    }

    private static string HtmlEncode(string value)
        => WebUtility.HtmlEncode(value);

    private static string EscapeJavaScriptString(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);

    private static bool IsHtmlOutputPath(string outputRelativePath)
        => outputRelativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
           || outputRelativePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

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

}

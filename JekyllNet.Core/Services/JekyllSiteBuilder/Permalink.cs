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
    private static string ResolveCollectionName(string relativePath, bool isPost, HashSet<string> collections)
    {
        if (isPost)
        {
            return "posts";
        }

        var firstSegment = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstSegment) && firstSegment.StartsWith('_'))
        {
            var name = firstSegment[1..];
            if (collections.Contains(name))
            {
                return name;
            }
        }

        return string.Empty;
    }

    private static DateTimeOffset? ResolveDate(string relativePath, Dictionary<string, object?> frontMatter, bool isPost, bool isDraft)
    {
        if (frontMatter.TryGetValue("date", out var dateValue) && dateValue is not null
            && DateTimeOffset.TryParse(dateValue.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedFrontMatterDate))
        {
            return parsedFrontMatterDate;
        }

        if (isPost)
        {
            var fileName = Path.GetFileNameWithoutExtension(relativePath);
            if (HasDatePrefix(fileName)
                && DateTimeOffset.TryParseExact(fileName[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedPostDate))
            {
                return parsedPostDate;
            }
        }

        return isDraft ? DateTimeOffset.UtcNow : null;
    }

    private static string ResolvePermalink(
        string relativePath,
        Dictionary<string, object?> frontMatter,
        DateTimeOffset? date,
        string collection,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> categories,
        bool isPost,
        IReadOnlyDictionary<string, object?> siteConfig,
        JekyllSiteOptions options)
    {
        if (frontMatter.TryGetValue("permalink", out var permalinkValue) && permalinkValue?.ToString() is { Length: > 0 } permalink)
        {
            return NormalizePermalink(permalink, relativePath, date, collection, tags, categories);
        }

        if (isPost
            && siteConfig.TryGetValue("permalink", out var sitePermalinkValue)
            && sitePermalinkValue?.ToString() is { Length: > 0 } sitePermalink)
        {
            return NormalizePermalink(sitePermalink, relativePath, date, collection, tags, categories);
        }

        if (!string.IsNullOrWhiteSpace(collection)
            && TryResolveObject(siteConfig, $"{options.Compatibility.CollectionsKey}.{collection}.permalink", out var collectionPermalinkValue)
            && collectionPermalinkValue?.ToString() is { Length: > 0 } collectionPermalink)
        {
            return NormalizePermalink(collectionPermalink, relativePath, date, collection, tags, categories);
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(relativePath);
        if (isPost)
        {
            var slug = ResolveSlug(relativePath);
            var resolvedDate = date ?? DateTimeOffset.MinValue;
            return $"/{resolvedDate:yyyy}/{resolvedDate:MM}/{resolvedDate:dd}/{slug}/";
        }

        if (!string.IsNullOrWhiteSpace(collection))
        {
            return $"/{collection}/{fileNameWithoutExtension}/";
        }

        if (string.Equals(fileNameWithoutExtension, "index", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/').Trim('/');
            return string.IsNullOrWhiteSpace(directory)
                ? "/"
                : $"/{directory}/";
        }

        return $"/{fileNameWithoutExtension}/";
    }

    private static string NormalizePermalink(
        string permalink,
        string relativePath,
        DateTimeOffset? date,
        string collection,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> categories)
    {
        var slug = ResolveSlug(relativePath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(relativePath);
        var resolvedDate = date ?? DateTimeOffset.MinValue;
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [":title"] = slug,
            [":slug"] = slug,
            [":name"] = fileNameWithoutExtension,
            [":path"] = ResolvePermalinkPath(relativePath, collection),
            [":collection"] = collection,
            [":categories"] = BuildPermalinkTaxonomyPath(categories),
            [":tags"] = BuildPermalinkTaxonomyPath(tags),
            [":year"] = resolvedDate.ToString("yyyy", CultureInfo.InvariantCulture),
            [":month"] = resolvedDate.ToString("MM", CultureInfo.InvariantCulture),
            [":day"] = resolvedDate.ToString("dd", CultureInfo.InvariantCulture)
        };

        var replaced = permalink.Replace('\\', '/');
        foreach (var token in tokens)
        {
            replaced = replaced.Replace(token.Key, token.Value, StringComparison.Ordinal);
        }

        replaced = Regex.Replace(replaced, "/{2,}", "/");

        if (!replaced.StartsWith('/'))
        {
            replaced = "/" + replaced;
        }

        if (!replaced.EndsWith('/'))
        {
            replaced += "/";
        }

        return replaced;
    }

    private static string ResolvePermalinkPath(string relativePath, string collection)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var withoutExtension = Path.ChangeExtension(normalized, null)?.Replace('\\', '/') ?? normalized;
        var segments = withoutExtension.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count == 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(collection))
        {
            var collectionDirectoryName = "_" + collection;
            if (string.Equals(segments[0], collectionDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                segments.RemoveAt(0);
            }
        }

        return string.Join('/', segments);
    }

    private static string BuildPermalinkTaxonomyPath(IReadOnlyList<string> values)
        => string.Join('/', values.Select(SlugifyPermalinkSegment).Where(static value => value.Length > 0));

    private static string SlugifyPermalinkSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var slug = new System.Text.StringBuilder();
        var previousWasHyphen = false;

        foreach (var character in value.ToLowerInvariant())
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

    private static bool ShouldIncludeItem(JekyllContentItem item, JekyllSiteOptions options)
    {
        if (item.IsDraft && !options.IncludeDrafts)
        {
            return false;
        }

        if (!options.IncludeUnpublished
            && item.FrontMatter.TryGetValue("published", out var publishedValue)
            && TryConvertToBoolean(publishedValue) is false)
        {
            return false;
        }

        if (!options.IncludeFuture
            && item.Date is { } date
            && date > DateTimeOffset.UtcNow)
        {
            return false;
        }

        return true;
    }

    private static bool? TryConvertToBoolean(object? value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            string text when bool.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool HasDatePrefix(string fileName)
        => fileName.Length > 11
           && fileName[10] == '-'
           && DateTimeOffset.TryParseExact(fileName[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);

    private static string ResolveSlug(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        return HasDatePrefix(fileName) ? fileName[11..] : fileName;
    }

    private static string EnsureTrailingSlash(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "/";
        }

        return url.EndsWith('/') ? url : url + "/";
    }

    private static string UrlToOutputPath(string url)
    {
        if (string.Equals(url, "/", StringComparison.Ordinal))
        {
            return "index.html";
        }

        var trimmed = url.Trim('/');
        if (Path.HasExtension(trimmed))
        {
            return trimmed;
        }

        return Path.Combine(trimmed.Replace('/', Path.DirectorySeparatorChar), "index.html");
    }

    private static bool ShouldSkip(string relativePath, IReadOnlyDictionary<string, object?> siteConfig, JekyllSiteOptions options)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (ShouldIncludePath(normalized, siteConfig))
        {
            return false;
        }

        if (normalized.StartsWith(options.Compatibility.DestinationDirectoryName + "/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (MatchesConfiguredPathList(normalized, siteConfig, "exclude"))
        {
            return true;
        }

        var segments = normalized.Split('/');
        return segments.Any(segment =>
            segment.StartsWith('.')
            || string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, options.Compatibility.LayoutsDirectoryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, options.Compatibility.IncludesDirectoryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, options.Compatibility.DataDirectoryName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldIncludePath(string normalizedPath, IReadOnlyDictionary<string, object?> siteConfig)
        => MatchesConfiguredPathList(normalizedPath, siteConfig, "include");

    private static bool MatchesConfiguredPathList(string normalizedPath, IReadOnlyDictionary<string, object?> siteConfig, string key)
    {
        if (!siteConfig.TryGetValue(key, out var configuredValue) || configuredValue is null)
        {
            return false;
        }

        var patterns = configuredValue switch
        {
            string single => [single],
            IEnumerable<object?> sequence => sequence.Select(item => item?.ToString()).Where(static item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToList(),
            _ => []
        };

        foreach (var pattern in patterns)
        {
            var normalizedPattern = pattern.Replace('\\', '/').Trim().Trim('/');
            if (normalizedPattern.Length == 0)
            {
                continue;
            }

            if (normalizedPattern.Contains('*'))
            {
                var regexPattern = "^" + Regex.Escape(normalizedPattern).Replace(@"\*", ".*") + "($|/)";
                if (Regex.IsMatch(normalizedPath, regexPattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (string.Equals(normalizedPath, normalizedPattern, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(normalizedPattern + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSassFile(string path)
        => path.EndsWith(".scss", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".sass", StringComparison.OrdinalIgnoreCase);

    private static bool IsCssFile(string path)
        => path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);

    private static string CompileFrontMatterCss(JekyllStaticFile file, string rendered)
    {
        if (string.IsNullOrWhiteSpace(rendered))
        {
            return rendered;
        }

        var rootDirectory = ResolveStaticFileRootDirectory(file);
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return rendered;
        }

        var inheritedThemeDirectories = ResolveInheritedThemeDirectories(rootDirectory);
        var includePaths = inheritedThemeDirectories
            .Concat([rootDirectory])
            .Select(directory => Path.Combine(directory, "_sass"))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (includePaths.Count == 0)
        {
            return rendered;
        }

        try
        {
            var normalizedSource = NormalizeSassImportPaths(rendered, file.SourcePath, includePaths);
            var compiler = new SassCompiler();
            var result = compiler.Compile(normalizedSource, indentedSyntax: false, options: new CompilationOptions
            {
                IncludePaths = includePaths
            });

            return result.CompiledContent;
        }
        catch
        {
            return rendered;
        }
    }

    private static string? ResolveStaticFileRootDirectory(JekyllStaticFile file)
    {
        if (string.IsNullOrWhiteSpace(file.SourcePath) || string.IsNullOrWhiteSpace(file.RelativePath))
        {
            return null;
        }

        var sourcePath = Path.GetFullPath(file.SourcePath);
        var relativePath = file.RelativePath.Replace('/', Path.DirectorySeparatorChar);
        if (!sourcePath.EndsWith(relativePath, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(sourcePath);
        }

        var rootLength = sourcePath.Length - relativePath.Length;
        return sourcePath[..rootLength].TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool IsContentFile(string path)
        => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

    private static bool IsTextStaticFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".txt" or ".text" or ".js" or ".json" or ".xml" or ".css" or ".html" or ".svg" or ".csv" or ".yml" or ".yaml";
    }

    private static void SetNestedValue(Dictionary<string, object?> root, string dottedPath, object? value)
    {
        var segments = dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = root;

        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            if (!current.TryGetValue(segment, out var existing) || existing is not Dictionary<string, object?> child)
            {
                child = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[segment] = child;
            }

            current = child;
        }

        current[segments[^1]] = value;
    }

    private static object? NormalizeDataValue(object? value)
    {
        return value switch
        {
            Dictionary<object, object?> dict => dict.ToDictionary(
                x => x.Key.ToString() ?? string.Empty,
                x => NormalizeDataValue(x.Value),
                StringComparer.OrdinalIgnoreCase),
            IList<object?> list => list.Select(NormalizeDataValue).ToList(),
            _ => value
        };
    }

    private static object? NormalizeYamlValue(object? value)
    {
        return value switch
        {
            Dictionary<object, object?> dict => dict.ToDictionary(
                x => x.Key.ToString() ?? string.Empty,
                x => NormalizeYamlValue(x.Value),
                StringComparer.OrdinalIgnoreCase),
            IList<object?> list => list.Select(NormalizeYamlValue).ToList(),
            _ => value
        };
    }

    /// <summary>
    /// Resolves <c>{{version}}</c> placeholders in config string values.
    /// When a YAML object defines a <c>version</c> key, every string value
    /// within that object (and its nested objects) that contains the literal
    /// text <c>{{version}}</c> is replaced with the version string.
    /// This makes JekyllNet compatible with themes such as al-folio that use
    /// this pattern inside <c>third_party_libraries</c>.
    /// </summary>
    private static void ResolveVersionPlaceholders(Dictionary<string, object?> dict, string? inheritedVersion = null)
    {
        // A version key on this dict takes precedence over any inherited version.
        if (dict.TryGetValue("version", out var v) && v is string ownVersion)
            inheritedVersion = ownVersion;

        foreach (var key in dict.Keys.ToList())
        {
            switch (dict[key])
            {
                case string s when inheritedVersion is not null && s.Contains("{{version}}"):
                    dict[key] = s.Replace("{{version}}", inheritedVersion);
                    break;
                case Dictionary<string, object?> nested:
                    ResolveVersionPlaceholders(nested, inheritedVersion);
                    break;
                case List<object?> list when inheritedVersion is not null:
                    ResolveVersionPlaceholdersInList(list, inheritedVersion);
                    break;
            }
        }
    }

    private static void ResolveVersionPlaceholdersInList(List<object?> list, string version)
    {
        for (var i = 0; i < list.Count; i++)
        {
            switch (list[i])
            {
                case string s when s.Contains("{{version}}"):
                    list[i] = s.Replace("{{version}}", version);
                    break;
                case Dictionary<string, object?> nested:
                    ResolveVersionPlaceholders(nested, version);
                    break;
                case List<object?> nestedList:
                    ResolveVersionPlaceholdersInList(nestedList, version);
                    break;
            }
        }
    }

    private static void LogInfo(JekyllSiteOptions options, string message, bool verboseOnly = false)
    {
        if (options.Log is null || (verboseOnly && !options.VerboseLogging))
        {
            return;
        }

        options.Log(message);
    }

    private static string NormalizeLogPath(string path)
        => path.Replace('\\', '/');

    private sealed record FooterLabels(
        string IcpLabel,
        string PublicSecurityLabel,
        string TelecomLicenseLabel,
        string TermsLabel,
        string PrivacyLabel,
        string ReportPhoneLabel,
        string ReportEmailLabel,
        string ValueSeparator);

    private sealed record AiTranslationSettings(
        string Provider,
        string Model,
        string BaseUrl,
        string? ApiKey,
        List<string> TargetLanguages,
        List<string> FrontMatterKeys,
        string? CachePath,
        AiTranslationGlossary Glossary);

    private sealed record LocaleDefinition(string Code, string Root, string Label);

    private sealed record TranslationLink(string Code, string Label, string Url);
}

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
    private static HashSet<string> ReadCollectionDefinitions(Dictionary<string, object?> siteConfig, JekyllSiteOptions options)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (siteConfig.TryGetValue(options.Compatibility.CollectionsKey, out var collectionsValue)
            && collectionsValue is Dictionary<string, object?> collections)
        {
            foreach (var key in collections.Keys)
            {
                result.Add(key);
            }
        }

        return result;
    }

    private static Dictionary<string, object?> ApplyFrontMatterDefaults(
        string relativePath,
        Dictionary<string, object?> frontMatter,
        Dictionary<string, object?> siteConfig,
        HashSet<string> collections,
        JekyllSiteOptions options)
    {
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (siteConfig.TryGetValue("defaults", out var defaultsValue) && defaultsValue is IEnumerable<object?> entries)
        {
            var contentType = ResolveDefaultScopeType(relativePath, collections, options);
            foreach (var entry in entries.OfType<Dictionary<string, object?>>())
            {
                if (!entry.TryGetValue("scope", out var scopeValue) || scopeValue is not Dictionary<string, object?> scope)
                {
                    continue;
                }

                if (!entry.TryGetValue("values", out var valuesValue) || valuesValue is not Dictionary<string, object?> values)
                {
                    continue;
                }

                if (!ScopeMatches(relativePath, contentType, scope))
                {
                    continue;
                }

                foreach (var pair in values)
                {
                    merged[pair.Key] = pair.Value;
                }
            }
        }

        foreach (var pair in frontMatter)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static Dictionary<string, List<JekyllContentItem>> BuildTaxonomy(
        IEnumerable<JekyllContentItem> items,
        Func<JekyllContentItem, IEnumerable<string>> selector)
    {
        return items
            .SelectMany(item => selector(item).Distinct(StringComparer.OrdinalIgnoreCase).Select(value => (value, item)))
            .GroupBy(x => x.value, x => x.item, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Date).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private List<JekyllContentItem> CreatePaginationItems(
        IReadOnlyCollection<JekyllContentItem> items,
        IReadOnlyList<JekyllContentItem> posts,
        IReadOnlyDictionary<string, object?> siteConfig,
        JekyllSiteOptions options)
    {
        var result = new List<JekyllContentItem>();
        var paginatedPosts = posts.Where(ShouldIncludeInPagination).ToList();

        foreach (var item in items.Where(CanPaginate))
        {
            var pageSize = ResolvePaginationPageSize(item, siteConfig, options);
            if (pageSize is null || pageSize <= 0 || paginatedPosts.Count == 0)
            {
                continue;
            }

            var totalPages = (int)Math.Ceiling(paginatedPosts.Count / (double)pageSize.Value);
            for (var pageNumber = 1; pageNumber <= totalPages; pageNumber++)
            {
                var pagePosts = paginatedPosts
                    .Skip((pageNumber - 1) * pageSize.Value)
                    .Take(pageSize.Value)
                    .Select(item => ToLiquidObject(item, ShouldShowExcerpts(siteConfig)))
                    .Cast<object?>()
                    .ToList();
                var paginator = BuildPaginator(item, pagePosts, pageNumber, totalPages, pageSize.Value, paginatedPosts.Count, siteConfig);

                if (pageNumber == 1)
                {
                    item.Paginator = paginator;
                    continue;
                }

                result.Add(new JekyllContentItem
                {
                    SourcePath = item.SourcePath,
                    RelativePath = item.RelativePath,
                    OutputRelativePath = UrlToOutputPath(paginator["page_path"]?.ToString() ?? item.Url),
                    Url = paginator["page_path"]?.ToString() ?? item.Url,
                    Collection = item.Collection,
                    IsPost = item.IsPost,
                    IsDraft = item.IsDraft,
                    Date = item.Date,
                    Tags = [.. item.Tags],
                    Categories = [.. item.Categories],
                    FrontMatter = new Dictionary<string, object?>(item.FrontMatter, StringComparer.OrdinalIgnoreCase),
                    RawContent = item.RawContent,
                    RenderedContent = item.RenderedContent,
                    Excerpt = item.Excerpt,
                    Paginator = paginator
                });
            }
        }

        return result;
    }

    private static Dictionary<string, object?> BuildPaginator(
        JekyllContentItem item,
        List<object?> pagePosts,
        int pageNumber,
        int totalPages,
        int pageSize,
        int totalPosts,
        IReadOnlyDictionary<string, object?> siteConfig)
    {
        var pagePath = pageNumber == 1 ? item.Url : ResolvePaginationUrl(item, pageNumber, siteConfig);
        var previousPagePath = pageNumber > 1
            ? (pageNumber == 2 ? item.Url : ResolvePaginationUrl(item, pageNumber - 1, siteConfig))
            : null;
        var nextPagePath = pageNumber < totalPages
            ? ResolvePaginationUrl(item, pageNumber + 1, siteConfig)
            : null;

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = pageNumber,
            ["per_page"] = pageSize,
            ["posts"] = pagePosts,
            ["total_posts"] = totalPosts,
            ["total_pages"] = totalPages,
            ["previous_page"] = pageNumber > 1 ? pageNumber - 1 : null,
            ["previous_page_path"] = previousPagePath,
            ["next_page"] = pageNumber < totalPages ? pageNumber + 1 : null,
            ["next_page_path"] = nextPagePath,
            ["page_path"] = pagePath
        };
    }

    private static List<string> ReadStringList(Dictionary<string, object?> frontMatter, string key)
    {
        if (!frontMatter.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        if (value is string single)
        {
            return single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        if (value is IEnumerable<object?> sequence)
        {
            return sequence.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList();
        }

        return [value.ToString()!];
    }

    private static bool CanPaginate(JekyllContentItem item)
        => !item.IsPost
           && !IsPaginationDisabled(item.FrontMatter)
           && (item.RelativePath.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase)
               || string.Equals(item.RelativePath, "index.html", StringComparison.OrdinalIgnoreCase));

    private static bool IsPaginationDisabled(IReadOnlyDictionary<string, object?> frontMatter)
    {
        if (frontMatter.TryGetValue("pagination", out var paginationValue)
            && paginationValue is bool paginationEnabled)
        {
            return !paginationEnabled;
        }

        return TryResolveObject(frontMatter, "pagination.enabled", out var enabledValue)
            && TryConvertToBoolean(enabledValue) is false;
    }

    private static bool ShouldIncludeInPagination(JekyllContentItem item)
    {
        return !(item.FrontMatter.TryGetValue("hidden", out var hiddenValue)
            && TryConvertToBoolean(hiddenValue) is true);
    }

    private static int? ResolvePaginationPageSize(JekyllContentItem item, IReadOnlyDictionary<string, object?> siteConfig, JekyllSiteOptions options)
    {
        if (item.FrontMatter.TryGetValue("paginate", out var pageValue) && TryConvertToInt(pageValue, out var pageSize))
        {
            return pageSize;
        }

        if (TryResolveObject(item.FrontMatter, "pagination.per_page", out var pagePerPageValue) && TryConvertToInt(pagePerPageValue, out pageSize))
        {
            return pageSize;
        }

        if (siteConfig.TryGetValue("paginate", out var sitePaginateValue) && TryConvertToInt(sitePaginateValue, out pageSize))
        {
            return pageSize;
        }

        if (TryResolveObject(siteConfig, "pagination.per_page", out var configPerPageValue) && TryConvertToInt(configPerPageValue, out pageSize))
        {
            return pageSize;
        }

        return options.PostsPerPage;
    }

    private static bool ShouldShowExcerpts(IReadOnlyDictionary<string, object?> siteConfig)
    {
        return siteConfig.TryGetValue("show_excerpts", out var showExcerptsValue)
            && TryConvertToBoolean(showExcerptsValue) is true;
    }

    private static bool TryConvertToInt(object? value, out int result)
    {
        return value switch
        {
            int intValue => (result = intValue) > 0,
            long longValue when longValue is > 0 and <= int.MaxValue => (result = (int)longValue) > 0,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => (result = parsed) > 0,
            _ => int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result > 0
        };
    }

    private static string ResolvePaginationUrl(JekyllContentItem item, int pageNumber, IReadOnlyDictionary<string, object?> siteConfig)
    {
        var paginatePath = item.FrontMatter.TryGetValue("paginate_path", out var pagePathValue)
            ? pagePathValue?.ToString()
            : siteConfig.TryGetValue("paginate_path", out var configPathValue)
                ? configPathValue?.ToString()
                : null;

        paginatePath ??= ReadStringValue(item.FrontMatter, "pagination_path");
        paginatePath ??= TryResolveObject(item.FrontMatter, "pagination.path", out var nestedPagePathValue)
            ? nestedPagePathValue?.ToString()
            : null;
        paginatePath ??= TryResolveObject(item.FrontMatter, "pagination.permalink", out var nestedPagePermalinkValue)
            ? nestedPagePermalinkValue?.ToString()
            : null;
        paginatePath ??= ReadConfigString(siteConfig, "pagination.path", "pagination.permalink");

        if (string.IsNullOrWhiteSpace(paginatePath))
        {
            return item.Url.TrimEnd('/') + $"/page{pageNumber}/";
        }

        var resolved = paginatePath.Replace(":num", pageNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (resolved.StartsWith('/'))
        {
            return EnsureTrailingSlash(resolved);
        }

        return EnsureTrailingSlash(item.Url.TrimEnd('/') + "/" + resolved.TrimStart('/'));
    }

    private static string BuildExcerpt(JekyllContentItem item, MarkdownPipeline markdownPipeline, IReadOnlyDictionary<string, object?> siteConfig)
    {
        var excerptSource = ExtractExcerptSource(item.RawContent, item.FrontMatter, siteConfig);
        if (string.IsNullOrWhiteSpace(excerptSource))
        {
            return string.Empty;
        }

        return item.SourcePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? excerptSource.Trim()
            : Markdown.ToHtml(excerptSource, markdownPipeline).Trim();
    }

    private static string ExtractExcerptSource(
        string content,
        IReadOnlyDictionary<string, object?> frontMatter,
        IReadOnlyDictionary<string, object?> siteConfig)
    {
        if (frontMatter.TryGetValue("excerpt", out var explicitExcerpt)
            && !string.IsNullOrWhiteSpace(explicitExcerpt?.ToString()))
        {
            return explicitExcerpt.ToString()!.Trim();
        }

        var separator = frontMatter.TryGetValue("excerpt_separator", out var frontMatterSeparator)
            ? frontMatterSeparator?.ToString()
            : siteConfig.TryGetValue("excerpt_separator", out var configSeparator)
                ? configSeparator?.ToString()
                : null;

        if (!string.IsNullOrWhiteSpace(separator))
        {
            var separatorIndex = content.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex >= 0)
            {
                return content[..separatorIndex].Trim();
            }
        }

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var paragraphs = normalized.Split("\n\n", 2, StringSplitOptions.None);
        return paragraphs[0].Trim();
    }

    private static string ResolveExcerptValue(JekyllContentItem item)
    {
        if (item.FrontMatter.TryGetValue("excerpt", out var explicitExcerpt)
            && !string.IsNullOrWhiteSpace(explicitExcerpt?.ToString()))
        {
            return explicitExcerpt.ToString()!;
        }

        return item.Excerpt;
    }

    private static bool ShouldPreserveRenderedHtml(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '<')
        {
            return false;
        }

        return !trimmed.Contains("{%", StringComparison.Ordinal)
            && !trimmed.Contains("{{", StringComparison.Ordinal);
    }

    private static string ResolveDefaultScopeType(string relativePath, HashSet<string> collections, JekyllSiteOptions options)
    {
        if (relativePath.StartsWith(options.Compatibility.PostsDirectoryName + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "posts";
        }

        if (relativePath.StartsWith("_drafts/", StringComparison.OrdinalIgnoreCase))
        {
            return "drafts";
        }

        var collection = ResolveCollectionName(relativePath, isPost: false, collections);
        return string.IsNullOrWhiteSpace(collection) ? "pages" : collection;
    }

    private static bool ScopeMatches(string relativePath, string contentType, Dictionary<string, object?> scope)
    {
        var pathScope = scope.TryGetValue("path", out var pathValue) ? pathValue?.ToString() ?? string.Empty : string.Empty;
        var typeScope = scope.TryGetValue("type", out var typeValue) ? typeValue?.ToString() ?? string.Empty : string.Empty;

        if (!string.IsNullOrWhiteSpace(typeScope) && !string.Equals(typeScope, contentType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        var normalizedScope = pathScope.Replace('\\', '/').Trim('/').Trim();
        if (string.IsNullOrWhiteSpace(normalizedScope))
        {
            return true;
        }

        if (normalizedScope.Contains('*'))
        {
            var pattern = "^" + Regex.Escape(normalizedScope).Replace(@"\*", ".*") + "$";
            return Regex.IsMatch(normalizedPath, pattern, RegexOptions.IgnoreCase);
        }

        return string.Equals(normalizedPath, normalizedScope, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedScope + "/", StringComparison.OrdinalIgnoreCase);
    }

}

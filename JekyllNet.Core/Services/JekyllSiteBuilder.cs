using System.Globalization;
using System.Text.RegularExpressions;
using DartSassHost;
using Markdig;
using JekyllNet.Core.Models;
using JekyllNet.Core.Parsers;
using JekyllNet.Core.Rendering;
using YamlDotNet.Serialization;

namespace JekyllNet.Core.Services;

public sealed class JekyllSiteBuilder
{
    private readonly FrontMatterParser _frontMatterParser = new();
    private readonly TemplateRenderer _templateRenderer = new();
    private readonly IDeserializer _yamlDeserializer = new DeserializerBuilder().Build();

    public async Task BuildAsync(JekyllSiteOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DestinationDirectory);

        if (!Directory.Exists(options.SourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {options.SourceDirectory}");
        }

        if (Directory.Exists(options.DestinationDirectory))
        {
            Directory.Delete(options.DestinationDirectory, recursive: true);
        }

        Directory.CreateDirectory(options.DestinationDirectory);

        var siteConfig = await LoadConfigAsync(options.SourceDirectory, cancellationToken);
        var data = await LoadDataAsync(options.SourceDirectory, options, cancellationToken);
        var layouts = await LoadNamedTemplatesAsync(options.SourceDirectory, options.Compatibility.LayoutsDirectoryName, cancellationToken);
        var includes = await LoadNamedTemplatesAsync(options.SourceDirectory, options.Compatibility.IncludesDirectoryName, cancellationToken);
        var markdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        var items = await DiscoverContentItemsAsync(options.SourceDirectory, siteConfig, options, cancellationToken);
        var posts = items.Where(x => x.IsPost).OrderByDescending(x => x.Date).ToList();
        var collections = items
            .Where(x => !string.IsNullOrWhiteSpace(x.Collection))
            .GroupBy(x => x.Collection, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Date).ToList(), StringComparer.OrdinalIgnoreCase);
        var tags = BuildTaxonomy(items, static item => item.Tags);
        var categories = BuildTaxonomy(items, static item => item.Categories);

        var context = new JekyllSiteContext
        {
            SourceDirectory = options.SourceDirectory,
            DestinationDirectory = options.DestinationDirectory,
            SiteConfig = BuildSiteVariables(siteConfig, data, posts, collections, tags, categories, options),
            Layouts = layouts,
            Includes = includes,
            Posts = posts,
            Collections = collections,
            Compatibility = options.Compatibility
        };

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var html = item.SourcePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                ? item.RawContent
                : Markdown.ToHtml(item.RawContent, markdownPipeline);

            item.RenderedContent = html;

            var variables = BuildVariables(context, item, html);
            var rendered = ApplyLayout(item, html, context.Layouts, context.Includes, variables);

            var destinationPath = Path.Combine(options.DestinationDirectory, item.OutputRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(destinationPath, rendered, cancellationToken);
        }

        await CompileSassAsync(options.SourceDirectory, options.DestinationDirectory, options, cancellationToken);
        await CopyStaticFilesAsync(options.SourceDirectory, options.DestinationDirectory, items, options, cancellationToken);
    }

    private async Task<List<JekyllContentItem>> DiscoverContentItemsAsync(
        string sourceDirectory,
        Dictionary<string, object?> siteConfig,
        JekyllSiteOptions options,
        CancellationToken cancellationToken)
    {
        var result = new List<JekyllContentItem>();
        var collectionDefinitions = ReadCollectionDefinitions(siteConfig, options);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            if (ShouldSkip(relativePath, options) || !IsContentFile(file))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var document = _frontMatterParser.Parse(text);
            var frontMatter = ApplyFrontMatterDefaults(relativePath, document.FrontMatter, siteConfig, collectionDefinitions, options);
            var item = CreateContentItem(file, relativePath, frontMatter, document.Content, collectionDefinitions, options);
            if (ShouldIncludeItem(item, options))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private JekyllContentItem CreateContentItem(
        string sourcePath,
        string relativePath,
        Dictionary<string, object?> frontMatter,
        string rawContent,
        HashSet<string> collections,
        JekyllSiteOptions options)
    {
        var isDraft = relativePath.StartsWith("_drafts/", StringComparison.OrdinalIgnoreCase);
        var isPost = isDraft || relativePath.StartsWith(options.Compatibility.PostsDirectoryName + "/", StringComparison.OrdinalIgnoreCase);
        var collection = ResolveCollectionName(relativePath, isPost, collections);
        var date = ResolveDate(relativePath, frontMatter, isPost, isDraft);
        var url = ResolvePermalink(relativePath, frontMatter, date, collection, isPost);
        var tags = ReadStringList(frontMatter, "tags");
        var categories = ReadStringList(frontMatter, "categories");

        return new JekyllContentItem
        {
            SourcePath = sourcePath,
            RelativePath = relativePath,
            FrontMatter = frontMatter,
            RawContent = rawContent,
            Collection = collection,
            IsPost = isPost,
            IsDraft = isDraft,
            Date = date,
            Tags = tags,
            Categories = categories,
            Url = url,
            OutputRelativePath = UrlToOutputPath(url)
        };
    }

    private Dictionary<string, object?> BuildVariables(JekyllSiteContext context, JekyllContentItem item, string content)
    {
        var page = new Dictionary<string, object?>(item.FrontMatter, StringComparer.OrdinalIgnoreCase)
        {
            ["content"] = content,
            ["path"] = item.RelativePath,
            ["url"] = item.Url,
            ["date"] = item.Date,
            ["collection"] = item.Collection,
            ["tags"] = item.Tags.Cast<object?>().ToList(),
            ["categories"] = item.Categories.Cast<object?>().ToList()
        };

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = page,
            ["site"] = context.SiteConfig,
            ["content"] = content
        };
    }

    private Dictionary<string, object?> BuildSiteVariables(
        Dictionary<string, object?> siteConfig,
        Dictionary<string, object?> data,
        List<JekyllContentItem> posts,
        Dictionary<string, List<JekyllContentItem>> collections,
        Dictionary<string, List<JekyllContentItem>> tags,
        Dictionary<string, List<JekyllContentItem>> categories,
        JekyllSiteOptions options)
    {
        return new Dictionary<string, object?>(siteConfig, StringComparer.OrdinalIgnoreCase)
        {
            ["data"] = data,
            ["posts"] = posts.Select(ToLiquidObject).Cast<object?>().ToList(),
            ["collections"] = collections.ToDictionary(
                x => x.Key,
                x => x.Value.Select(ToLiquidObject).Cast<object?>().ToList(),
                StringComparer.OrdinalIgnoreCase),
            ["tags"] = tags.ToDictionary(
                x => x.Key,
                x => x.Value.Select(ToLiquidObject).Cast<object?>().ToList(),
                StringComparer.OrdinalIgnoreCase),
            ["categories"] = categories.ToDictionary(
                x => x.Key,
                x => x.Value.Select(ToLiquidObject).Cast<object?>().ToList(),
                StringComparer.OrdinalIgnoreCase),
            ["github_pages"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["enabled"] = options.Compatibility.Enabled,
                ["plugins"] = options.Compatibility.WhitelistedPlugins.Cast<object?>().ToList(),
                ["source"] = options.SourceDirectory,
                ["destination"] = options.DestinationDirectory
            }
        };
    }

    private Dictionary<string, object?> ToLiquidObject(JekyllContentItem item)
        => new(item.FrontMatter, StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = item.FrontMatter.TryGetValue("title", out var title) ? title : Path.GetFileNameWithoutExtension(item.RelativePath),
            ["url"] = item.Url,
            ["date"] = item.Date,
            ["content"] = item.RenderedContent,
            ["path"] = item.RelativePath,
            ["collection"] = item.Collection,
            ["tags"] = item.Tags.Cast<object?>().ToList(),
            ["categories"] = item.Categories.Cast<object?>().ToList()
        };

    private string ApplyLayout(
        JekyllContentItem item,
        string content,
        IReadOnlyDictionary<string, string> layouts,
        IReadOnlyDictionary<string, string> includes,
        Dictionary<string, object?> variables)
    {
        var pageContent = _templateRenderer.Render(content, variables, includes);
        variables["content"] = pageContent;

        if (variables["page"] is Dictionary<string, object?> page)
        {
            page["content"] = pageContent;
        }

        if (!item.FrontMatter.TryGetValue("layout", out var layoutName) || layoutName is null)
        {
            return pageContent;
        }

        return RenderLayout(layoutName.ToString() ?? string.Empty, layouts, includes, variables, pageContent, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private string RenderLayout(
        string layoutKey,
        IReadOnlyDictionary<string, string> layouts,
        IReadOnlyDictionary<string, string> includes,
        Dictionary<string, object?> variables,
        string content,
        HashSet<string> visitedLayouts)
    {
        if (!layouts.TryGetValue(layoutKey, out var layoutTemplate) || !visitedLayouts.Add(layoutKey))
        {
            return content;
        }

        var renderedLayout = _templateRenderer.Render(layoutTemplate, variables, includes);
        var layoutDocument = _frontMatterParser.Parse(renderedLayout);
        var layoutContent = layoutDocument.Content.Replace("{{ content }}", content, StringComparison.Ordinal);

        foreach (var pair in layoutDocument.FrontMatter)
        {
            if (variables["page"] is Dictionary<string, object?> page)
            {
                page[pair.Key] = pair.Value;
            }
        }

        if (layoutDocument.FrontMatter.TryGetValue("layout", out var parentLayout) && parentLayout is not null)
        {
            variables["content"] = layoutContent;
            return RenderLayout(parentLayout.ToString() ?? string.Empty, layouts, includes, variables, layoutContent, visitedLayouts);
        }

        return layoutContent;
    }

    private async Task CopyStaticFilesAsync(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyCollection<JekyllContentItem> items,
        JekyllSiteOptions options,
        CancellationToken cancellationToken)
    {
        var renderedContentPaths = items.Select(x => x.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            if (ShouldSkip(relativePath, options) || renderedContentPaths.Contains(relativePath))
            {
                continue;
            }

            if (IsSassFile(file))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }

        await Task.CompletedTask;
    }

    private async Task<Dictionary<string, object?>> LoadConfigAsync(string sourceDirectory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(sourceDirectory, "_config.yml");
        if (!File.Exists(path))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var yaml = await File.ReadAllTextAsync(path, cancellationToken);
        var values = _yamlDeserializer.Deserialize<Dictionary<object, object?>>(yaml) ?? new Dictionary<object, object?>();
        return values.ToDictionary(
            k => k.Key.ToString() ?? string.Empty,
            v => NormalizeYamlValue(v.Value),
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, string>> LoadNamedTemplatesAsync(string sourceDirectory, string directoryName, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(sourceDirectory, directoryName);
        if (!Directory.Exists(directory))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            var relativeName = Path.GetRelativePath(directory, file).Replace('\\', '/');
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            result[relativeName] = content;
            result[Path.GetFileName(relativeName)] = content;
            result[Path.GetFileNameWithoutExtension(relativeName)] = content;
        }

        return result;
    }

    private async Task<Dictionary<string, object?>> LoadDataAsync(string sourceDirectory, JekyllSiteOptions options, CancellationToken cancellationToken)
    {
        var dataDirectory = Path.Combine(sourceDirectory, options.Compatibility.DataDirectoryName);
        if (!Directory.Exists(dataDirectory))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(dataDirectory, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativeName = Path.GetRelativePath(dataDirectory, file).Replace('\\', '/');
            var key = Path.ChangeExtension(relativeName, null)?.Replace('/', '.');
            var extension = Path.GetExtension(file);
            var text = await File.ReadAllTextAsync(file, cancellationToken);

            object? value = extension.ToLowerInvariant() switch
            {
                ".yml" or ".yaml" => _yamlDeserializer.Deserialize<object?>(text),
                ".json" => text,
                _ => text
            };

            if (!string.IsNullOrWhiteSpace(key))
            {
                SetNestedValue(result, key, NormalizeDataValue(value));
            }
        }

        return result;
    }

    private async Task CompileSassAsync(string sourceDirectory, string destinationDirectory, JekyllSiteOptions options, CancellationToken cancellationToken)
    {
        var sassFiles = Directory.EnumerateFiles(sourceDirectory, "*.*", SearchOption.AllDirectories)
            .Where(IsSassFile)
            .Where(file =>
            {
                var relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                var fileName = Path.GetFileName(relative);
                return !fileName.StartsWith("_", StringComparison.Ordinal) && !ShouldSkip(relative, options);
            })
            .ToList();

        if (sassFiles.Count == 0)
        {
            return;
        }

        var compiler = new SassCompiler();
        foreach (var file in sassFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            var cssRelative = Path.ChangeExtension(relative, ".css")!;
            var destinationPath = Path.Combine(destinationDirectory, cssRelative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            try
            {
                var result = compiler.CompileFile(file);
                await File.WriteAllTextAsync(destinationPath, result.CompiledContent, cancellationToken);
            }
            catch (Exception)
            {
                var fallbackContent = await File.ReadAllTextAsync(file, cancellationToken);
                await File.WriteAllTextAsync(destinationPath, fallbackContent, cancellationToken);
            }
        }
    }

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

    private static string ResolvePermalink(string relativePath, Dictionary<string, object?> frontMatter, DateTimeOffset? date, string collection, bool isPost)
    {
        if (frontMatter.TryGetValue("permalink", out var permalinkValue) && permalinkValue?.ToString() is { Length: > 0 } permalink)
        {
            return NormalizePermalink(permalink, relativePath, date, collection);
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

    private static string NormalizePermalink(string permalink, string relativePath, DateTimeOffset? date, string collection)
    {
        var slug = ResolveSlug(relativePath);
        var resolvedDate = date ?? DateTimeOffset.MinValue;

        var replaced = permalink
            .Replace(":title", slug, StringComparison.Ordinal)
            .Replace(":slug", slug, StringComparison.Ordinal)
            .Replace(":collection", collection, StringComparison.Ordinal)
            .Replace(":year", resolvedDate.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(":month", resolvedDate.ToString("MM", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(":day", resolvedDate.ToString("dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace('\\', '/');

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

    private static string UrlToOutputPath(string url)
    {
        if (string.Equals(url, "/", StringComparison.Ordinal))
        {
            return "index.html";
        }

        var trimmed = url.Trim('/');
        if (trimmed.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return Path.Combine(trimmed.Replace('/', Path.DirectorySeparatorChar), "index.html");
    }

    private static bool ShouldSkip(string relativePath, JekyllSiteOptions options)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith(options.Compatibility.DestinationDirectoryName + "/", StringComparison.OrdinalIgnoreCase))
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

    private static bool IsSassFile(string path)
        => path.EndsWith(".scss", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".sass", StringComparison.OrdinalIgnoreCase);

    private static bool IsContentFile(string path)
        => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

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
}

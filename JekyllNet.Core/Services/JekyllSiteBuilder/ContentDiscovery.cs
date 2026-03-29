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
            if (ShouldSkip(relativePath, siteConfig, options) || !IsContentFile(file))
            {
                continue;
            }

            try
            {
                var text = await File.ReadAllTextAsync(file, cancellationToken);
                var document = _frontMatterParser.Parse(text);
                var frontMatter = ApplyFrontMatterDefaults(relativePath, document.FrontMatter, siteConfig, collectionDefinitions, options);
                var item = CreateContentItem(file, relativePath, frontMatter, document.Content, collectionDefinitions, siteConfig, options);
                if (ShouldIncludeItem(item, options))
                {
                    result.Add(item);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Failed to parse content file '{relativePath}'. {ex.Message}", ex);
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
        IReadOnlyDictionary<string, object?> siteConfig,
        JekyllSiteOptions options)
    {
        var isDraft = relativePath.StartsWith("_drafts/", StringComparison.OrdinalIgnoreCase);
        var isPost = isDraft || relativePath.StartsWith(options.Compatibility.PostsDirectoryName + "/", StringComparison.OrdinalIgnoreCase);
        var collection = ResolveCollectionName(relativePath, isPost, collections);
        var date = ResolveDate(relativePath, frontMatter, isPost, isDraft);
        var tags = ReadStringList(frontMatter, "tags");
        var categories = ReadStringList(frontMatter, "categories");
        var url = ResolvePermalink(relativePath, frontMatter, date, collection, tags, categories, isPost, siteConfig, options);

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

}

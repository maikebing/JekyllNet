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
    private Dictionary<string, object?> BuildVariables(JekyllSiteContext context, JekyllContentItem item, string content)
    {
        var page = new Dictionary<string, object?>(item.FrontMatter, StringComparer.OrdinalIgnoreCase)
        {
            ["content"] = content,
            ["excerpt"] = ResolveExcerptValue(item),
            ["path"] = item.RelativePath,
            ["url"] = item.Url,
            ["date"] = item.Date,
            ["collection"] = item.Collection,
            ["tags"] = item.Tags.Cast<object?>().ToList(),
            ["categories"] = item.Categories.Cast<object?>().ToList()
        };

        if (item.Paginator is not null)
        {
            page["paginator"] = item.Paginator;
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = page,
            ["site"] = context.SiteConfig,
            ["content"] = content,
            ["paginator"] = item.Paginator
        };
    }

    private Dictionary<string, object?> BuildSiteVariables(
        Dictionary<string, object?> siteConfig,
        Dictionary<string, object?> data,
        List<JekyllContentItem> posts,
        Dictionary<string, List<JekyllContentItem>> collections,
        Dictionary<string, List<JekyllContentItem>> tags,
        Dictionary<string, List<JekyllContentItem>> categories,
        List<JekyllStaticFile> staticFiles,
        JekyllSiteOptions options)
    {
        var result = new Dictionary<string, object?>(siteConfig, StringComparer.OrdinalIgnoreCase)
        {
            ["data"] = data,
            ["posts"] = posts.Select(item => ToLiquidObject(item, ShouldShowExcerpts(siteConfig))).Cast<object?>().ToList(),
            ["static_files"] = staticFiles.Select(ToLiquidObject).Cast<object?>().ToList(),
            ["collections"] = collections.ToDictionary(
                x => x.Key,
                x => x.Value.Select(item => ToLiquidObject(item, ShouldShowExcerpts(siteConfig))).Cast<object?>().ToList(),
                StringComparer.OrdinalIgnoreCase),
            ["tags"] = tags.ToDictionary(
                x => x.Key,
                x => x.Value.Select(item => ToLiquidObject(item, ShouldShowExcerpts(siteConfig))).Cast<object?>().ToList(),
                StringComparer.OrdinalIgnoreCase),
            ["categories"] = categories.ToDictionary(
                x => x.Key,
                x => x.Value.Select(item => ToLiquidObject(item, ShouldShowExcerpts(siteConfig))).Cast<object?>().ToList(),
                StringComparer.OrdinalIgnoreCase),
            ["github_pages"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["enabled"] = options.Compatibility.Enabled,
                ["plugins"] = options.Compatibility.WhitelistedPlugins.Cast<object?>().ToList(),
                ["source"] = options.SourceDirectory,
                ["destination"] = options.DestinationDirectory
            }
        };

        var bibliographyEntries = LoadBibliographyEntries(options.SourceDirectory);
        if (bibliographyEntries.Count > 0)
        {
            result["bibliography_entries"] = bibliographyEntries.Cast<object?>().ToList();
            result["bibliography_by_key"] = bibliographyEntries
                .Where(entry => entry.TryGetValue("key", out var key) && !string.IsNullOrWhiteSpace(key?.ToString()))
                .ToDictionary(
                    entry => entry["key"]!.ToString()!,
                    entry => (object?)entry,
                    StringComparer.OrdinalIgnoreCase);
        }

        var footer = BuildFooterSiteObject(siteConfig);
        if (footer.Count > 0)
        {
            result["footer"] = footer;
        }

        var analytics = BuildAnalyticsSiteObject(siteConfig);
        if (analytics.Count > 0)
        {
            result["analytics"] = analytics;
        }

        return result;
    }

}

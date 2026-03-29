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
    private string ApplyLayout(
        JekyllContentItem item,
        string content,
        IReadOnlyDictionary<string, string> layouts,
        IReadOnlyDictionary<string, string> includes,
        Dictionary<string, object?> variables)
    {
        var renderedContent = _templateRenderer.Render(content, variables, includes);
        var pageContent = item.SourcePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? renderedContent
            : ShouldPreserveRenderedHtml(renderedContent)
                ? renderedContent
                : Markdown.ToHtml(renderedContent, MarkdownPipeline);
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

}

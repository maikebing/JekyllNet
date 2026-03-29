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
    private void PrepareContentItems(IEnumerable<JekyllContentItem> items, MarkdownPipeline markdownPipeline, IReadOnlyDictionary<string, object?> siteConfig)
    {
        foreach (var item in items)
        {
            item.RenderedContent = item.SourcePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                ? item.RawContent
                : Markdown.ToHtml(item.RawContent, markdownPipeline);
            item.Excerpt = BuildExcerpt(item, markdownPipeline, siteConfig);
        }
    }

    private async Task<List<JekyllStaticFile>> DiscoverStaticFilesAsync(
        string sourceDirectory,
        IReadOnlyList<string> inheritedThemeDirectories,
        Dictionary<string, object?> siteConfig,
        IReadOnlyCollection<JekyllContentItem> items,
        JekyllSiteOptions options,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, JekyllStaticFile>(StringComparer.OrdinalIgnoreCase);
        var renderedContentPaths = items.Select(x => x.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var collectionDefinitions = ReadCollectionDefinitions(siteConfig, options);

        foreach (var themeDirectory in inheritedThemeDirectories)
        {
            await AddStaticFilesFromDirectoryAsync(
                themeDirectory,
                includeAllFiles: false,
                renderedContentPaths,
                collectionDefinitions,
                siteConfig,
                options,
                cancellationToken,
                result);
        }

        await AddStaticFilesFromDirectoryAsync(
            sourceDirectory,
            includeAllFiles: true,
            renderedContentPaths,
            collectionDefinitions,
            siteConfig,
            options,
            cancellationToken,
            result);

        return result.Values.ToList();
    }

    private async Task AddStaticFilesFromDirectoryAsync(
        string rootDirectory,
        bool includeAllFiles,
        IReadOnlySet<string> renderedContentPaths,
        HashSet<string> collectionDefinitions,
        Dictionary<string, object?> siteConfig,
        JekyllSiteOptions options,
        CancellationToken cancellationToken,
        Dictionary<string, JekyllStaticFile> result)
    {
        foreach (var file in EnumerateStaticCandidateFiles(rootDirectory, includeAllFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
            if (ShouldSkip(relativePath, siteConfig, options)
                || ShouldSkipStaticFile(relativePath, collectionDefinitions, options)
                || renderedContentPaths.Contains(relativePath)
                || IsSassFile(file))
            {
                continue;
            }

            var hasFrontMatter = false;
            var content = string.Empty;
            Dictionary<string, object?> frontMatter;

            try
            {
                if (IsTextStaticFile(file))
                {
                    var text = await File.ReadAllTextAsync(file, cancellationToken);
                    var document = _frontMatterParser.Parse(text);
                    hasFrontMatter = document.HasFrontMatter;
                    content = hasFrontMatter ? document.Content : text;
                    frontMatter = ApplyFrontMatterDefaults(relativePath, document.FrontMatter, siteConfig, collectionDefinitions, options);
                }
                else
                {
                    frontMatter = ApplyFrontMatterDefaults(relativePath, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase), siteConfig, collectionDefinitions, options);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Failed to inspect static file '{relativePath}'. {ex.Message}", ex);
            }

            result[relativePath] = new JekyllStaticFile
            {
                SourcePath = file,
                RelativePath = relativePath,
                OutputRelativePath = hasFrontMatter
                    && frontMatter.TryGetValue("permalink", out var permalinkValue)
                    && !string.IsNullOrWhiteSpace(permalinkValue?.ToString())
                    ? UrlToOutputPath(ResolvePermalink(
                        relativePath,
                        frontMatter,
                        date: null,
                        collection: string.Empty,
                        tags: [],
                        categories: [],
                        isPost: false,
                        siteConfig,
                        options))
                    : relativePath,
                Url = hasFrontMatter
                    && frontMatter.TryGetValue("permalink", out var staticPermalinkValue)
                    && !string.IsNullOrWhiteSpace(staticPermalinkValue?.ToString())
                    ? ResolvePermalink(
                        relativePath,
                        frontMatter,
                        date: null,
                        collection: string.Empty,
                        tags: [],
                        categories: [],
                        isPost: false,
                        siteConfig,
                        options)
                    : "/" + relativePath.Replace('\\', '/'),
                Content = content,
                FrontMatter = frontMatter,
                HasFrontMatter = hasFrontMatter
            };
        }
    }

    private async Task CopyStaticFilesAsync(
        string destinationDirectory,
        IReadOnlyCollection<JekyllStaticFile> staticFiles,
        JekyllSiteContext context,
        JekyllSiteOptions options,
        CancellationToken cancellationToken)
    {
        LogInfo(options, $"Copying {staticFiles.Count} static file(s).");
        foreach (var file in staticFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = Path.Combine(destinationDirectory, file.OutputRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            LogInfo(options, $"Copying {NormalizeLogPath(file.RelativePath)} -> {NormalizeLogPath(file.OutputRelativePath)}", verboseOnly: true);

            try
            {
                if (file.HasFrontMatter && IsTextStaticFile(file.SourcePath))
                {
                    var page = new Dictionary<string, object?>(file.FrontMatter, StringComparer.OrdinalIgnoreCase)
                    {
                        ["path"] = file.RelativePath,
                        ["url"] = file.Url
                    };
                    var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["page"] = page,
                        ["site"] = context.SiteConfig,
                        ["content"] = file.Content
                    };
                    var rendered = _templateRenderer.Render(file.Content, variables, context.Includes);
                    rendered = ApplyAutomaticSiteEnhancements(rendered, file.OutputRelativePath, context.SiteConfig, page);

                    if (IsCssFile(file.OutputRelativePath))
                    {
                        rendered = CompileFrontMatterCss(file, rendered);
                    }

                    await File.WriteAllTextAsync(destinationPath, rendered, cancellationToken);
                    continue;
                }

                File.Copy(file.SourcePath, destinationPath, overwrite: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Failed to copy static file '{file.RelativePath}'. {ex.Message}", ex);
            }
        }
    }

    private static IReadOnlyList<string> ResolveInheritedThemeDirectories(string sourceDirectory)
    {
        var result = new List<string>();
        var current = Directory.GetParent(Path.GetFullPath(sourceDirectory));

        while (current is not null)
        {
            if (DirectoryContainsThemeArtifacts(current.FullName))
            {
                result.Add(current.FullName);
            }

            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
            {
                break;
            }

            current = current.Parent;
        }

        result.Reverse();
        return result;
    }

    private static bool DirectoryContainsThemeArtifacts(string directory)
        => Directory.Exists(Path.Combine(directory, "_layouts"))
           || Directory.Exists(Path.Combine(directory, "_includes"))
           || Directory.Exists(Path.Combine(directory, "_data"))
           || Directory.Exists(Path.Combine(directory, "_sass"))
           || Directory.Exists(Path.Combine(directory, "assets"));

    private static IEnumerable<string> EnumerateStaticCandidateFiles(string rootDirectory, bool includeAllFiles)
    {
        if (includeAllFiles)
        {
            return Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories);
        }

        var assetsDirectory = Path.Combine(rootDirectory, "assets");
        return Directory.Exists(assetsDirectory)
            ? Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories)
            : [];
    }

    private static IEnumerable<string> EnumerateSassEntryFiles(
        string rootDirectory,
        bool includeAllFiles,
        IReadOnlyDictionary<string, object?> siteConfig,
        JekyllSiteOptions options)
    {
        return EnumerateStaticCandidateFiles(rootDirectory, includeAllFiles)
            .Where(IsSassFile)
            .Where(file =>
            {
                var relative = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
                var fileName = Path.GetFileName(relative);
                return !fileName.StartsWith("_", StringComparison.Ordinal)
                    && !ShouldSkip(relative, siteConfig, options)
                    && HasFrontMatter(file);
            });
    }

    private static bool HasFrontMatter(string path)
    {
        using var reader = new StreamReader(path);
        var firstLine = reader.ReadLine();
        if (!string.Equals(firstLine, "---", StringComparison.Ordinal))
        {
            return false;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal) || string.Equals(line, "...", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
    private static bool ShouldSkipStaticFile(string relativePath, HashSet<string> collectionDefinitions, JekyllSiteOptions options)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0)
        {
            return false;
        }

        var fileName = Path.GetFileName(normalized);
        if (fileName.StartsWith("_config", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var firstSegment = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstSegment))
        {
            return false;
        }

        if (string.Equals(firstSegment, "_drafts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(firstSegment, options.Compatibility.PostsDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return firstSegment.StartsWith('_')
            && collectionDefinitions.Contains(firstSegment[1..]);
    }

    private static void EnsureSassEngineRegistered()
    {
        if (Interlocked.Exchange(ref _sassEngineRegistered, 1) == 1)
        {
            return;
        }

        var switcher = JsEngineSwitcher.Current;
        if (!switcher.EngineFactories.Any(factory => string.Equals(factory.EngineName, JintJsEngine.EngineName, StringComparison.OrdinalIgnoreCase)))
        {
            switcher.EngineFactories.AddJint();
        }

        if (string.IsNullOrWhiteSpace(switcher.DefaultEngineName))
        {
            switcher.DefaultEngineName = JintJsEngine.EngineName;
        }
    }

}

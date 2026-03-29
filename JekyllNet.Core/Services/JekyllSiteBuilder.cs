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
    private readonly FrontMatterParser _frontMatterParser = new();
    private readonly TemplateRenderer _templateRenderer = new();
    private readonly IDeserializer _yamlDeserializer = new DeserializerBuilder().Build();
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    private static int _sassEngineRegistered;

    public async Task BuildAsync(JekyllSiteOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DestinationDirectory);

        if (!Directory.Exists(options.SourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {options.SourceDirectory}");
        }

        LogInfo(options, $"Starting build: {options.SourceDirectory} -> {options.DestinationDirectory}");

        if (Directory.Exists(options.DestinationDirectory))
        {
            Directory.Delete(options.DestinationDirectory, recursive: true);
        }

        Directory.CreateDirectory(options.DestinationDirectory);

        var siteConfig = await LoadConfigAsync(options.SourceDirectory, cancellationToken);
        var inheritedThemeDirectories = ResolveInheritedThemeDirectories(options.SourceDirectory);
        var templateDirectories = inheritedThemeDirectories.Concat([options.SourceDirectory]).ToArray();
        var data = await LoadDataAsync(templateDirectories, options, cancellationToken);
        var layouts = await LoadNamedTemplatesAsync(templateDirectories, options.Compatibility.LayoutsDirectoryName, cancellationToken);
        var includes = await LoadNamedTemplatesAsync(templateDirectories, options.Compatibility.IncludesDirectoryName, cancellationToken);
        LogInfo(options, $"Loaded site configuration, {layouts.Count} layout entries, {includes.Count} include entries, and {data.Count} top-level data entries.");
        if (inheritedThemeDirectories.Count > 0)
        {
            LogInfo(options, $"Using {inheritedThemeDirectories.Count} inherited theme directory(s).", verboseOnly: true);
        }

        // Load plugins from _plugins/ directory (both .cs and .rb files supported).
        var pluginsDir = Path.Combine(options.SourceDirectory, "_plugins");
        var pluginDiagnostics = new List<string>();
        var pluginRegistry = JekyllPluginLoader.Load(pluginsDir, pluginDiagnostics);
        foreach (var diag in pluginDiagnostics)
            LogInfo(options, diag, verboseOnly: true);
        _templateRenderer.PluginRegistry = pluginRegistry;
        _templateRenderer.SourceDirectory = options.SourceDirectory;

        var items = await DiscoverContentItemsAsync(options.SourceDirectory, siteConfig, options, cancellationToken);
        IAiTranslationClient? aiTranslationClient = options.AiTranslationClient;
        OpenAiCompatibleTranslationClient? ownedAiTranslationClient = null;
        AiTranslationCacheStore? translationCache = null;
        try
        {
            var aiTranslationSettings = await ResolveAiTranslationSettingsAsync(options.SourceDirectory, siteConfig, cancellationToken);
            if (aiTranslationSettings is not null)
            {
                translationCache = await AiTranslationCacheStore.LoadAsync(aiTranslationSettings.CachePath, cancellationToken);
                if (aiTranslationClient is null)
                {
                    ownedAiTranslationClient = CreateAiTranslationClient(aiTranslationSettings);
                    aiTranslationClient = ownedAiTranslationClient;
                }

                var translatedItems = await TranslateContentItemsAsync(items, siteConfig, aiTranslationSettings, aiTranslationClient, translationCache, cancellationToken);
                items.AddRange(translatedItems);

                var translatedFooterLabels = await TranslateFooterLabelsAsync(aiTranslationSettings, aiTranslationClient, translationCache, cancellationToken);
                if (translatedFooterLabels.Count > 0)
                {
                    siteConfig["_auto_footer_labels"] = translatedFooterLabels;
                }

                await translationCache.SaveAsync(cancellationToken);
            }

            ApplyTranslationLinks(items, siteConfig);
        }
        finally
        {
            ownedAiTranslationClient?.Dispose();
        }

        LogInfo(options, $"Discovered {items.Count} content item(s).");
        PrepareContentItems(items, MarkdownPipeline, siteConfig);
        var posts = items.Where(x => x.IsPost).OrderByDescending(x => x.Date).ToList();
        var paginatedItems = CreatePaginationItems(items, posts, siteConfig, options);
        items.AddRange(paginatedItems);
        var staticFiles = await DiscoverStaticFilesAsync(options.SourceDirectory, inheritedThemeDirectories, siteConfig, items, options, cancellationToken);
        LogInfo(options, $"Discovered {staticFiles.Count} static file(s).");
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
            SiteConfig = BuildSiteVariables(siteConfig, data, posts, collections, tags, categories, staticFiles, options),
            Layouts = layouts,
            Includes = includes,
            Posts = posts,
            Collections = collections,
            StaticFiles = staticFiles,
            Compatibility = options.Compatibility
        };

        // Run IJekyllGenerator plugins; they may append items to context.ExtraItems.
        foreach (var generator in pluginRegistry.Generators)
            await generator.GenerateAsync(context, cancellationToken);
        items.AddRange(context.ExtraItems);

        LogInfo(options, $"Rendering {items.Count} page(s).");
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogInfo(options, $"Rendering {NormalizeLogPath(item.RelativePath)} -> {NormalizeLogPath(item.OutputRelativePath)}", verboseOnly: true);

            try
            {
                var sourceContent = item.SourcePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    ? item.RenderedContent
                    : item.RawContent;
                var variables = BuildVariables(context, item, sourceContent);
                var rendered = ApplyLayout(item, sourceContent, context.Layouts, context.Includes, variables);
                rendered = ApplyAutomaticSiteEnhancements(rendered, item.OutputRelativePath, siteConfig, item.FrontMatter);

                var destinationPath = Path.Combine(options.DestinationDirectory, item.OutputRelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await File.WriteAllTextAsync(destinationPath, rendered, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Failed to render content item '{item.RelativePath}'. {ex.Message}", ex);
            }
        }

        await CompileSassAsync(
            options.SourceDirectory,
            inheritedThemeDirectories,
            options.DestinationDirectory,
            options,
            context.SiteConfig,
            context.Includes,
            cancellationToken);
        await CopyStaticFilesAsync(options.DestinationDirectory, staticFiles, context, options, cancellationToken);

        LogInfo(options, $"Finished build: {items.Count} page(s), {staticFiles.Count} static file(s), destination {options.DestinationDirectory}");
    }

}

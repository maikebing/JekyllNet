using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DartSassHost;
using Markdig;
using JekyllNet.Core.Models;
using JekyllNet.Core.Parsers;
using JekyllNet.Core.Rendering;
using JekyllNet.Core.Translation;
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

        PrepareContentItems(items, markdownPipeline, siteConfig);
        var posts = items.Where(x => x.IsPost).OrderByDescending(x => x.Date).ToList();
        var paginatedItems = CreatePaginationItems(items, posts, siteConfig, options);
        items.AddRange(paginatedItems);
        var staticFiles = await DiscoverStaticFilesAsync(options.SourceDirectory, siteConfig, items, options, cancellationToken);
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

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var variables = BuildVariables(context, item, item.RenderedContent);
            var rendered = ApplyLayout(item, item.RenderedContent, context.Layouts, context.Includes, variables);
            rendered = ApplyAutomaticSiteEnhancements(rendered, item.OutputRelativePath, siteConfig, item.FrontMatter);

            var destinationPath = Path.Combine(options.DestinationDirectory, item.OutputRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(destinationPath, rendered, cancellationToken);
        }

        await CompileSassAsync(options.SourceDirectory, options.DestinationDirectory, options, cancellationToken);
        await CopyStaticFilesAsync(options.DestinationDirectory, staticFiles, context, cancellationToken);
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
            if (ShouldSkip(relativePath, siteConfig, options) || !IsContentFile(file))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var document = _frontMatterParser.Parse(text);
            var frontMatter = ApplyFrontMatterDefaults(relativePath, document.FrontMatter, siteConfig, collectionDefinitions, options);
            var item = CreateContentItem(file, relativePath, frontMatter, document.Content, collectionDefinitions, siteConfig, options);
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
        IReadOnlyDictionary<string, object?> siteConfig,
        JekyllSiteOptions options)
    {
        var isDraft = relativePath.StartsWith("_drafts/", StringComparison.OrdinalIgnoreCase);
        var isPost = isDraft || relativePath.StartsWith(options.Compatibility.PostsDirectoryName + "/", StringComparison.OrdinalIgnoreCase);
        var collection = ResolveCollectionName(relativePath, isPost, collections);
        var date = ResolveDate(relativePath, frontMatter, isPost, isDraft);
        var url = ResolvePermalink(relativePath, frontMatter, date, collection, isPost, siteConfig);
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
            ["excerpt"] = item.Excerpt,
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

    private async Task<List<JekyllContentItem>> TranslateContentItemsAsync(
        IReadOnlyCollection<JekyllContentItem> items,
        IReadOnlyDictionary<string, object?> siteConfig,
        AiTranslationSettings settings,
        IAiTranslationClient translationClient,
        AiTranslationCacheStore? translationCache,
        CancellationToken cancellationToken)
    {
        var result = new List<JekyllContentItem>();

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ShouldAutoTranslateItem(item))
            {
                continue;
            }

            var sourceLanguage = ResolveItemLanguage(item.FrontMatter, siteConfig);
            foreach (var targetLanguage in settings.TargetLanguages)
            {
                if (string.Equals(NormalizeLanguageCode(sourceLanguage), NormalizeLanguageCode(targetLanguage), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(await TranslateContentItemAsync(item, sourceLanguage, targetLanguage, settings, translationClient, translationCache, cancellationToken));
            }
        }

        return result;
    }

    private async Task<JekyllContentItem> TranslateContentItemAsync(
        JekyllContentItem item,
        string sourceLanguage,
        string targetLanguage,
        AiTranslationSettings settings,
        IAiTranslationClient translationClient,
        AiTranslationCacheStore? translationCache,
        CancellationToken cancellationToken)
    {
        var translatedFrontMatter = new Dictionary<string, object?>(item.FrontMatter, StringComparer.OrdinalIgnoreCase)
        {
            ["lang"] = targetLanguage,
            ["translated"] = true,
            ["translation_source_lang"] = sourceLanguage,
            ["translation_source_url"] = item.Url
        };

        if (IsEnglishLanguage(targetLanguage))
        {
            translatedFrontMatter["is_en"] = true;
        }
        else
        {
            translatedFrontMatter.Remove("is_en");
        }

        foreach (var field in settings.FrontMatterKeys)
        {
            if (!translatedFrontMatter.TryGetValue(field, out var fieldValue) || string.IsNullOrWhiteSpace(fieldValue?.ToString()))
            {
                continue;
            }

            translatedFrontMatter[field] = await TranslateTextAsync(
                settings,
                translationClient,
                translationCache,
                sourceLanguage,
                targetLanguage,
                fieldValue!.ToString()!,
                AiTextKind.Text,
                cancellationToken);
        }

        var translatedContent = await TranslateTextAsync(
            settings,
            translationClient,
            translationCache,
            sourceLanguage,
            targetLanguage,
            item.RawContent,
            AiTextKind.Markdown,
            cancellationToken);

        var translatedUrl = BuildTranslatedUrl(item.Url, sourceLanguage, targetLanguage);
        translatedFrontMatter["permalink"] = translatedUrl;

        return new JekyllContentItem
        {
            SourcePath = item.SourcePath,
            RelativePath = BuildTranslatedRelativePath(item.RelativePath, sourceLanguage, targetLanguage),
            OutputRelativePath = UrlToOutputPath(translatedUrl),
            Url = translatedUrl,
            Collection = item.Collection,
            IsPost = item.IsPost,
            IsDraft = item.IsDraft,
            Date = item.Date,
            Tags = [.. item.Tags],
            Categories = [.. item.Categories],
            FrontMatter = translatedFrontMatter,
            RawContent = translatedContent
        };
    }

    private async Task<Dictionary<string, object?>> TranslateFooterLabelsAsync(
        AiTranslationSettings settings,
        IAiTranslationClient translationClient,
        AiTranslationCacheStore? translationCache,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var targetLanguage in settings.TargetLanguages.Where(static language => !IsEnglishLanguage(language) && !language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)))
        {
            var labels = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["icp"] = await TranslateTextAsync(settings, translationClient, translationCache, "en", targetLanguage, "ICP Filing No.", AiTextKind.Label, cancellationToken),
                ["public_security"] = await TranslateTextAsync(settings, translationClient, translationCache, "en", targetLanguage, "Public Security Filing No.", AiTextKind.Label, cancellationToken),
                ["telecom_license"] = await TranslateTextAsync(settings, translationClient, translationCache, "en", targetLanguage, "Value-added Telecom License", AiTextKind.Label, cancellationToken),
                ["terms"] = await TranslateTextAsync(settings, translationClient, translationCache, "en", targetLanguage, "Terms of Service", AiTextKind.Label, cancellationToken),
                ["privacy"] = await TranslateTextAsync(settings, translationClient, translationCache, "en", targetLanguage, "Privacy Policy", AiTextKind.Label, cancellationToken),
                ["report_phone"] = await TranslateTextAsync(settings, translationClient, translationCache, "en", targetLanguage, "Report Phone", AiTextKind.Label, cancellationToken),
                ["report_email"] = await TranslateTextAsync(settings, translationClient, translationCache, "en", targetLanguage, "Report Email", AiTextKind.Label, cancellationToken)
            };

            result[NormalizeLanguageCode(targetLanguage)] = labels;
        }

        return result;
    }

    private static void ApplyTranslationLinks(
        IReadOnlyCollection<JekyllContentItem> items,
        IReadOnlyDictionary<string, object?> siteConfig)
    {
        var locales = ReadLocales(siteConfig);
        foreach (var item in items)
        {
            item.FrontMatter["locale_code"] = ResolveLocaleCode(item.FrontMatter, siteConfig);
        }

        var groups = items
            .GroupBy(item => BuildTranslationGroupKey(item, locales, siteConfig), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => ResolveLocaleCode(item.FrontMatter, siteConfig)).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

        foreach (var group in groups)
        {
            var links = group
                .GroupBy(item => ResolveLocaleCode(item.FrontMatter, siteConfig), StringComparer.OrdinalIgnoreCase)
                .Select(localeGroup =>
                {
                    var localeCode = localeGroup.Key;
                    var selectedItem = localeGroup
                        .OrderByDescending(item => IsCanonicalLocaleUrl(item.Url, localeCode, locales))
                        .ThenBy(item => item.Url.Length)
                        .First();

                    return new TranslationLink(
                        localeCode,
                        ReadLocaleLabel(localeCode, locales),
                        selectedItem.Url);
                })
                .OrderBy(link => GetLocaleOrder(link.Code, locales))
                .ToList();

            foreach (var item in group)
            {
                var currentCode = ResolveLocaleCode(item.FrontMatter, siteConfig);
                var relatedLinks = links
                    .Where(link => !string.Equals(link.Code, currentCode, StringComparison.OrdinalIgnoreCase))
                    .Select(link => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["code"] = link.Code,
                        ["label"] = link.Label,
                        ["url"] = link.Url
                    })
                    .Cast<object?>()
                    .ToList();

                if (relatedLinks.Count > 0)
                {
                    item.FrontMatter["translation_links"] = relatedLinks;
                    if (relatedLinks.Count == 1 && relatedLinks[0] is Dictionary<string, object?> onlyLink)
                    {
                        item.FrontMatter["translation_url"] = onlyLink["url"];
                        item.FrontMatter["translation_target_label"] = onlyLink["label"];
                    }
                }
            }
        }
    }

    private Dictionary<string, object?> ToLiquidObject(JekyllContentItem item)
        => ToLiquidObject(item, includeExcerpt: true);

    private Dictionary<string, object?> ToLiquidObject(JekyllContentItem item, bool includeExcerpt)
    {
        var result = new Dictionary<string, object?>(item.FrontMatter, StringComparer.OrdinalIgnoreCase)
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

        if (includeExcerpt)
        {
            result["excerpt"] = item.Excerpt;
        }

        return result;
    }

    private Dictionary<string, object?> ToLiquidObject(JekyllStaticFile file)
        => new(file.FrontMatter, StringComparer.OrdinalIgnoreCase)
        {
            ["path"] = file.RelativePath,
            ["url"] = file.Url,
            ["name"] = Path.GetFileName(file.RelativePath),
            ["extname"] = Path.GetExtension(file.RelativePath),
            ["basename"] = Path.GetFileNameWithoutExtension(file.RelativePath)
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

    private static string ApplyAutomaticSiteEnhancements(
        string html,
        string outputRelativePath,
        IReadOnlyDictionary<string, object?> siteConfig,
        IReadOnlyDictionary<string, object?>? pageData)
    {
        if (!IsHtmlOutputPath(outputRelativePath) || string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        var footerHtml = BuildAutomaticFooterHtml(siteConfig, pageData);
        var analyticsHtml = BuildAnalyticsHtml(siteConfig);
        if (string.IsNullOrWhiteSpace(footerHtml) && string.IsNullOrWhiteSpace(analyticsHtml))
        {
            return html;
        }

        var combined = string.Concat(
            string.IsNullOrWhiteSpace(footerHtml) ? string.Empty : Environment.NewLine + footerHtml,
            string.IsNullOrWhiteSpace(analyticsHtml) ? string.Empty : Environment.NewLine + analyticsHtml,
            Environment.NewLine);

        return InsertBeforeBodyEnd(html, combined);
    }

    private static string BuildAutomaticFooterHtml(
        IReadOnlyDictionary<string, object?> siteConfig,
        IReadOnlyDictionary<string, object?>? pageData)
    {
        var labels = ResolveFooterLabels(siteConfig, pageData);
        var icpLabel = ReadConfigString(siteConfig, "footer.icp_label", "footer.beian_label") ?? labels.IcpLabel;
        var publicSecurityLabel = ReadConfigString(siteConfig, "footer.public_security_label", "footer.public_security_beian_label", "footer.gongan_beian_label") ?? labels.PublicSecurityLabel;
        var telecomLicenseLabel = ReadConfigString(siteConfig, "footer.telecom_license_label", "footer.value_added_telecom_license_label") ?? labels.TelecomLicenseLabel;
        var reportPhoneLabel = ReadConfigString(siteConfig, "footer.report_phone_label") ?? labels.ReportPhoneLabel;
        var reportEmailLabel = ReadConfigString(siteConfig, "footer.report_email_label") ?? labels.ReportEmailLabel;
        var lineOneSegments = new List<string>();
        var lineTwoSegments = new List<string>();

        var copyright = ReadConfigString(siteConfig, "footer.copyright", "copyright");
        if (!string.IsNullOrWhiteSpace(copyright))
        {
            lineOneSegments.Add(HtmlEncode(copyright));
        }

        var icp = ReadConfigString(siteConfig, "footer.icp", "footer.beian", "icp", "\u5907\u6848\u53f7");
        if (!string.IsNullOrWhiteSpace(icp))
        {
            lineOneSegments.Add(BuildExternalLink("https://beian.miit.gov.cn/", PrefixLabeledValue(icp, icpLabel, labels.ValueSeparator)));
        }

        var publicSecurityBeian = ReadConfigString(
            siteConfig,
            "footer.public_security_beian",
            "footer.gongan_beian",
            "footer.police_beian",
            "public_security_beian",
            "gongan_beian",
            "police_beian",
            "\u516c\u5b89\u5907\u6848\u53f7");
        if (!string.IsNullOrWhiteSpace(publicSecurityBeian))
        {
            var publicSecurityUrl = BuildPublicSecurityRecordUrl(publicSecurityBeian);
            var publicSecurityText = PrefixLabeledValue(publicSecurityBeian, publicSecurityLabel, labels.ValueSeparator);
            lineOneSegments.Add(string.IsNullOrWhiteSpace(publicSecurityUrl)
                ? HtmlEncode(publicSecurityText)
                : BuildExternalLink(publicSecurityUrl, publicSecurityText));
        }

        var telecomLicense = ReadConfigString(
            siteConfig,
            "footer.telecom_license",
            "footer.value_added_telecom_license",
            "telecom_license",
            "value_added_telecom_license",
            "\u589e\u503c\u7535\u4fe1\u4e1a\u52a1\u7ecf\u8425\u8bb8\u53ef\u8bc1");
        if (!string.IsNullOrWhiteSpace(telecomLicense))
        {
            lineOneSegments.Add(HtmlEncode(PrefixLabeledValue(telecomLicense, telecomLicenseLabel, labels.ValueSeparator)));
        }

        var termsLink = BuildConfiguredLink(
            siteConfig,
            ReadConfigString(siteConfig, "footer.terms_label", "footer.service_terms_label") ?? labels.TermsLabel,
            "footer.terms_url",
            "footer.service_terms_url",
            "terms_url",
            "service_terms_url");
        if (!string.IsNullOrWhiteSpace(termsLink))
        {
            lineOneSegments.Add(termsLink);
        }

        var privacyLink = BuildConfiguredLink(
            siteConfig,
            ReadConfigString(siteConfig, "footer.privacy_label", "footer.privacy_policy_label") ?? labels.PrivacyLabel,
            "footer.privacy_url",
            "footer.privacy_policy_url",
            "privacy_url",
            "privacy_policy_url");
        if (!string.IsNullOrWhiteSpace(privacyLink))
        {
            lineOneSegments.Add(privacyLink);
        }

        var reportPhone = ReadConfigString(
            siteConfig,
            "footer.report_phone",
            "report_phone",
            "\u8fdd\u6cd5\u548c\u4e0d\u826f\u4e3e\u62a5\u7535\u8bdd");
        if (!string.IsNullOrWhiteSpace(reportPhone))
        {
            lineTwoSegments.Add(HtmlEncode(FormatLabeledValue(reportPhoneLabel, reportPhone, labels.ValueSeparator)));
        }

        var reportEmail = ReadConfigString(
            siteConfig,
            "footer.report_email",
            "report_email",
            "\u4e3e\u62a5\u90ae\u7bb1");
        if (!string.IsNullOrWhiteSpace(reportEmail))
        {
            lineTwoSegments.Add($"{HtmlEncode(FormatLabeledValue(reportEmailLabel, string.Empty, labels.ValueSeparator))}<a href=\"mailto:{HtmlEncode(reportEmail)}\">{HtmlEncode(reportEmail)}</a>");
        }

        if (lineOneSegments.Count == 0 && lineTwoSegments.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>();
        if (lineOneSegments.Count > 0)
        {
            lines.Add($"<p>{string.Join(" | ", lineOneSegments)}</p>");
        }

        if (lineTwoSegments.Count > 0)
        {
            lines.Add($"<p>{string.Join(" | ", lineTwoSegments)}</p>");
        }

        return $"""
<footer class="jekyllnet-site-footer" data-jekyllnet-auto-footer="true">
  {string.Join(Environment.NewLine + "  ", lines)}
</footer>
""";
    }

private static string BuildAnalyticsHtml(IReadOnlyDictionary<string, object?> siteConfig)
    {
        var snippets = new List<string>();

        var googleAnalyticsId = ReadConfigString(
            siteConfig,
            "analytics.google",
            "analytics.google_analytics",
            "google_analytics",
            "google");
        if (!string.IsNullOrWhiteSpace(googleAnalyticsId))
        {
            var escapedId = EscapeJavaScriptString(googleAnalyticsId);
            snippets.Add($$"""
<script async src="https://www.googletagmanager.com/gtag/js?id={{HtmlEncode(googleAnalyticsId)}}"></script>
<script>
window.dataLayer = window.dataLayer || [];
function gtag(){dataLayer.push(arguments);}
gtag('js', new Date());
gtag('config', '{{escapedId}}');
</script>
""");
        }

        var baiduAnalyticsId = ReadConfigString(
            siteConfig,
            "analytics.baidu",
            "analytics.baidu_tongji",
            "baidu",
            "baidu_tongji");
        if (!string.IsNullOrWhiteSpace(baiduAnalyticsId))
        {
            snippets.Add($$"""
<script>
var _hmt = window._hmt || [];
window._hmt = _hmt;
(function() {
  var hm = document.createElement("script");
  hm.src = "https://hm.baidu.com/hm.js?{{EscapeJavaScriptString(baiduAnalyticsId)}}";
  hm.async = true;
  var s = document.getElementsByTagName("script")[0];
  s.parentNode.insertBefore(hm, s);
})();
</script>
""");
        }

        var cnzzAnalyticsId = ReadConfigString(
            siteConfig,
            "analytics.cnzz",
            "analytics.umeng",
            "cnzz",
            "umeng");
        if (!string.IsNullOrWhiteSpace(cnzzAnalyticsId))
        {
            var escapedId = EscapeJavaScriptString(cnzzAnalyticsId);
            snippets.Add($$"""
<script>
var _czc = window._czc || [];
window._czc = _czc;
_czc.push(["_setAccount", "{{escapedId}}"]);
(function() {
  var cnzz = document.createElement("script");
  cnzz.type = "text/javascript";
  cnzz.async = true;
  cnzz.charset = "utf-8";
  cnzz.src = "https://w.cnzz.com/c.php?id={{escapedId}}&async=1";
  var root = document.getElementsByTagName("script")[0];
  root.parentNode.insertBefore(cnzz, root);
})();
</script>
""");
        }

        var laConfig = Build51LaConfiguration(siteConfig);
        if (laConfig.Count > 0
            && laConfig.TryGetValue("id", out var laIdValue)
            && laIdValue?.ToString() is { Length: > 0 } laId)
        {
            var laCk = laConfig.TryGetValue("ck", out var laCkValue) && !string.IsNullOrWhiteSpace(laCkValue?.ToString())
                ? laCkValue!.ToString()!
                : laId;

            var options = new List<string>
            {
                $"id: \"{EscapeJavaScriptString(laId)}\"",
                $"ck: \"{EscapeJavaScriptString(laCk)}\""
            };

            if (laConfig.TryGetValue("autoTrack", out var autoTrackValue) && autoTrackValue is bool autoTrack)
            {
                options.Add($"autoTrack: {autoTrack.ToString().ToLowerInvariant()}");
            }

            if (laConfig.TryGetValue("hashMode", out var hashModeValue) && hashModeValue is bool hashMode)
            {
                options.Add($"hashMode: {hashMode.ToString().ToLowerInvariant()}");
            }

            if (laConfig.TryGetValue("screenRecord", out var screenRecordValue) && screenRecordValue is bool screenRecord)
            {
                options.Add($"screenRecord: {screenRecord.ToString().ToLowerInvariant()}");
            }

            snippets.Add($$"""
<script charset="UTF-8" id="LA_COLLECT" src="https://sdk.51.la/js-sdk-pro.min.js"></script>
<script>LA.init({ {{string.Join(", ", options)}} });</script>
""");
        }

        return string.Join(Environment.NewLine, snippets);
    }

    private static Dictionary<string, object?> BuildFooterSiteObject(IReadOnlyDictionary<string, object?> siteConfig)
    {
        var result = ReadConfigDictionary(siteConfig, "footer");

        SetIfPresent(result, "copyright", ReadConfigString(siteConfig, "footer.copyright", "copyright"));
        SetIfPresent(result, "icp", ReadConfigString(siteConfig, "footer.icp", "footer.beian", "icp", "\u5907\u6848\u53f7"));
        SetIfPresent(
            result,
            "public_security_beian",
            ReadConfigString(
                siteConfig,
                "footer.public_security_beian",
                "footer.gongan_beian",
                "footer.police_beian",
                "public_security_beian",
                "gongan_beian",
                "police_beian",
                "\u516c\u5b89\u5907\u6848\u53f7"));
        SetIfPresent(
            result,
            "telecom_license",
            ReadConfigString(
                siteConfig,
                "footer.telecom_license",
                "footer.value_added_telecom_license",
                "telecom_license",
                "value_added_telecom_license",
                "\u589e\u503c\u7535\u4fe1\u4e1a\u52a1\u7ecf\u8425\u8bb8\u53ef\u8bc1"));
        SetIfPresent(result, "terms_url", ReadConfigString(siteConfig, "footer.terms_url", "footer.service_terms_url", "terms_url", "service_terms_url"));
        SetIfPresent(result, "privacy_url", ReadConfigString(siteConfig, "footer.privacy_url", "footer.privacy_policy_url", "privacy_url", "privacy_policy_url"));
        SetIfPresent(result, "report_phone", ReadConfigString(siteConfig, "footer.report_phone", "report_phone", "\u8fdd\u6cd5\u548c\u4e0d\u826f\u4e3e\u62a5\u7535\u8bdd"));
        SetIfPresent(result, "report_email", ReadConfigString(siteConfig, "footer.report_email", "report_email", "\u4e3e\u62a5\u90ae\u7bb1"));

        return result;
    }

    private static Dictionary<string, object?> BuildAnalyticsSiteObject(IReadOnlyDictionary<string, object?> siteConfig)
    {
        var result = ReadConfigDictionary(siteConfig, "analytics");

        SetIfPresent(result, "google", ReadConfigString(siteConfig, "analytics.google", "analytics.google_analytics", "google_analytics", "google"));
        SetIfPresent(result, "baidu", ReadConfigString(siteConfig, "analytics.baidu", "analytics.baidu_tongji", "baidu", "baidu_tongji"));
        SetIfPresent(result, "cnzz", ReadConfigString(siteConfig, "analytics.cnzz", "analytics.umeng", "cnzz", "umeng"));

        var la = Build51LaConfiguration(siteConfig);
        if (la.Count > 0)
        {
            result["51la"] = la;
        }

        return result;
    }

    private static Dictionary<string, object?> Build51LaConfiguration(IReadOnlyDictionary<string, object?> siteConfig)
    {
        var analytics = ReadConfigDictionary(siteConfig, "analytics");
        var result = analytics.TryGetValue("51la", out var nested51La) && nested51La is Dictionary<string, object?> nestedDictionary
            ? new Dictionary<string, object?>(nestedDictionary, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var simpleValue = ReadConfigValue(siteConfig, "analytics.51la", "analytics.51_la", "51la", "51_la");
        if (simpleValue is Dictionary<string, object?> configDictionary)
        {
            foreach (var pair in configDictionary)
            {
                result[pair.Key] = pair.Value;
            }
        }
        else if (simpleValue?.ToString() is { Length: > 0 } simpleId)
        {
            result["id"] = simpleId;
            result["ck"] = simpleId;
        }

        SetIfPresent(result, "id", ReadConfigString(siteConfig, "analytics.51la.id", "analytics.51_la.id"));
        SetIfPresent(result, "ck", ReadConfigString(siteConfig, "analytics.51la.ck", "analytics.51_la.ck"));

        SetBooleanIfPresent(result, "autoTrack", ReadConfigBoolean(siteConfig, "analytics.51la.autoTrack", "analytics.51la.auto_track", "analytics.51_la.autoTrack", "analytics.51_la.auto_track"));
        SetBooleanIfPresent(result, "hashMode", ReadConfigBoolean(siteConfig, "analytics.51la.hashMode", "analytics.51la.hash_mode", "analytics.51_la.hashMode", "analytics.51_la.hash_mode"));
        SetBooleanIfPresent(result, "screenRecord", ReadConfigBoolean(siteConfig, "analytics.51la.screenRecord", "analytics.51la.screen_record", "analytics.51_la.screenRecord", "analytics.51_la.screen_record"));

        return result;
    }

    private static object? ReadConfigValue(IReadOnlyDictionary<string, object?> siteConfig, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryResolveObject(siteConfig, key, out var value) && value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? ReadConfigString(IReadOnlyDictionary<string, object?> siteConfig, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryResolveObject(siteConfig, key, out var value) || value is null)
            {
                continue;
            }

            var text = value.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool? ReadConfigBoolean(IReadOnlyDictionary<string, object?> siteConfig, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryResolveObject(siteConfig, key, out var value) || value is null)
            {
                continue;
            }

            var parsed = TryConvertToBoolean(value);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static Dictionary<string, object?> ReadConfigDictionary(IReadOnlyDictionary<string, object?> siteConfig, string key)
    {
        if (TryResolveObject(siteConfig, key, out var value) && value is Dictionary<string, object?> dictionary)
        {
            return new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<AiTranslationSettings?> ResolveAiTranslationSettingsAsync(
        string sourceDirectory,
        IReadOnlyDictionary<string, object?> siteConfig,
        CancellationToken cancellationToken)
    {
        if (!TryResolveObject(siteConfig, "ai", out _))
        {
            return null;
        }

        var targetLanguages = ReadConfigStringList(siteConfig, "ai.translate.targets");
        if (targetLanguages.Count == 0)
        {
            return null;
        }

        var provider = ReadConfigString(siteConfig, "ai.provider") ?? "openai";
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var model = ReadConfigString(siteConfig, "ai.model") ?? provider.ToLowerInvariant() switch
        {
            "deepseek" => "deepseek-chat",
            "ollama" => "qwen3:8b",
            _ => "gpt-5-mini"
        };
        var baseUrl = ReadConfigString(siteConfig, "ai.base_url");
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = normalizedProvider switch
            {
                "deepseek" => "https://api.deepseek.com",
                "ollama" => "http://localhost:11434",
                "openai" => "https://api.openai.com",
                "openai-compatible" => throw new InvalidOperationException("ai.base_url is required when ai.provider is openai-compatible."),
                "compatible" => throw new InvalidOperationException("ai.base_url is required when ai.provider is compatible."),
                _ => throw new InvalidOperationException($"Unknown AI provider '{provider}'. Configure ai.base_url for any OpenAI-compatible third-party provider.")
            };
        }
        var apiKeyEnv = ReadConfigString(siteConfig, "ai.api_key_env") ?? provider.ToLowerInvariant() switch
        {
            "deepseek" => "DEEPSEEK_API_KEY",
            "ollama" => string.Empty,
            _ => "OPENAI_API_KEY"
        };
        var apiKey = ReadConfigString(siteConfig, "ai.api_key");
        if (string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(apiKeyEnv))
        {
            apiKey = Environment.GetEnvironmentVariable(apiKeyEnv);
        }

        var cacheEnabled = ReadConfigBoolean(siteConfig, "ai.translate.cache") ?? true;
        var configuredCachePath = ReadConfigString(siteConfig, "ai.translate.cache_path") ?? ".jekyllnet/translation-cache.json";
        var glossary = await LoadAiTranslationGlossaryAsync(sourceDirectory, ReadConfigString(siteConfig, "ai.translate.glossary"), cancellationToken);

        return new AiTranslationSettings(
            Provider: provider,
            Model: model,
            BaseUrl: baseUrl,
            ApiKey: apiKey,
            TargetLanguages: targetLanguages,
            FrontMatterKeys: ReadConfigStringList(siteConfig, "ai.translate.front_matter_keys", "ai.translate.fields").DefaultIfEmpty("title").ToList(),
            CachePath: cacheEnabled ? ResolveConfigFilePath(sourceDirectory, configuredCachePath) : null,
            Glossary: glossary);
    }

    private static OpenAiCompatibleTranslationClient CreateAiTranslationClient(AiTranslationSettings settings)
    {
        if (!string.Equals(settings.Provider, "ollama", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException($"AI translation provider '{settings.Provider}' requires an API key. Configure ai.api_key or ai.api_key_env.");
        }

        return new OpenAiCompatibleTranslationClient(settings.BaseUrl, settings.Model, settings.ApiKey);
    }

    private static bool ShouldAutoTranslateItem(JekyllContentItem item)
    {
        if (item.SourcePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (item.FrontMatter.TryGetValue("ai_translate", out var aiTranslateValue) && TryConvertToBoolean(aiTranslateValue) is false)
        {
            return false;
        }

        return true;
    }

    private static string ResolveItemLanguage(
        IReadOnlyDictionary<string, object?> frontMatter,
        IReadOnlyDictionary<string, object?> siteConfig)
    {
        return ReadStringValue(frontMatter, "lang", "language", "locale")
            ?? ReadConfigString(siteConfig, "lang", "language", "locale")
            ?? "en";
    }

    private async Task<AiTranslationGlossary> LoadAiTranslationGlossaryAsync(
        string sourceDirectory,
        string? glossaryPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(glossaryPath))
        {
            return AiTranslationGlossary.Empty;
        }

        var resolvedPath = ResolveConfigFilePath(sourceDirectory, glossaryPath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"AI translation glossary file not found: {resolvedPath}", resolvedPath);
        }

        var yaml = await File.ReadAllTextAsync(resolvedPath, cancellationToken);
        var parsed = NormalizeYamlValue(_yamlDeserializer.Deserialize<object?>(yaml));
        if (parsed is not Dictionary<string, object?> root)
        {
            return AiTranslationGlossary.Empty;
        }

        var termsValue = root.TryGetValue("terms", out var configuredTerms) ? configuredTerms : parsed;
        if (termsValue is not Dictionary<string, object?> glossaryTerms)
        {
            return AiTranslationGlossary.Empty;
        }

        var terms = new List<AiTranslationGlossaryTerm>();
        foreach (var pair in glossaryTerms)
        {
            var source = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            switch (pair.Value)
            {
                case string defaultTarget when !string.IsNullOrWhiteSpace(defaultTarget):
                    terms.Add(new AiTranslationGlossaryTerm(source, defaultTarget.Trim(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
                    break;
                case Dictionary<string, object?> perLanguageTargets:
                    var targets = perLanguageTargets
                        .Where(static entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value?.ToString()))
                        .ToDictionary(
                            entry => NormalizeLanguageCode(entry.Key),
                            entry => entry.Value!.ToString()!.Trim(),
                            StringComparer.OrdinalIgnoreCase);
                    terms.Add(new AiTranslationGlossaryTerm(source, null, targets));
                    break;
            }
        }

        return terms.Count == 0 ? AiTranslationGlossary.Empty : new AiTranslationGlossary(terms);
    }

    private static string ResolveConfigFilePath(string sourceDirectory, string configuredPath)
    {
        var normalizedPath = configuredPath.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalizedPath)
            ? normalizedPath
            : Path.GetFullPath(Path.Combine(sourceDirectory, normalizedPath));
    }

    private static async Task<string> TranslateTextAsync(
        AiTranslationSettings settings,
        IAiTranslationClient translationClient,
        AiTranslationCacheStore? translationCache,
        string sourceLanguage,
        string targetLanguage,
        string text,
        AiTextKind textKind,
        CancellationToken cancellationToken)
    {
        var request = new AiTranslationRequest(
            sourceLanguage,
            targetLanguage,
            text,
            textKind,
            settings.Glossary.ResolveEntries(targetLanguage));

        if (translationCache is not null
            && translationCache.TryGet(settings.Provider, settings.Model, settings.BaseUrl, request, out var cached))
        {
            return cached;
        }

        var translated = await translationClient.TranslateAsync(request, cancellationToken);
        translationCache?.Set(settings.Provider, settings.Model, settings.BaseUrl, request, translated);
        return translated;
    }

    private static List<LocaleDefinition> ReadLocales(IReadOnlyDictionary<string, object?> siteConfig)
    {
        var configuredLocales = ReadConfigValue(siteConfig, "locales");
        if (configuredLocales is IEnumerable<object?> sequence)
        {
            return sequence
                .OfType<Dictionary<string, object?>>()
                .Select(locale => new LocaleDefinition(
                    ReadStringValue(locale, "code") ?? string.Empty,
                    ReadStringValue(locale, "root") ?? "/" + (ReadStringValue(locale, "code") ?? string.Empty) + "/",
                    ReadStringValue(locale, "label") ?? (ReadStringValue(locale, "code") ?? string.Empty).ToUpperInvariant()))
                .Where(locale => !string.IsNullOrWhiteSpace(locale.Code))
                .ToList();
        }

        return [];
    }

    private static List<string> ReadConfigStringList(IReadOnlyDictionary<string, object?> siteConfig, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ReadConfigValue(siteConfig, key);
            switch (value)
            {
                case string single when !string.IsNullOrWhiteSpace(single):
                    return single
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                case IEnumerable<object?> sequence:
                    return sequence
                        .Select(static item => item?.ToString())
                        .Where(static item => !string.IsNullOrWhiteSpace(item))
                        .Cast<string>()
                        .ToList();
            }
        }

        return [];
    }

    private static string BuildTranslatedUrl(string originalUrl, string sourceLanguage, string targetLanguage)
    {
        var targetSegment = GetLanguagePathSegment(targetLanguage);
        var sourceSegment = GetLanguagePathSegment(sourceLanguage);
        var normalized = EnsureTrailingSlash(originalUrl);

        if (string.Equals(normalized, "/", StringComparison.Ordinal))
        {
            return $"/{targetSegment}/";
        }

        var segments = normalized.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 0 && string.Equals(segments[0], sourceSegment, StringComparison.OrdinalIgnoreCase))
        {
            segments[0] = targetSegment;
        }
        else
        {
            segments.Insert(0, targetSegment);
        }

        return "/" + string.Join('/', segments) + "/";
    }

    private static string BuildTranslatedRelativePath(string relativePath, string sourceLanguage, string targetLanguage)
    {
        var targetSegment = GetLanguagePathSegment(targetLanguage);
        var sourceSegment = GetLanguagePathSegment(sourceLanguage);
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return targetSegment + "/index.md";
        }

        if (normalized.StartsWith(sourceSegment + "/", StringComparison.OrdinalIgnoreCase))
        {
            return targetSegment + normalized[sourceSegment.Length..];
        }

        return targetSegment + "/" + normalized;
    }

    private static string GetLanguagePathSegment(string language)
        => NormalizeLanguageCode(language).Split('-', 2)[0];

    private static string NormalizeLanguageCode(string language)
        => language.Trim().ToLowerInvariant();

    private static string ResolveLocaleCode(
        IReadOnlyDictionary<string, object?> frontMatter,
        IReadOnlyDictionary<string, object?> siteConfig)
        => GetLanguagePathSegment(ResolveItemLanguage(frontMatter, siteConfig));

    private static string BuildTranslationGroupKey(
        JekyllContentItem item,
        IReadOnlyCollection<LocaleDefinition> locales,
        IReadOnlyDictionary<string, object?> siteConfig)
    {
        var normalizedUrl = EnsureTrailingSlash(item.Url);
        var segments = normalizedUrl.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 0 && locales.Any(locale => string.Equals(locale.Code, segments[0], StringComparison.OrdinalIgnoreCase)))
        {
            segments.RemoveAt(0);
        }

        return segments.Count == 0
            ? "/"
            : "/" + string.Join('/', segments) + "/";
    }

    private static string ReadLocaleLabel(string localeCode, IReadOnlyList<LocaleDefinition> locales)
    {
        return locales.FirstOrDefault(locale => string.Equals(locale.Code, localeCode, StringComparison.OrdinalIgnoreCase))?.Label
            ?? localeCode.ToUpperInvariant();
    }

    private static bool IsCanonicalLocaleUrl(string url, string localeCode, IReadOnlyList<LocaleDefinition> locales)
    {
        var localeRoot = locales.FirstOrDefault(locale => string.Equals(locale.Code, localeCode, StringComparison.OrdinalIgnoreCase))?.Root;
        if (string.IsNullOrWhiteSpace(localeRoot))
        {
            return false;
        }

        return string.Equals(EnsureTrailingSlash(url), EnsureTrailingSlash(localeRoot), StringComparison.OrdinalIgnoreCase);
    }

    private static int GetLocaleOrder(string localeCode, IReadOnlyList<LocaleDefinition> locales)
    {
        for (var index = 0; index < locales.Count; index++)
        {
            if (string.Equals(locales[index].Code, localeCode, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static bool TryResolveObject(IReadOnlyDictionary<string, object?> variables, string path, out object? value)
    {
        object? current = variables;

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is IReadOnlyDictionary<string, object?> readOnlyDictionary && readOnlyDictionary.TryGetValue(segment, out var readOnlyNext))
            {
                current = readOnlyNext;
                continue;
            }

            if (current is Dictionary<string, object?> dictionary && dictionary.TryGetValue(segment, out var next))
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
            if (values.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } text)
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
            if (values.TryGetValue(key, out var value) && value is not null)
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
            || Uri.TryCreate(url, UriKind.Absolute, out _)
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
        Dictionary<string, object?> siteConfig,
        IReadOnlyCollection<JekyllContentItem> items,
        JekyllSiteOptions options,
        CancellationToken cancellationToken)
    {
        var result = new List<JekyllStaticFile>();
        var renderedContentPaths = items.Select(x => x.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var collectionDefinitions = ReadCollectionDefinitions(siteConfig, options);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            if (ShouldSkip(relativePath, siteConfig, options) || renderedContentPaths.Contains(relativePath) || IsSassFile(file))
            {
                continue;
            }

            var hasFrontMatter = false;
            var content = string.Empty;
            var frontMatter = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (IsTextStaticFile(file))
            {
                var text = await File.ReadAllTextAsync(file, cancellationToken);
                var document = _frontMatterParser.Parse(text);
                hasFrontMatter = document.FrontMatter.Count > 0;
                content = hasFrontMatter ? document.Content : text;
                frontMatter = ApplyFrontMatterDefaults(relativePath, document.FrontMatter, siteConfig, collectionDefinitions, options);
            }
            else
            {
                frontMatter = ApplyFrontMatterDefaults(relativePath, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase), siteConfig, collectionDefinitions, options);
            }

            result.Add(new JekyllStaticFile
            {
                SourcePath = file,
                RelativePath = relativePath,
                OutputRelativePath = relativePath,
                Url = "/" + relativePath.Replace('\\', '/'),
                Content = content,
                FrontMatter = frontMatter,
                HasFrontMatter = hasFrontMatter
            });
        }

        return result;
    }

    private async Task CopyStaticFilesAsync(
        string destinationDirectory,
        IReadOnlyCollection<JekyllStaticFile> staticFiles,
        JekyllSiteContext context,
        CancellationToken cancellationToken)
    {
        foreach (var file in staticFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = Path.Combine(destinationDirectory, file.OutputRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

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
                await File.WriteAllTextAsync(destinationPath, rendered, cancellationToken);
                continue;
            }

            File.Copy(file.SourcePath, destinationPath, overwrite: true);
        }
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
        var siteConfig = await LoadConfigAsync(sourceDirectory, cancellationToken);
        var sassFiles = Directory.EnumerateFiles(sourceDirectory, "*.*", SearchOption.AllDirectories)
            .Where(IsSassFile)
            .Where(file =>
            {
                var relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                var fileName = Path.GetFileName(relative);
                return !fileName.StartsWith("_", StringComparison.Ordinal) && !ShouldSkip(relative, siteConfig, options);
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
           && (item.RelativePath.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase)
               || string.Equals(item.RelativePath, "index.html", StringComparison.OrdinalIgnoreCase));

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

        if (siteConfig.TryGetValue("paginate", out var sitePaginateValue) && TryConvertToInt(sitePaginateValue, out pageSize))
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

    private static string ResolvePermalink(
        string relativePath,
        Dictionary<string, object?> frontMatter,
        DateTimeOffset? date,
        string collection,
        bool isPost,
        IReadOnlyDictionary<string, object?> siteConfig)
    {
        if (frontMatter.TryGetValue("permalink", out var permalinkValue) && permalinkValue?.ToString() is { Length: > 0 } permalink)
        {
            return NormalizePermalink(permalink, relativePath, date, collection);
        }

        if (isPost
            && siteConfig.TryGetValue("permalink", out var sitePermalinkValue)
            && sitePermalinkValue?.ToString() is { Length: > 0 } sitePermalink)
        {
            return NormalizePermalink(sitePermalink, relativePath, date, collection);
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
        if (trimmed.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
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

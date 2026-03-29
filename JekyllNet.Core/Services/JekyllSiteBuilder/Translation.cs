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
            result["excerpt"] = ResolveExcerptValue(item);
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

}

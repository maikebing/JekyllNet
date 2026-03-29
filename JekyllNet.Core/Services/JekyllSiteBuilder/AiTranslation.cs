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

}

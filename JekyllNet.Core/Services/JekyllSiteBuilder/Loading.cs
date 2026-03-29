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
    private async Task<Dictionary<string, object?>> LoadConfigAsync(string sourceDirectory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(sourceDirectory, "_config.yml");
        if (!File.Exists(path))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        var yaml = await File.ReadAllTextAsync(path, cancellationToken);
        var values = _yamlDeserializer.Deserialize<Dictionary<object, object?>>(yaml) ?? new Dictionary<object, object?>();
        var config = values.ToDictionary(
            k => k.Key.ToString() ?? string.Empty,
            v => NormalizeYamlValue(v.Value),
            StringComparer.OrdinalIgnoreCase);
        ResolveVersionPlaceholders(config);
        return config;
    }

    private async Task<Dictionary<string, string>> LoadNamedTemplatesAsync(
        IEnumerable<string> sourceDirectories,
        string directoryName,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceDirectory in sourceDirectories)
        {
            var directory = Path.Combine(sourceDirectory, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                var relativeName = Path.GetRelativePath(directory, file).Replace('\\', '/');
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                result[relativeName] = content;
                result[Path.GetFileName(relativeName)] = content;
                result[Path.GetFileNameWithoutExtension(relativeName)] = content;
            }
        }

        return result;
    }

    private async Task<Dictionary<string, object?>> LoadDataAsync(
        IEnumerable<string> sourceDirectories,
        JekyllSiteOptions options,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceDirectory in sourceDirectories)
        {
            var dataDirectory = Path.Combine(sourceDirectory, options.Compatibility.DataDirectoryName);
            if (!Directory.Exists(dataDirectory))
            {
                continue;
            }

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
        }

        return result;
    }

}

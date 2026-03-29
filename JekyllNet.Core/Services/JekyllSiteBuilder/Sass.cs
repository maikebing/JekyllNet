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
    private async Task CompileSassAsync(
        string sourceDirectory,
        IReadOnlyList<string> inheritedThemeDirectories,
        string destinationDirectory,
        JekyllSiteOptions options,
        IReadOnlyDictionary<string, object?> siteVariables,
        IReadOnlyDictionary<string, string> includes,
        CancellationToken cancellationToken)
    {
        var siteConfig = await LoadConfigAsync(sourceDirectory, cancellationToken);
        var sassFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in inheritedThemeDirectories.Concat([sourceDirectory]))
        {
            foreach (var file in EnumerateSassEntryFiles(directory, directory == sourceDirectory, siteConfig, options))
            {
                var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                sassFiles[relative] = file;
            }
        }

        if (sassFiles.Count == 0)
        {
            return;
        }

        LogInfo(options, $"Compiling {sassFiles.Count} Sass entry file(s).");
        EnsureSassEngineRegistered();

        var includePaths = inheritedThemeDirectories
            .Concat([sourceDirectory])
            .Select(directory => Path.Combine(directory, "_sass"))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var compiler = new SassCompiler();

        foreach (var pair in sassFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = pair.Key;
            var file = pair.Value;
            var cssRelative = Path.ChangeExtension(relative, ".css")!;
            var destinationPath = Path.Combine(destinationDirectory, cssRelative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            LogInfo(options, $"Compiling Sass {relative} -> {cssRelative}", verboseOnly: true);
            var renderedSource = await RenderSassSourceAsync(file, relative, siteVariables, includes, cancellationToken);
            renderedSource = NormalizeSassImportPaths(renderedSource, file, includePaths);
            try
            {
                var compilationOptions = new CompilationOptions
                {
                    IncludePaths = includePaths
                };

                var result = string.Equals(Path.GetExtension(file), ".css", StringComparison.OrdinalIgnoreCase)
                    ? compiler.Compile(renderedSource, indentedSyntax: false, options: compilationOptions)
                    : compiler.Compile(renderedSource, file, destinationPath, sourceMapPath: null, options: compilationOptions);

                await File.WriteAllTextAsync(destinationPath, result.CompiledContent, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"Failed to compile Sass entry '{relative}'. {ex.Message}", ex);
            }
        }
    }

    private async Task<string> RenderSassSourceAsync(
        string sourcePath,
        string relativePath,
        IReadOnlyDictionary<string, object?> siteVariables,
        IReadOnlyDictionary<string, string> includes,
        CancellationToken cancellationToken)
    {
        try
        {
            var source = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            var document = _frontMatterParser.Parse(source);
            if (!document.HasFrontMatter)
            {
                return source;
            }

            var page = new Dictionary<string, object?>(document.FrontMatter, StringComparer.OrdinalIgnoreCase)
            {
                ["path"] = relativePath,
                ["url"] = "/" + Path.ChangeExtension(relativePath, ".css")!.Replace('\\', '/')
            };
            var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["page"] = page,
                ["site"] = siteVariables,
                ["content"] = document.Content
            };

            return _templateRenderer.Render(document.Content, variables, includes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to prepare Sass entry '{relativePath}'. {ex.Message}", ex);
        }
    }

    private static string NormalizeSassImportPaths(string source, string sourcePath, IReadOnlyList<string> includePaths)
    {
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("@import ", StringComparison.Ordinal)
                && !trimmed.StartsWith("@use ", StringComparison.Ordinal)
                && !trimmed.StartsWith("@forward ", StringComparison.Ordinal))
            {
                continue;
            }

            lines[i] = Regex.Replace(lines[i], "(['\"])\\./([^'\"]+)\\1", "$1$2$1");
        }

        return string.Join(newline, lines);
    }

    private static bool CanResolveSassImportFromCurrentFile(string sourcePath, string importPath)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        return !string.IsNullOrWhiteSpace(directory) && SassImportExists(directory, importPath);
    }

    private static bool CanResolveSassImportFromIncludePaths(string importPath, IReadOnlyList<string> includePaths)
        => includePaths.Any(path => SassImportExists(path, importPath));

    private static bool SassImportExists(string rootDirectory, string importPath)
    {
        var relativePath = importPath.Replace('/', Path.DirectorySeparatorChar);
        var basePath = Path.Combine(rootDirectory, relativePath);
        foreach (var candidate in ExpandSassImportCandidates(basePath))
        {
            if (File.Exists(candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ExpandSassImportCandidates(string basePath)
    {
        yield return basePath;

        if (string.IsNullOrWhiteSpace(Path.GetExtension(basePath)))
        {
            yield return basePath + ".scss";
            yield return basePath + ".sass";
        }

        var directory = Path.GetDirectoryName(basePath);
        var fileName = Path.GetFileName(basePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || fileName.StartsWith("_", StringComparison.Ordinal))
        {
            yield break;
        }

        var partialBasePath = Path.Combine(directory, "_" + fileName);
        yield return partialBasePath;

        if (string.IsNullOrWhiteSpace(Path.GetExtension(partialBasePath)))
        {
            yield return partialBasePath + ".scss";
            yield return partialBasePath + ".sass";
        }
    }
}

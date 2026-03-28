using System.Text;
using System.Runtime.CompilerServices;
using JekyllNet.Core.Models;
using JekyllNet.Core.Services;
using JekyllNet.Core.Translation;

namespace JekyllNet.Tests;

internal static class TestInfrastructure
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string GetRepoPath(params string[] segments)
        => Path.Combine([RepoRoot, .. segments]);

    public static async Task<string> BuildSiteAsync(
        string sourceDirectory,
        bool includeDrafts = false,
        bool includeFuture = false,
        bool includeUnpublished = false,
        int? postsPerPage = null,
        IAiTranslationClient? aiTranslationClient = null)
    {
        var destinationDirectory = Path.Combine(Path.GetTempPath(), "JekyllNet.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destinationDirectory);

        var builder = new JekyllSiteBuilder();
        await builder.BuildAsync(new JekyllSiteOptions
        {
            SourceDirectory = sourceDirectory,
            DestinationDirectory = destinationDirectory,
            IncludeDrafts = includeDrafts,
            IncludeFuture = includeFuture,
            IncludeUnpublished = includeUnpublished,
            PostsPerPage = postsPerPage,
            AiTranslationClient = aiTranslationClient
        });

        return destinationDirectory;
    }

    public static string[] EnumerateRelativeFiles(string directory)
    {
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    public static string ReadNormalizedText(string path)
        => NormalizeLineEndings(File.ReadAllText(path, Encoding.UTF8));

    public static void AssertRelativeFileMatches(string expectedRootDirectory, string actualRootDirectory, string relativePath)
    {
        var expectedPath = Path.Combine(expectedRootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var actualPath = Path.Combine(actualRootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (IsTextFile(relativePath))
        {
            Assert.Equal(ReadNormalizedText(expectedPath), ReadNormalizedText(actualPath));
            return;
        }

        Assert.Equal(File.ReadAllBytes(expectedPath), File.ReadAllBytes(actualPath));
    }

    public static string CreateSiteFixture(IReadOnlyDictionary<string, string> files)
    {
        var root = Path.Combine(Path.GetTempPath(), "JekyllNet.Tests", "Sites", Guid.NewGuid().ToString("N"));
        foreach (var pair in files)
        {
            var path = Path.Combine(root, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, pair.Value, Encoding.UTF8);
        }

        return root;
    }

    private static string FindRepoRoot([CallerFilePath] string currentFilePath = "")
    {
        var currentDirectory = Path.GetDirectoryName(currentFilePath);
        if (string.IsNullOrWhiteSpace(currentDirectory))
        {
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        return Path.GetFullPath(Path.Combine(currentDirectory, ".."));
    }

    private static bool IsTextFile(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        return extension is ".html" or ".css" or ".scss" or ".sass" or ".svg" or ".yml" or ".yaml" or ".md" or ".txt" or ".json";
    }

    private static string NormalizeLineEndings(string input)
        => input.Replace("\r\n", "\n", StringComparison.Ordinal);
}

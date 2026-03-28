namespace JekyllNet.Tests;

public sealed class SiteBuildRegressionTests
{
    [Fact]
    public async Task SampleSite_BuildsExpectedOutputStructure()
    {
        var sourceDirectory = TestInfrastructure.GetRepoPath("sample-site");
        var actualDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);

        Assert.Equal(
        [
            "2026/03/23/hello-world/index.html",
            "assets/css/site.css",
            "assets/scss/site.css",
            "docs/getting-started/index.html",
            "index.html"
        ],
        TestInfrastructure.EnumerateRelativeFiles(actualDirectory));

        TestInfrastructure.AssertRelativeFileMatches(sourceDirectory, actualDirectory, "assets/css/site.css");

        var homePage = TestInfrastructure.ReadNormalizedText(Path.Combine(actualDirectory, "index.html"));
        Assert.Contains("JekyllNet Sample", homePage, StringComparison.Ordinal);
        Assert.Contains("GitHub Pages compatibility layer enabled.", homePage, StringComparison.Ordinal);
        Assert.Contains("/2026/03/23/hello-world/", homePage, StringComparison.Ordinal);
        Assert.Contains("Mystic / Maintainer", homePage, StringComparison.Ordinal);
        Assert.Contains("Front Matter", homePage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DocsSite_BuildsExpectedOutputStructureAndAssets()
    {
        var sourceDirectory = TestInfrastructure.GetRepoPath("docs");
        var actualDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);

        Assert.Equal(GetExpectedDocsOutputFiles(sourceDirectory), TestInfrastructure.EnumerateRelativeFiles(actualDirectory));

        foreach (var relativePath in GetExpectedCopiedDocsFiles(sourceDirectory))
        {
            TestInfrastructure.AssertRelativeFileMatches(sourceDirectory, actualDirectory, relativePath);
        }

        var englishHome = TestInfrastructure.ReadNormalizedText(Path.Combine(actualDirectory, "en", "index.html"));
        Assert.Contains("Build Jekyll-style sites in C#", englishHome, StringComparison.Ordinal);
        Assert.Contains("href=\"/en/navigation/\"", englishHome, StringComparison.Ordinal);
        Assert.Contains("href=\"/assets/brand/jekyll-net-favicon.svg\"", englishHome, StringComparison.Ordinal);
        Assert.Contains("href=\"https://github.com/JekyllNet/JekyllNet/tree/main/sample-site\"", englishHome, StringComparison.Ordinal);

        var chineseHome = TestInfrastructure.ReadNormalizedText(Path.Combine(actualDirectory, "zh", "index.html"));
        Assert.Contains("href=\"/zh/navigation/\"", chineseHome, StringComparison.Ordinal);
        Assert.Contains("href=\"/en/\"", chineseHome, StringComparison.Ordinal);
        Assert.Contains("href=\"/assets/css/site.css\"", chineseHome, StringComparison.Ordinal);

        var projectStatusNews = TestInfrastructure.ReadNormalizedText(Path.Combine(actualDirectory, "en", "news", "project-status", "index.html"));
        Assert.Contains("JekyllNet launch: docs, workflows, and compatibility baseline", projectStatusNews, StringComparison.Ordinal);
        Assert.Contains("AI-assisted translation", projectStatusNews, StringComparison.Ordinal);
    }

    private static string[] GetExpectedDocsOutputFiles(string sourceDirectory)
    {
        var renderableMarkdownFiles = Directory.EnumerateFiles(sourceDirectory, "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/'))
            .Where(static relativePath => relativePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .All(static segment => !segment.StartsWith('_')))
            .Select(static relativePath => GetMarkdownOutputPath(relativePath));

        return GetExpectedCopiedDocsFiles(sourceDirectory)
            .Concat(renderableMarkdownFiles)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] GetExpectedCopiedDocsFiles(string sourceDirectory)
    {
        var assetFiles = Directory.EnumerateFiles(Path.Combine(sourceDirectory, "assets"), "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(sourceDirectory, path).Replace('\\', '/'));

        return ["CNAME", .. assetFiles.OrderBy(path => path, StringComparer.Ordinal)];
    }

    private static string GetMarkdownOutputPath(string relativePath)
    {
        var withoutExtension = relativePath[..^Path.GetExtension(relativePath).Length];
        return string.Equals(Path.GetFileName(withoutExtension), "index", StringComparison.OrdinalIgnoreCase)
            ? withoutExtension + ".html"
            : withoutExtension + "/index.html";
    }
}

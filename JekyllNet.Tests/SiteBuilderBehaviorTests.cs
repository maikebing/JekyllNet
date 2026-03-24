namespace JekyllNet.Tests;

public sealed class SiteBuilderBehaviorTests
{
    [Fact]
    public async Task Build_ExcludesDraftsFutureAndUnpublished_ByDefault()
    {
        var sourceDirectory = CreateContentSiteFixture();
        var outputDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);

        Assert.True(File.Exists(Path.Combine(outputDirectory, "index.html")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "blog", "index.html")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "2000", "01", "02", "published", "index.html")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "2099", "01", "01", "future", "index.html")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "2000", "01", "03", "unpublished", "index.html")));
        Assert.False(File.Exists(Path.Combine(outputDirectory, "2000", "01", "04", "draft-entry", "index.html")));
    }

    [Fact]
    public async Task Build_IncludesDraftsFutureAndUnpublished_WhenEnabled()
    {
        var sourceDirectory = CreateContentSiteFixture();
        var outputDirectory = await TestInfrastructure.BuildSiteAsync(
            sourceDirectory,
            includeDrafts: true,
            includeFuture: true,
            includeUnpublished: true);

        Assert.True(File.Exists(Path.Combine(outputDirectory, "2099", "01", "01", "future", "index.html")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "2000", "01", "03", "unpublished", "index.html")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "2000", "01", "04", "draft-entry", "index.html")));
    }

    private static string CreateContentSiteFixture()
    {
        return TestInfrastructure.CreateSiteFixture(new Dictionary<string, string>
        {
            ["_config.yml"] = """
                title: Feature Test Site
                """,
            ["_layouts/default.html"] = """
                <html>
                <body>
                {{ content }}
                </body>
                </html>
                """,
            ["index.md"] = """
                ---
                layout: default
                title: Home
                ---
                # Home
                """,
            ["blog/index.md"] = """
                ---
                layout: default
                title: Blog
                ---
                # Blog
                """,
            ["_posts/2000-01-02-published.md"] = """
                ---
                layout: default
                title: Published
                ---
                Published
                """,
            ["_posts/2099-01-01-future.md"] = """
                ---
                layout: default
                title: Future
                ---
                Future
                """,
            ["_posts/2000-01-03-unpublished.md"] = """
                ---
                layout: default
                title: Unpublished
                published: false
                ---
                Unpublished
                """,
            ["_drafts/draft-entry.md"] = """
                ---
                layout: default
                title: Draft
                date: 2000-01-04
                ---
                Draft
                """
        });
    }
}

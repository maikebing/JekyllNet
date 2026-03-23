namespace JekyllNet.Core.Compatibility;

public sealed class GitHubPagesCompatibilityOptions
{
    public bool Enabled { get; init; } = true;

    public string SourceDirectoryName { get; init; } = string.Empty;

    public string DestinationDirectoryName { get; init; } = "_site";

    public string PostsDirectoryName { get; init; } = "_posts";

    public string LayoutsDirectoryName { get; init; } = "_layouts";

    public string IncludesDirectoryName { get; init; } = "_includes";

    public string DataDirectoryName { get; init; } = "_data";

    public string CollectionsKey { get; init; } = "collections";

    public IReadOnlyList<string> WhitelistedPlugins { get; init; } =
    [
        "jekyll-feed",
        "jekyll-sitemap",
        "jekyll-seo-tag",
        "jekyll-paginate",
        "jekyll-redirect-from"
    ];
}
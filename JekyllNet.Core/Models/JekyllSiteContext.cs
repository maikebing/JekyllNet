using JekyllNet.Core.Compatibility;

namespace JekyllNet.Core.Models;

public sealed class JekyllSiteContext
{
    public required string SourceDirectory { get; init; }

    public required string DestinationDirectory { get; init; }

    public required Dictionary<string, object?> SiteConfig { get; init; }

    public required Dictionary<string, string> Layouts { get; init; }

    public required Dictionary<string, string> Includes { get; init; }

    public required List<JekyllContentItem> Posts { get; init; }

    public required Dictionary<string, List<JekyllContentItem>> Collections { get; init; }

    public required List<JekyllStaticFile> StaticFiles { get; init; }

    public required GitHubPagesCompatibilityOptions Compatibility { get; init; }

    /// <summary>
    /// Additional content items injected by <see cref="JekyllNet.Core.Plugins.IJekyllGenerator"/>
    /// implementations. These are merged into <see cref="Posts"/> / <see cref="Collections"/>
    /// before the main rendering pass begins.
    /// </summary>
    public List<JekyllContentItem> ExtraItems { get; } = [];
}

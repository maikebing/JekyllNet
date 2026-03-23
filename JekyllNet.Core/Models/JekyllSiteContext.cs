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

    public required GitHubPagesCompatibilityOptions Compatibility { get; init; }
}
using JekyllNet.Core.Compatibility;

namespace JekyllNet.Core.Models;

public sealed class JekyllSiteOptions
{
    public string SourceDirectory { get; init; } = string.Empty;

    public string DestinationDirectory { get; init; } = string.Empty;

    public GitHubPagesCompatibilityOptions Compatibility { get; init; } = new();

    public bool IncludeDrafts { get; init; }

    public bool IncludeFuture { get; init; }

    public bool IncludeUnpublished { get; init; }

    public int? PostsPerPage { get; init; }
}
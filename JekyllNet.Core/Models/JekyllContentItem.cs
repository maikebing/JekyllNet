namespace JekyllNet.Core.Models;

public sealed class JekyllContentItem
{
    public string SourcePath { get; init; } = string.Empty;

    public string RelativePath { get; init; } = string.Empty;

    public string OutputRelativePath { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string Collection { get; init; } = string.Empty;

    public bool IsPost { get; init; }

    public bool IsDraft { get; init; }

    public DateTimeOffset? Date { get; set; }

    public List<string> Tags { get; init; } = [];

    public List<string> Categories { get; init; } = [];

    public Dictionary<string, object?> FrontMatter { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string RawContent { get; init; } = string.Empty;

    public string RenderedContent { get; set; } = string.Empty;
}

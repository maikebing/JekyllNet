using System.Collections.Generic;

namespace JekyllNet.Core.Models;

public sealed class FrontMatterDocument
{
    public Dictionary<string, object?> FrontMatter { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string Content { get; init; } = string.Empty;
}
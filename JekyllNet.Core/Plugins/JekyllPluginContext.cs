using JekyllNet.Core.Models;

namespace JekyllNet.Core.Plugins;

/// <summary>
/// The context passed to plugin methods during template rendering or site generation.
/// Provides read access to the site configuration, variables, and source directory.
/// </summary>
public sealed class JekyllPluginContext
{
    /// <summary>Merged Liquid variable scope for the current render pass.</summary>
    public required IReadOnlyDictionary<string, object?> Variables { get; init; }

    /// <summary>The site configuration loaded from <c>_config.yml</c>.</summary>
    public required IReadOnlyDictionary<string, object?> SiteConfig { get; init; }

    /// <summary>The absolute path to the source directory being built.</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>
    /// Convenience accessor for a top-level Liquid variable.
    /// Returns <see langword="null"/> if the key is not present.
    /// </summary>
    public object? this[string key] =>
        Variables.TryGetValue(key, out var v) ? v : null;
}

using JekyllNet.Core.Models;

namespace JekyllNet.Core.Plugins;

/// <summary>
/// A site-level generator that runs before any content is rendered,
/// equivalent to subclassing <c>Jekyll::Generator</c> in Ruby.
/// </summary>
/// <remarks>
/// Generators can inject new content items, modify site data, or
/// populate <see cref="JekyllSiteContext.ExtraItems"/> with dynamically
/// created pages/posts.
/// </remarks>
public interface IJekyllGenerator : IJekyllPlugin
{
    /// <summary>Runs the generator and may modify <paramref name="context"/>.</summary>
    Task GenerateAsync(JekyllSiteContext context, CancellationToken cancellationToken = default);
}

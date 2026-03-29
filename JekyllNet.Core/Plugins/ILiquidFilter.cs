namespace JekyllNet.Core.Plugins;

/// <summary>
/// A custom Liquid filter module registered via
/// <c>Liquid::Template.register_filter(Module)</c> in Ruby,
/// or by implementing this interface in C#.
/// </summary>
/// <remarks>
/// A single filter module can expose multiple filter methods.
/// Each method is identified by <see cref="FilterNames"/>.
/// </remarks>
public interface ILiquidFilter : IJekyllPlugin
{
    /// <summary>
    /// The filter names this implementation handles (lower-case, snake_case).
    /// </summary>
    IReadOnlyList<string> FilterNames { get; }

    /// <summary>
    /// Applies a named filter to <paramref name="input"/> and returns the result.
    /// </summary>
    /// <param name="filterName">Which filter to apply (one of <see cref="FilterNames"/>).</param>
    /// <param name="input">The piped-in value.</param>
    /// <param name="argument">Optional colon argument, e.g., <c>"keyword"</c> in <c>value | filter: "keyword"</c>.</param>
    /// <param name="context">The current template rendering context.</param>
    object? Apply(string filterName, object? input, string? argument, JekyllPluginContext context);
}

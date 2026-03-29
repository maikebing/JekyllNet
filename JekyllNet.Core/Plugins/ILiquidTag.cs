namespace JekyllNet.Core.Plugins;

/// <summary>
/// A custom Liquid inline tag registered via
/// <c>Liquid::Template.register_tag('name', Class)</c> in Ruby
/// or by implementing this interface in C#.
/// </summary>
/// <remarks>
/// Corresponds to the Liquid <c>{% tag_name markup %}</c> syntax.
/// </remarks>
public interface ILiquidTag : IJekyllPlugin
{
    /// <summary>Gets the Liquid tag name (lower-case, snake_case).</summary>
    string TagName { get; }

    /// <summary>
    /// Renders the tag and returns the HTML/text output.
    /// </summary>
    /// <param name="markup">The raw markup string after the tag name.</param>
    /// <param name="context">The current template rendering context.</param>
    string Render(string markup, JekyllPluginContext context);
}

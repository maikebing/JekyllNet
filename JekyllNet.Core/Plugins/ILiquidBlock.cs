namespace JekyllNet.Core.Plugins;

/// <summary>
/// A custom Liquid block tag registered by extending
/// <c>Liquid::Block</c> in Ruby, or by implementing this interface in C#.
/// </summary>
/// <remarks>
/// Corresponds to the Liquid <c>{% block_name markup %}body{% endblock_name %}</c> syntax.
/// </remarks>
public interface ILiquidBlock : IJekyllPlugin
{
    /// <summary>Gets the Liquid block tag name (lower-case, snake_case).</summary>
    string TagName { get; }

    /// <summary>
    /// Renders the block and returns the HTML/text output.
    /// </summary>
    /// <param name="markup">The raw markup string after the opening tag name.</param>
    /// <param name="body">The rendered content between the opening and closing tags.</param>
    /// <param name="context">The current template rendering context.</param>
    string Render(string markup, string body, JekyllPluginContext context);
}

namespace JekyllNet.Core.Plugins;

/// <summary>
/// Marker interface for all JekyllNet plugins.
/// Implement one or more of the derived interfaces:
/// <see cref="ILiquidTag"/>, <see cref="ILiquidBlock"/>,
/// <see cref="ILiquidFilter"/>, or <see cref="IJekyllGenerator"/>.
/// </summary>
public interface IJekyllPlugin { }

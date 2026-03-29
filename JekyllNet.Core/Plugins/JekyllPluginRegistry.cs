namespace JekyllNet.Core.Plugins;

/// <summary>
/// Holds all registered plugins for a single build.
/// </summary>
public sealed class JekyllPluginRegistry
{
    private readonly Dictionary<string, ILiquidTag> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ILiquidBlock> _blocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ILiquidFilter> _filtersByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IJekyllGenerator> _generators = [];

    public IReadOnlyDictionary<string, ILiquidTag> Tags => _tags;
    public IReadOnlyDictionary<string, ILiquidBlock> Blocks => _blocks;
    public IReadOnlyDictionary<string, ILiquidFilter> FiltersByName => _filtersByName;
    public IReadOnlyList<IJekyllGenerator> Generators => _generators;

    public void RegisterTag(ILiquidTag tag) => _tags[tag.TagName] = tag;
    public void RegisterBlock(ILiquidBlock block) => _blocks[block.TagName] = block;
    public void RegisterGenerator(IJekyllGenerator generator) => _generators.Add(generator);

    public void RegisterFilter(ILiquidFilter filter)
    {
        foreach (var name in filter.FilterNames)
            _filtersByName[name] = filter;
    }
}

namespace JekyllNet.Core.Plugins.Loading;

/// <summary>
/// Scans a <c>_plugins</c> directory and produces a populated
/// <see cref="JekyllPluginRegistry"/> by compiling <c>.cs</c> files directly
/// and transpiling <c>.rb</c> files through
/// <see cref="RubyPluginTranspiler"/> before compilation.
/// </summary>
public static class JekyllPluginLoader
{
    /// <summary>
    /// Loads all plugins from <paramref name="pluginsDirectory"/>.
    /// Returns an empty registry if the directory does not exist.
    /// </summary>
    /// <param name="pluginsDirectory">Absolute path to the <c>_plugins</c> folder.</param>
    /// <param name="diagnostics">
    /// When non-null, receives one line per plugin that could not be loaded (non-fatal).
    /// Fatal compilation errors still throw.
    /// </param>
    public static JekyllPluginRegistry Load(
        string pluginsDirectory,
        IList<string>? diagnostics = null)
    {
        var registry = new JekyllPluginRegistry();

        if (!Directory.Exists(pluginsDirectory))
            return registry;

        foreach (var file in Directory.EnumerateFiles(pluginsDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".rb", StringComparison.OrdinalIgnoreCase)
                && !ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                LoadFile(file, registry, diagnostics);
            }
            catch (PluginCompilationException ex)
            {
                // Non-fatal: log and continue so a single bad plugin doesn't break the build.
                diagnostics?.Add($"[plugin] Compilation failed for '{Path.GetFileName(file)}': {ex.Message}");
            }
            catch (Exception ex)
            {
                diagnostics?.Add($"[plugin] Unexpected error loading '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        return registry;
    }

    // ───────────────────────────────────────────────────────────────────────

    private static void LoadFile(string filePath, JekyllPluginRegistry registry, IList<string>? diagnostics)
    {
        var ext = Path.GetExtension(filePath);
        string source;

        if (ext.Equals(".rb", StringComparison.OrdinalIgnoreCase))
        {
            var rubySource = File.ReadAllText(filePath);
            var csSource = RubyPluginTranspiler.Transpile(rubySource, filePath);
            if (csSource is null)
            {
                diagnostics?.Add($"[plugin] Skipped '{Path.GetFileName(filePath)}': not a recognised Jekyll extension point.");
                return;
            }
            source = csSource;
        }
        else
        {
            source = File.ReadAllText(filePath);
        }

        var plugins = CSharpPluginCompiler.CompileAndInstantiate(source, filePath);
        foreach (var plugin in plugins)
            RegisterPlugin(plugin, registry);

        if (plugins.Count > 0)
            diagnostics?.Add($"[plugin] Loaded {plugins.Count} plugin(s) from '{Path.GetFileName(filePath)}'.");
    }

    private static void RegisterPlugin(IJekyllPlugin plugin, JekyllPluginRegistry registry)
    {
        switch (plugin)
        {
            case ILiquidBlock block:
                registry.RegisterBlock(block);
                break;
            case ILiquidTag tag:
                registry.RegisterTag(tag);
                break;
            case ILiquidFilter filter:
                registry.RegisterFilter(filter);
                break;
            case IJekyllGenerator generator:
                registry.RegisterGenerator(generator);
                break;
        }
    }
}

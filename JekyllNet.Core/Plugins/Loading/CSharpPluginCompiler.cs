using System.Reflection;
using System.Runtime.Loader;
using JekyllNet.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace JekyllNet.Core.Plugins.Loading;

/// <summary>
/// Compiles a C# source file (or generated source string) into an in-memory assembly
/// and returns all <see cref="IJekyllPlugin"/> implementations found in it.
/// </summary>
internal static class CSharpPluginCompiler
{
    // Framework + JekyllNet.Core assemblies needed for plugin compilation
    private static readonly IReadOnlyList<MetadataReference> _baseReferences = BuildBaseReferences();
    private static readonly IReadOnlyDictionary<string, string> _trustedPlatformAssemblies = BuildTrustedPlatformAssemblyMap();

    /// <summary>
    /// Compiles <paramref name="sourceCode"/> and reflects all
    /// <see cref="IJekyllPlugin"/> concrete classes from the resulting assembly.
    /// </summary>
    /// <param name="sourceCode">Full C# source text.</param>
    /// <param name="fileName">Display name used in diagnostic messages.</param>
    /// <exception cref="PluginCompilationException">Thrown when the source has compile errors.</exception>
    public static IReadOnlyList<IJekyllPlugin> CompileAndInstantiate(string sourceCode, string fileName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: fileName);

        var compilation = CSharpCompilation.Create(
            assemblyName: $"JekyllPlugin_{Path.GetFileNameWithoutExtension(fileName)}_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: _baseReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString());
            throw new PluginCompilationException(fileName, errors);
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(ms);

        return assembly.GetExportedTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.IsAssignableTo(typeof(IJekyllPlugin)))
            .Select(t => (IJekyllPlugin)Activator.CreateInstance(t)!)
            .ToList();
    }

    private static IReadOnlyList<MetadataReference> BuildBaseReferences()
    {
        var refs = new List<MetadataReference>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Resolve compile-time references from trusted platform assemblies so single-file apps are supported.
        foreach (var assemblyFile in new[]
                 {
                     "System.Runtime.dll",
                     "System.Collections.dll",
                     "System.Linq.dll",
                     "System.Net.Http.dll",
                     "System.Text.RegularExpressions.dll",
                     "System.Text.Json.dll",
                     "System.Private.CoreLib.dll",
                     "netstandard.dll",
                     typeof(IJekyllPlugin).Assembly.GetName().Name + ".dll"
                 })
        {
            AddReferenceIfPresent(refs, added, assemblyFile);
        }

        return refs;
    }

    private static IReadOnlyDictionary<string, string> BuildTrustedPlatformAssemblyMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
        {
            return map;
        }

        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            map[Path.GetFileName(path)] = path;
        }

        return map;
    }

    private static void AddReferenceIfPresent(List<MetadataReference> references, HashSet<string> added, string assemblyFile)
    {
        if (!_trustedPlatformAssemblies.TryGetValue(assemblyFile, out var path)
            || !added.Add(path)
            || !File.Exists(path))
        {
            return;
        }

        references.Add(MetadataReference.CreateFromFile(path));
    }
}

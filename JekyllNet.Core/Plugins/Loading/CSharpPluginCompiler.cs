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

        // .NET runtime assemblies
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in new[] { "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll",
                                     "System.Net.Http.dll", "System.Text.RegularExpressions.dll",
                                     "System.Text.Json.dll", "netstandard.dll" })
        {
            var path = Path.Combine(runtimeDir, dll);
            if (File.Exists(path))
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        // mscorlib / System.Private.CoreLib
        refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));

        // JekyllNet.Core itself (so plugins can implement our interfaces)
        refs.Add(MetadataReference.CreateFromFile(typeof(IJekyllPlugin).Assembly.Location));

        return refs;
    }
}

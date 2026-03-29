namespace JekyllNet.Core.Plugins.Loading;

/// <summary>
/// Thrown when a plugin's C# source fails to compile.
/// </summary>
public sealed class PluginCompilationException : Exception
{
    public string FileName { get; }
    public IReadOnlyList<string> CompilerErrors { get; }

    public PluginCompilationException(string fileName, IEnumerable<string> errors)
        : base($"Plugin '{fileName}' failed to compile:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}")
    {
        FileName = fileName;
        CompilerErrors = errors.ToList();
    }
}

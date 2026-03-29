using JekyllNet.ReleaseTool;

namespace JekyllNet.Tests;

public sealed class ReleaseToolRuntimeTests
{
    [Fact]
    public void ResolveThemeTargets_UsesDefaultSixThemes_WhenSelectionIsEmpty()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        var targets = ReleaseToolRuntime.ResolveThemeTargets(repoRoot, null);

        Assert.Equal(6, targets.Count);
        Assert.Contains(targets, static target => target.Name == "jekyll-theme-chirpy");
        Assert.Contains(targets, static target => target.Name == "minimal-mistakes");
        Assert.Contains(targets, static target => target.Name == "al-folio");
        Assert.Contains(targets, static target => target.Name == "jekyll-TeXt-theme");
        Assert.Contains(targets, static target => target.Name == "just-the-docs");
        Assert.Contains(targets, static target => target.Name == "jekyll-theme-lumen");
    }

    [Fact]
    public void ResolveThemeTargets_ThrowsForUnknownTheme()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        Assert.Throws<ArgumentException>(() => ReleaseToolRuntime.ResolveThemeTargets(repoRoot, ["unknown-theme"]));
    }

    [Fact]
    public void ResolveThemeTargets_RemovesDuplicateThemes()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        var targets = ReleaseToolRuntime.ResolveThemeTargets(repoRoot, ["al-folio", "AL-FOLIO", "just-the-docs"]);

        Assert.Equal(2, targets.Count);
        Assert.Equal("al-folio", targets[0].Name);
        Assert.Equal("just-the-docs", targets[1].Name);
    }

    [Fact]
    public async Task ResolveVersion_UsesProjectVersion_WhenManualInputIsMissing()
    {
        var projectPath = Path.Combine(Path.GetTempPath(), "JekyllNet.Tests", Guid.NewGuid().ToString("N"), "Tool.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <Version>1.2.3</Version>
              </PropertyGroup>
            </Project>
            """);

        var settings = new ResolveVersionSettings(projectPath, "branch", "main", null, null);
        using var output = new StringWriter();

        var result = await ReleaseToolRuntime.ResolveVersionAsync(settings, output, CancellationToken.None);

        Assert.Equal("1.2.3", result.PackageVersion);
        Assert.Equal("v1.2.3", result.ReleaseTag);
    }

    [Fact]
    public async Task WriteSha256_WritesChecksumFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "JekyllNet.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "artifact.zip");
        var checksumPath = Path.Combine(root, "SHA256SUMS.txt");
        await File.WriteAllTextAsync(filePath, "checksum test");

        var settings = new WriteSha256Settings(filePath, "artifact.zip", checksumPath, null, null);
        using var output = new StringWriter();

        var hash = await ReleaseToolRuntime.WriteSha256Async(settings, output, CancellationToken.None);
        var checksumContent = await File.ReadAllTextAsync(checksumPath);

        Assert.Contains(hash, checksumContent, StringComparison.Ordinal);
        Assert.Contains("artifact.zip", checksumContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportWingetManifest_ReplacesTemplatePlaceholders()
    {
        var root = Path.Combine(Path.GetTempPath(), "JekyllNet.Tests", Guid.NewGuid().ToString("N"));
        var zipSource = Path.Combine(root, "payload");
        var zipPath = Path.Combine(root, "JekyllNet-win-x64.zip");
        var outputDirectory = Path.Combine(root, "winget");
        Directory.CreateDirectory(zipSource);
        await File.WriteAllTextAsync(Path.Combine(zipSource, "jekyllnet.exe"), "placeholder");
        System.IO.Compression.ZipFile.CreateFromDirectory(zipSource, zipPath);

        var settings = new ExportWingetManifestSettings(
            "0.1.0",
            "https://github.com/JekyllNet/JekyllNet/releases/download/v0.1.0/JekyllNet-win-x64.zip",
            zipPath,
            outputDirectory);

        using var output = new StringWriter();
        var manifestDirectory = await ReleaseToolRuntime.ExportWingetManifestAsync(settings, output, CancellationToken.None);
        var installerManifestPath = Path.Combine(manifestDirectory, "JekyllNet.JekyllNet.installer.yaml");
        var installerManifest = await File.ReadAllTextAsync(installerManifestPath);

        Assert.Contains("PackageVersion: 0.1.0", installerManifest, StringComparison.Ordinal);
        Assert.Contains("InstallerUrl: https://github.com/JekyllNet/JekyllNet/releases/download/v0.1.0/JekyllNet-win-x64.zip", installerManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("__WINDOWS_X64_ZIP_SHA256__", installerManifest, StringComparison.Ordinal);
    }
}

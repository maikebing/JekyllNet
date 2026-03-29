using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace JekyllNet.ReleaseTool;

internal sealed record ResolveVersionSettings(
    string? ProjectPath,
    string? GitHubRefType,
    string? GitHubRefName,
    string? InputVersion,
    string? GitHubOutputPath);

internal sealed record WriteSha256Settings(
    string FilePath,
    string AssetName,
    string OutputPath,
    string? GitHubOutputPath,
    string? GitHubOutputKey);

internal sealed record ExportWingetManifestSettings(
    string Version,
    string InstallerUrl,
    string ZipPath,
    string? OutputDirectory);

internal sealed record ThemeMatrixSettings(
    string[]? Themes,
    string Configuration,
    int PortStart,
    int DebugPortStart,
    int? MaxParallelism);

internal sealed record ResolvedVersionInfo(string PackageVersion, string ReleaseTag);

internal static class ReleaseToolRuntime
{
    private static readonly Regex SemVerRegex = new("^[0-9]+\\.[0-9]+\\.[0-9]+([-.][0-9A-Za-z.-]+)?$", RegexOptions.Compiled);
    private static readonly string[] DefaultThemeNames =
    [
        "jekyll-theme-chirpy",
        "minimal-mistakes",
        "al-folio",
        "jekyll-TeXt-theme",
        "just-the-docs",
        "jekyll-theme-lumen"
    ];

    private static readonly IReadOnlyDictionary<string, string> ThemeRelativePaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["jekyll-theme-chirpy"] = Path.Combine("themes", "jekyll-theme-chirpy"),
            ["minimal-mistakes"] = Path.Combine("themes", "minimal-mistakes"),
            ["al-folio"] = Path.Combine("themes", "al-folio"),
            ["jekyll-TeXt-theme"] = Path.Combine("themes", "jekyll-TeXt-theme"),
            ["just-the-docs"] = Path.Combine("themes", "just-the-docs"),
            ["jekyll-theme-lumen"] = Path.Combine("themes", "jekyll-theme-lumen")
        };

    public static async Task<ResolvedVersionInfo> ResolveVersionAsync(
        ResolveVersionSettings settings,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var version = ResolveVersion(settings);
        var releaseTag = ResolveReleaseTag(settings, version);
        ValidateVersion(version);

        if (!string.IsNullOrWhiteSpace(settings.GitHubOutputPath))
        {
            await AppendGitHubOutputAsync(settings.GitHubOutputPath, new Dictionary<string, string>
            {
                ["package_version"] = version,
                ["release_tag"] = releaseTag
            }, cancellationToken);
        }

        await output.WriteLineAsync($"Resolved package version: {version}");
        await output.WriteLineAsync($"Resolved release tag: {releaseTag}");
        return new ResolvedVersionInfo(version, releaseTag);
    }

    public static async Task<string> WriteSha256Async(
        WriteSha256Settings settings,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.FilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.AssetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.OutputPath);

        var hash = await ComputeSha256Async(settings.FilePath, cancellationToken);
        var outputDirectory = Path.GetDirectoryName(settings.OutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(settings.OutputPath, $"{hash}  {settings.AssetName}{Environment.NewLine}", cancellationToken);

        if (!string.IsNullOrWhiteSpace(settings.GitHubOutputPath) && !string.IsNullOrWhiteSpace(settings.GitHubOutputKey))
        {
            await AppendGitHubOutputAsync(settings.GitHubOutputPath, new Dictionary<string, string>
            {
                [settings.GitHubOutputKey] = hash
            }, cancellationToken);
        }

        await output.WriteLineAsync($"SHA256 ({settings.AssetName}): {hash}");
        return hash;
    }

    public static async Task<string> ExportWingetManifestAsync(
        ExportWingetManifestSettings settings,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.InstallerUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ZipPath);
        ValidateVersion(settings.Version);

        var repoRoot = FindRepoRoot();
        var templateDirectory = Path.Combine(repoRoot, "packaging", "winget", "templates");
        if (!Directory.Exists(templateDirectory))
        {
            throw new DirectoryNotFoundException($"Winget template directory not found: {templateDirectory}");
        }

        var outputDirectory = settings.OutputDirectory;
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = Path.Combine(repoRoot, "artifacts", "winget");
        }

        var manifestDirectory = Path.Combine(outputDirectory, "JekyllNet.JekyllNet", settings.Version);
        Directory.CreateDirectory(manifestDirectory);

        var zipSha256 = await ComputeSha256Async(settings.ZipPath, cancellationToken);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["__VERSION__"] = settings.Version,
            ["__WINDOWS_X64_ZIP_URL__"] = settings.InstallerUrl,
            ["__WINDOWS_X64_ZIP_SHA256__"] = zipSha256
        };

        foreach (var templatePath in Directory.EnumerateFiles(templateDirectory, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var content = await File.ReadAllTextAsync(templatePath, cancellationToken);
            foreach (var replacement in replacements)
            {
                content = content.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
            }

            var targetPath = Path.Combine(manifestDirectory, Path.GetFileName(templatePath));
            await File.WriteAllTextAsync(targetPath, content, cancellationToken);
        }

        var resolvedManifestDirectory = Path.GetFullPath(manifestDirectory);
        await output.WriteLineAsync($"Generated manifests: {resolvedManifestDirectory}");
        await output.WriteLineAsync($"Installer URL: {settings.InstallerUrl}");
        await output.WriteLineAsync($"Installer SHA256: {zipSha256}");
        return resolvedManifestDirectory;
    }

    public static async Task<bool> TestThemeMatrixAsync(
        ThemeMatrixSettings settings,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Configuration);
        if (settings.PortStart < 1 || settings.PortStart > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.PortStart), "Port start must be between 1 and 65535.");
        }

        if (settings.DebugPortStart < 1 || settings.DebugPortStart > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(settings.DebugPortStart), "Debug port start must be between 1 and 65535.");
        }

        var overallStopwatch = Stopwatch.StartNew();
        var repoRoot = FindRepoRoot();
        var selectedThemes = ResolveThemeTargets(repoRoot, settings.Themes);
        var maxParallelism = settings.MaxParallelism ?? selectedThemes.Count;
        maxParallelism = Math.Max(1, Math.Min(maxParallelism, selectedThemes.Count));

        var dotnetHome = Path.Combine(repoRoot, ".dotnet-home");
        Directory.CreateDirectory(dotnetHome);

        var processEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_HOME"] = dotnetHome,
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"
        };

        await output.WriteLineAsync($"Building CLI in {settings.Configuration}...");
        var cliProjectPath = Path.Combine(repoRoot, "JekyllNet.Cli", "JekyllNet.Cli.csproj");
        var cliBuild = await RunProcessAsync(
            "dotnet",
            ["build", cliProjectPath, "-c", settings.Configuration],
            repoRoot,
            processEnvironment,
            cancellationToken);

        if (cliBuild.ExitCode != 0)
        {
            await output.WriteLineAsync("CLI build failed:");
            await output.WriteLineAsync(cliBuild.Output);
            return false;
        }

        var cliExecutable = ResolveCliExecutablePath(repoRoot, settings.Configuration);
        if (!File.Exists(cliExecutable))
        {
            await output.WriteLineAsync($"CLI executable was not found at {cliExecutable}");
            return false;
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync($"Starting parallel theme builds (max parallelism: {maxParallelism})...");

        var buildResults = new ConcurrentBag<ThemeBuildResult>();
        await Parallel.ForEachAsync(
            selectedThemes,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            },
            async (theme, token) =>
            {
                var stopwatch = Stopwatch.StartNew();
                ProcessExecutionResult result;
                try
                {
                    result = await RunProcessAsync(
                        cliExecutable,
                        ["build", "--source", theme.Source, "--destination", theme.Destination],
                        repoRoot,
                        processEnvironment,
                        token);
                }
                catch (Exception ex)
                {
                    result = new ProcessExecutionResult(1, ex.ToString());
                }
                finally
                {
                    stopwatch.Stop();
                }

                buildResults.Add(new ThemeBuildResult(theme.Name, theme.Source, theme.Destination, result.ExitCode, stopwatch.Elapsed, result.Output));
            });

        var orderedBuildResults = buildResults.OrderBy(static item => item.Name, StringComparer.Ordinal).ToArray();
        await output.WriteLineAsync();
        await output.WriteLineAsync("Build results:");
        foreach (var result in orderedBuildResults)
        {
            var status = result.ExitCode == 0 ? "OK" : "FAIL";
            await output.WriteLineAsync($"- {result.Name}: {status} ({result.Duration:hh\\:mm\\:ss\\.fff})");
        }

        var browserResults = new List<BrowserCheckResult>();
        var successfulBuilds = orderedBuildResults.Where(static item => item.ExitCode == 0).ToArray();
        if (successfulBuilds.Length > 0)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("Running browser checks...");

            var edgeExecutable = TryResolveEdgeExecutablePath();
            if (string.IsNullOrWhiteSpace(edgeExecutable))
            {
                await output.WriteLineAsync("Microsoft Edge was not found. Browser checks cannot run.");
                return false;
            }

            for (var index = 0; index < successfulBuilds.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = successfulBuilds[index];
                var port = settings.PortStart + index;
                var debugPort = settings.DebugPortStart + index;

                if (ShouldSkipRootBrowserCheck(result))
                {
                    browserResults.Add(new BrowserCheckResult(
                        result.Name,
                        $"http://127.0.0.1:{port}/",
                        Loaded: true,
                        Errors: [],
                        Skipped: true,
                        SkipReason: "theme package has no root index.html"));
                    continue;
                }

                try
                {
                    await using var server = new StaticSiteServer(result.Destination, port, result.Name);
                    await server.StartAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                    var browserResult = await InvokeBrowserCheckAsync(result.Name, $"http://127.0.0.1:{port}/", debugPort, edgeExecutable, cancellationToken);
                    browserResults.Add(browserResult);
                }
                catch (Exception ex)
                {
                    browserResults.Add(new BrowserCheckResult(result.Name, $"http://127.0.0.1:{port}/", false, [$"Browser check failed: {ex.Message}"]));
                }
            }
        }

        overallStopwatch.Stop();
        await output.WriteLineAsync();
        await output.WriteLineAsync("Browser results:");
        foreach (var result in orderedBuildResults)
        {
            if (result.ExitCode != 0)
            {
                await output.WriteLineAsync($"- {result.Name}: skipped (build failed)");
                continue;
            }

            var browser = browserResults.FirstOrDefault(item => string.Equals(item.Name, result.Name, StringComparison.Ordinal));
            if (browser is null)
            {
                await output.WriteLineAsync($"- {result.Name}: no browser result");
                continue;
            }

            if (browser.Skipped)
            {
                await output.WriteLineAsync($"- {result.Name}: skipped ({browser.SkipReason})");
                continue;
            }

            if (!browser.Loaded)
            {
                if (browser.Errors.Count == 0)
                {
                    await output.WriteLineAsync($"- {result.Name}: page did not finish loading (warning)");
                }
                else
                {
                    await output.WriteLineAsync($"- {result.Name}: page did not finish loading, with {browser.Errors.Count} browser error(s)");
                    foreach (var error in browser.Errors)
                    {
                        await output.WriteLineAsync($"  * {error}");
                    }
                }

                continue;
            }

            if (browser.Errors.Count == 0)
            {
                await output.WriteLineAsync($"- {result.Name}: no browser errors");
                continue;
            }

            await output.WriteLineAsync($"- {result.Name}: {browser.Errors.Count} browser error(s)");
            foreach (var error in browser.Errors)
            {
                await output.WriteLineAsync($"  * {error}");
            }
        }

        await output.WriteLineAsync();
        await output.WriteLineAsync($"Total elapsed: {overallStopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");

        var failedBuilds = orderedBuildResults.Where(static item => item.ExitCode != 0).ToArray();
        if (failedBuilds.Length > 0)
        {
            await output.WriteLineAsync();
            await output.WriteLineAsync("Failed build logs:");
            foreach (var result in failedBuilds)
            {
                await output.WriteLineAsync($"--- {result.Name} ---");
                await output.WriteLineAsync(result.Output);
            }
        }

        var hasBuildFailures = failedBuilds.Length > 0;
        var hasBrowserFailures = browserResults.Any(static item => item.Errors.Count > 0);
        return !hasBuildFailures && !hasBrowserFailures;
    }

    private static bool ShouldSkipRootBrowserCheck(ThemeBuildResult result)
    {
        if (!string.Equals(result.Name, "jekyll-theme-lumen", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rootIndexPath = Path.Combine(result.Destination, "index.html");
        return !File.Exists(rootIndexPath);
    }

    internal static IReadOnlyList<ThemeBuildTarget> ResolveThemeTargets(string repoRoot, IReadOnlyCollection<string>? selectedThemes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var requestedThemes = selectedThemes is null || selectedThemes.Count == 0
            ? DefaultThemeNames
            : selectedThemes.ToArray();

        var result = new List<ThemeBuildTarget>(requestedThemes.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requestedTheme in requestedThemes)
        {
            if (string.IsNullOrWhiteSpace(requestedTheme) || !seen.Add(requestedTheme))
            {
                continue;
            }

            if (!ThemeRelativePaths.TryGetValue(requestedTheme, out var relativePath))
            {
                throw new ArgumentException($"Unknown theme '{requestedTheme}'.", nameof(selectedThemes));
            }

            var source = Path.Combine(repoRoot, relativePath);
            var destination = Path.Combine(repoRoot, "artifacts", "theme-builds", requestedTheme);
            result.Add(new ThemeBuildTarget(requestedTheme, source, destination));
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException("No themes were selected for the matrix build.");
        }

        return result;
    }

    private static string ResolveVersion(ResolveVersionSettings settings)
    {
        if (string.Equals(settings.GitHubRefType, "tag", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.GitHubRefName))
        {
            return settings.GitHubRefName.StartsWith('v')
                ? settings.GitHubRefName[1..]
                : settings.GitHubRefName;
        }

        if (!string.IsNullOrWhiteSpace(settings.InputVersion))
        {
            return settings.InputVersion;
        }

        if (string.IsNullOrWhiteSpace(settings.ProjectPath))
        {
            throw new InvalidOperationException("Could not resolve a package version because no project path was provided.");
        }

        var project = XDocument.Load(settings.ProjectPath);
        var version = project.Descendants("Version").Select(static element => element.Value).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException($"Could not resolve a package version from project file '{settings.ProjectPath}'.");
        }

        return version;
    }

    private static string ResolveReleaseTag(ResolveVersionSettings settings, string version)
    {
        if (string.Equals(settings.GitHubRefType, "tag", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.GitHubRefName))
        {
            return settings.GitHubRefName;
        }

        return $"v{version}";
    }

    private static void ValidateVersion(string version)
    {
        if (!SemVerRegex.IsMatch(version))
        {
            throw new InvalidOperationException($"Version '{version}' is not a valid SemVer-style release version.");
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async Task AppendGitHubOutputAsync(
        string githubOutputPath,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(githubOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await using var stream = new FileStream(githubOutputPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream);
        foreach (var pair in values)
        {
            await writer.WriteLineAsync($"{pair.Key}={pair.Value}".AsMemory(), cancellationToken);
        }
    }

    private static string ResolveCliExecutablePath(string repoRoot, string configuration)
    {
        var binDirectory = Path.Combine(repoRoot, "JekyllNet.Cli", "bin", configuration, "net10.0");
        var executable = OperatingSystem.IsWindows()
            ? Path.Combine(binDirectory, "JekyllNet.Cli.exe")
            : Path.Combine(binDirectory, "JekyllNet.Cli");

        return executable;
    }

    private static string? TryResolveEdgeExecutablePath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("JEKYLLNET_EDGE_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var candidates = new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<BrowserCheckResult> InvokeBrowserCheckAsync(
        string name,
        string url,
        int debugPort,
        string edgeExecutable,
        CancellationToken cancellationToken)
    {
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "jekyllnet-edge", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(userDataDirectory);

        var arguments = new[]
        {
            "--headless=new",
            "--disable-gpu",
            "--no-first-run",
            "--no-default-browser-check",
            $"--remote-debugging-port={debugPort}",
            $"--user-data-dir={userDataDirectory}",
            "about:blank"
        };

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = edgeExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { arguments[0], arguments[1], arguments[2], arguments[3], arguments[4], arguments[5] }
        }) ?? throw new InvalidOperationException("Failed to start Microsoft Edge.");

        try
        {
            var webSocketUrl = await WaitForDevToolsPageWebSocketAsync(debugPort, cancellationToken);
            if (string.IsNullOrWhiteSpace(webSocketUrl))
            {
                return new BrowserCheckResult(name, url, false, ["DevTools endpoint did not become ready."]);
            }

            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken);

            var commandId = 1;
            await SendCdpCommandAsync(socket, commandId++, "Runtime.enable", null, cancellationToken);
            await SendCdpCommandAsync(socket, commandId++, "Page.enable", null, cancellationToken);
            await SendCdpCommandAsync(socket, commandId++, "Log.enable", null, cancellationToken);
            await SendCdpCommandAsync(socket, commandId++, "Network.enable", null, cancellationToken);
            await SendCdpCommandAsync(socket, commandId++, "Network.setCacheDisabled", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["cacheDisabled"] = true
            }, cancellationToken);
            await SendCdpCommandAsync(socket, commandId++, "Page.navigate", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["url"] = url
            }, cancellationToken);

            var loaded = false;
            var quietUntil = DateTimeOffset.UtcNow.AddSeconds(2);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            var errors = new List<string>();
            var requestUrls = new Dictionary<string, string>(StringComparer.Ordinal);
            var sawSuccessfulDocumentResponse = false;

            while (DateTimeOffset.UtcNow < deadline)
            {
                var message = await ReadWebSocketMessageAsync(socket, TimeSpan.FromMilliseconds(500), cancellationToken);
                if (string.IsNullOrWhiteSpace(message))
                {
                    if (loaded && DateTimeOffset.UtcNow >= quietUntil)
                    {
                        break;
                    }

                    continue;
                }

                using var payload = JsonDocument.Parse(message);
                if (!payload.RootElement.TryGetProperty("method", out var methodElement))
                {
                    continue;
                }

                var method = methodElement.GetString();
                if (string.IsNullOrWhiteSpace(method))
                {
                    continue;
                }

                var hasParams = payload.RootElement.TryGetProperty("params", out var paramsElement);

                switch (method)
                {
                    case "Page.loadEventFired":
                    case "Page.frameStoppedLoading":
                        loaded = true;
                        quietUntil = DateTimeOffset.UtcNow.AddSeconds(2);
                        break;
                    case "Runtime.exceptionThrown":
                        if (hasParams)
                        {
                            var description = TryGetNestedString(paramsElement, "exceptionDetails", "exception", "description")
                                ?? TryGetNestedString(paramsElement, "exceptionDetails", "text");
                            if (!string.IsNullOrWhiteSpace(description))
                            {
                                errors.Add($"Runtime exception: {description}");
                                quietUntil = DateTimeOffset.UtcNow.AddSeconds(2);
                            }
                        }

                        break;
                    case "Network.requestWillBeSent":
                        if (hasParams)
                        {
                            var requestId = TryGetNestedString(paramsElement, "requestId");
                            var requestUrl = TryGetNestedString(paramsElement, "request", "url");
                            if (!string.IsNullOrWhiteSpace(requestId) && !string.IsNullOrWhiteSpace(requestUrl))
                            {
                                requestUrls[requestId] = requestUrl;
                            }
                        }

                        break;
                    case "Network.responseReceived":
                        if (hasParams)
                        {
                            var status = TryGetNestedDouble(paramsElement, "response", "status");
                            var responseUrl = TryGetNestedString(paramsElement, "response", "url");
                            var resourceType = TryGetNestedString(paramsElement, "type");
                            if (string.Equals(resourceType, "Document", StringComparison.OrdinalIgnoreCase) && status is >= 200 and < 400)
                            {
                                sawSuccessfulDocumentResponse = true;
                            }

                            if (status >= 400
                                && !string.IsNullOrWhiteSpace(responseUrl)
                                && !ShouldIgnoreBrowserResourceError(responseUrl))
                            {
                                errors.Add($"HTTP {(int)status}: {responseUrl}");
                                quietUntil = DateTimeOffset.UtcNow.AddSeconds(2);
                            }
                        }

                        break;
                    case "Runtime.consoleAPICalled":
                        if (hasParams)
                        {
                            var type = TryGetNestedString(paramsElement, "type");
                            if (string.Equals(type, "error", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(type, "assert", StringComparison.OrdinalIgnoreCase))
                            {
                                var values = new List<string>();
                                if (paramsElement.TryGetProperty("args", out var argsElement)
                                    && argsElement.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var arg in argsElement.EnumerateArray())
                                    {
                                        var value = TryGetNestedString(arg, "value") ?? TryGetNestedString(arg, "description");
                                        if (!string.IsNullOrWhiteSpace(value))
                                        {
                                            values.Add(value);
                                        }
                                    }
                                }

                                if (values.Count > 0)
                                {
                                    errors.Add($"Console {type}: {string.Join(' ', values)}");
                                    quietUntil = DateTimeOffset.UtcNow.AddSeconds(2);
                                }
                            }
                        }

                        break;
                    case "Log.entryAdded":
                        if (hasParams)
                        {
                            var level = TryGetNestedString(paramsElement, "entry", "level");
                            if (string.Equals(level, "error", StringComparison.OrdinalIgnoreCase))
                            {
                                var text = TryGetNestedString(paramsElement, "entry", "text") ?? "Unknown log error";
                                if (!text.Contains("Failed to load resource", StringComparison.OrdinalIgnoreCase))
                                {
                                    errors.Add($"Log error: {text}");
                                    quietUntil = DateTimeOffset.UtcNow.AddSeconds(2);
                                }
                            }
                        }

                        break;
                    case "Network.loadingFailed":
                        if (hasParams)
                        {
                            var canceled = paramsElement.TryGetProperty("canceled", out var canceledElement)
                                && canceledElement.ValueKind == JsonValueKind.True;
                            if (!canceled)
                            {
                                var errorText = TryGetNestedString(paramsElement, "errorText") ?? "Unknown network failure";
                                var requestId = TryGetNestedString(paramsElement, "requestId") ?? "n/a";
                                requestUrls.TryGetValue(requestId, out var requestUrl);
                                if (string.IsNullOrWhiteSpace(requestUrl) || !ShouldIgnoreBrowserResourceError(requestUrl))
                                {
                                    var suffix = string.IsNullOrWhiteSpace(requestUrl) ? requestId : requestUrl;
                                    errors.Add($"Network failure: {errorText} ({suffix})");
                                    quietUntil = DateTimeOffset.UtcNow.AddSeconds(2);
                                }
                            }
                        }

                        break;
                }
            }

            if (!loaded && sawSuccessfulDocumentResponse && errors.Count == 0)
            {
                loaded = true;
            }

            return new BrowserCheckResult(name, url, loaded, errors);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            try
            {
                if (Directory.Exists(userDataDirectory))
                {
                    Directory.Delete(userDataDirectory, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static async Task<string?> WaitForDevToolsPageWebSocketAsync(int debugPort, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        for (var attempt = 0; attempt < 50; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await httpClient.GetStringAsync($"http://127.0.0.1:{debugPort}/json/list", cancellationToken);
                using var payload = JsonDocument.Parse(json);
                if (payload.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in payload.RootElement.EnumerateArray())
                {
                    var type = TryGetNestedString(item, "type");
                    if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var webSocketUrl = TryGetNestedString(item, "webSocketDebuggerUrl");
                    if (!string.IsNullOrWhiteSpace(webSocketUrl))
                    {
                        return webSocketUrl;
                    }
                }
            }
            catch
            {
            }

            await Task.Delay(200, cancellationToken);
        }

        return null;
    }

    private static async Task SendCdpCommandAsync(
        ClientWebSocket socket,
        int id,
        string method,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["method"] = method
        };

        if (parameters is not null && parameters.Count > 0)
        {
            payload["params"] = parameters;
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private static async Task<string?> ReadWebSocketMessageAsync(
        ClientWebSocket socket,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        await using var stream = new MemoryStream();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (true)
            {
                var segment = new ArraySegment<byte>(buffer);
                var result = await socket.ReceiveAsync(segment, timeoutCts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.Count > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
                }

                if (result.EndOfMessage)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (WebSocketException) when (socket.State is WebSocketState.Aborted or WebSocketState.Closed)
        {
            return null;
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? TryGetNestedString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static double TryGetNestedDouble(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
            {
                return double.NaN;
            }
        }

        return current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out var value)
            ? value
            : double.NaN;
    }

    private static bool ShouldIgnoreBrowserResourceError(string resourceUrl)
    {
        if (!Uri.TryCreate(resourceUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.EndsWith("/favicon.ico", StringComparison.Ordinal)
            || path.EndsWith(".map", StringComparison.Ordinal)
            || path.EndsWith("/apple-touch-icon.png", StringComparison.Ordinal);
    }

    private static async Task<ProcessExecutionResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        var output = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                output.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                output.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessExecutionResult(process.ExitCode, output.ToString().TrimEnd());
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "JekyllNet.slnx")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the current application base directory.");
    }

    internal sealed record ThemeBuildTarget(string Name, string Source, string Destination);

    private sealed record ThemeBuildResult(
        string Name,
        string Source,
        string Destination,
        int ExitCode,
        TimeSpan Duration,
        string Output);

    private sealed record BrowserCheckResult(
        string Name,
        string Url,
        bool Loaded,
        IReadOnlyList<string> Errors,
        bool Skipped = false,
        string? SkipReason = null);

    private sealed record ProcessExecutionResult(int ExitCode, string Output);

    private sealed class StaticSiteServer(string rootPath, int port, string? mountPrefix) : IAsyncDisposable
    {
        private readonly string rootPath = Path.GetFullPath(rootPath);
        private readonly string? mountPrefix = string.IsNullOrWhiteSpace(mountPrefix)
            ? null
            : "/" + mountPrefix.Trim('/');
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource cancellation = new();
        private Task? serverTask;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            serverTask = Task.Run(() => ListenLoopAsync(cancellation.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            try
            {
                listener.Stop();
                listener.Close();
            }
            catch
            {
            }

            if (serverTask is not null)
            {
                try
                {
                    await serverTask;
                }
                catch
                {
                }
            }

            cancellation.Dispose();
        }

        private async Task ListenLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync();
                    await HandleRequestAsync(context, cancellationToken);
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    if (context is not null)
                    {
                        try
                        {
                            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                            context.Response.Close();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var requestPath = context.Request.Url?.AbsolutePath ?? "/";
            var decodedPath = Uri.UnescapeDataString(requestPath);
            if (!string.IsNullOrWhiteSpace(mountPrefix)
                && decodedPath.StartsWith(mountPrefix, StringComparison.OrdinalIgnoreCase))
            {
                decodedPath = decodedPath[mountPrefix.Length..];
                if (string.IsNullOrWhiteSpace(decodedPath))
                {
                    decodedPath = "/";
                }
            }

            var relativePath = decodedPath.TrimStart('/');

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                relativePath = "index.html";
            }

            var localPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var resolvedPath = Path.Combine(rootPath, localPath);
            if (Directory.Exists(resolvedPath))
            {
                resolvedPath = Path.Combine(resolvedPath, "index.html");
            }

            resolvedPath = Path.GetFullPath(resolvedPath);
            if (!resolvedPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.Close();
                return;
            }

            if (!File.Exists(resolvedPath))
            {
                if (string.Equals(decodedPath, "/favicon.ico", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                    context.Response.Close();
                    return;
                }

                var notFoundPath = Path.Combine(rootPath, "404.html");
                if (File.Exists(notFoundPath))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    resolvedPath = notFoundPath;
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    var notFoundBytes = Encoding.UTF8.GetBytes("Not Found");
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.OutputStream.WriteAsync(notFoundBytes, cancellationToken);
                    context.Response.Close();
                    return;
                }
            }

            var bytes = await File.ReadAllBytesAsync(resolvedPath, cancellationToken);
            context.Response.ContentType = ResolveMimeType(resolvedPath);
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
        }

        private static string ResolveMimeType(string path)
            => Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".css" => "text/css; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".json" => "application/json; charset=utf-8",
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".ico" => "image/x-icon",
                ".xml" => "application/xml; charset=utf-8",
                ".txt" => "text/plain; charset=utf-8",
                _ => "text/html; charset=utf-8"
            };
    }
}

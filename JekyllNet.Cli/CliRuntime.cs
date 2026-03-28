using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using JekyllNet.Core.Models;
using JekyllNet.Core.Services;

namespace JekyllNet.Cli;

internal sealed record BuildCommandSettings(
    string SourceDirectory,
    string DestinationDirectory,
    bool IncludeDrafts,
    bool IncludeFuture,
    bool IncludeUnpublished,
    int? PostsPerPage,
    bool WriteGitHubOutputDestination,
    bool VerboseLogging);

internal sealed record ServeCommandSettings(
    BuildCommandSettings Build,
    string Host,
    int Port,
    bool Watch);

internal static class CliRuntime
{
    private const string DocumentationUrl = "https://jekyllnet.help/";
    private const string RepositoryUrl = "https://github.com/JekyllNet/JekyllNet";

    public static async Task BuildOnceAsync(BuildCommandSettings settings, TextWriter output, CancellationToken cancellationToken)
    {
        var builder = new JekyllSiteBuilder();
        try
        {
            await output.WriteLineAsync($"JekyllNet {GetProductVersion()} | docs: {DocumentationUrl} | repo: {RepositoryUrl}");
            await builder.BuildAsync(ToSiteOptions(settings, output), cancellationToken);
            await WriteGitHubOutputAsync(settings, cancellationToken);
            await output.WriteLineAsync($"Build complete: {settings.DestinationDirectory}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await output.WriteLineAsync($"Build failed: {ex.Message}");
            throw;
        }
    }

    public static async Task WatchAsync(BuildCommandSettings settings, TextWriter output, CancellationToken cancellationToken)
    {
        await BuildOnceAsync(settings, output, cancellationToken);
        await output.WriteLineAsync($"Watching for changes: {settings.SourceDirectory}");
        await RunWatchLoopAsync(settings, output, cancellationToken);
    }

    public static async Task ServeAsync(ServeCommandSettings settings, TextWriter output, CancellationToken cancellationToken)
    {
        await BuildOnceAsync(settings.Build, output, cancellationToken);

        using var watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watchTask = settings.Watch
            ? RunWatchLoopAsync(settings.Build, output, watchCancellation.Token)
            : Task.CompletedTask;

        var webBuilder = WebApplication.CreateSlimBuilder();
        webBuilder.WebHost.UseUrls(BuildListenUrl(settings.Host, settings.Port));

        var app = webBuilder.Build();
        ConfigureStaticSiteMiddleware(app, settings.Build.DestinationDirectory);

        await app.StartAsync(cancellationToken);
        await output.WriteLineAsync($"Serving {settings.Build.DestinationDirectory} at {BuildDisplayUrl(settings.Host, settings.Port)}");
        if (settings.Watch)
        {
            await output.WriteLineAsync("Watch mode is enabled.");
        }

        try
        {
            await app.WaitForShutdownAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            watchCancellation.Cancel();
            try
            {
                await watchTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static JekyllSiteOptions ToSiteOptions(BuildCommandSettings settings, TextWriter output)
        => new()
        {
            SourceDirectory = settings.SourceDirectory,
            DestinationDirectory = settings.DestinationDirectory,
            IncludeDrafts = settings.IncludeDrafts,
            IncludeFuture = settings.IncludeFuture,
            IncludeUnpublished = settings.IncludeUnpublished,
            PostsPerPage = settings.PostsPerPage,
            VerboseLogging = settings.VerboseLogging,
            Log = message => output.WriteLine(message)
        };

    private static string GetProductVersion()
    {
        var version = typeof(CliRuntime).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? typeof(CliRuntime).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        return version.Split('+', 2)[0];
    }

    private static async Task WriteGitHubOutputAsync(BuildCommandSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.WriteGitHubOutputDestination)
        {
            return;
        }

        var githubOutputPath = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
        if (string.IsNullOrWhiteSpace(githubOutputPath))
        {
            return;
        }

        var outputDirectory = Path.GetDirectoryName(githubOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await using var stream = new FileStream(githubOutputPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream);
        await writer.WriteLineAsync($"destination={settings.DestinationDirectory}".AsMemory(), cancellationToken);
    }

    private static async Task RunWatchLoopAsync(BuildCommandSettings settings, TextWriter output, CancellationToken cancellationToken)
    {
        using var watcher = new FileSystemWatcher(settings.SourceDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        var signal = new SemaphoreSlim(0);
        var sync = new object();
        var lastChangeAt = DateTimeOffset.MinValue;
        var latestChangedPath = string.Empty;
        var buildPending = false;

        void QueueBuild(string fullPath)
        {
            if (ShouldIgnoreWatchPath(fullPath, settings))
            {
                return;
            }

            lock (sync)
            {
                buildPending = true;
                lastChangeAt = DateTimeOffset.UtcNow;
                latestChangedPath = Path.GetRelativePath(settings.SourceDirectory, fullPath);
            }

            signal.Release();
        }

        watcher.Changed += (_, args) => QueueBuild(args.FullPath);
        watcher.Created += (_, args) => QueueBuild(args.FullPath);
        watcher.Deleted += (_, args) => QueueBuild(args.FullPath);
        watcher.Renamed += (_, args) => QueueBuild(args.FullPath);
        watcher.EnableRaisingEvents = true;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await signal.WaitAsync(cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(300, cancellationToken);

                    string changedPath;
                    lock (sync)
                    {
                        if (!buildPending)
                        {
                            break;
                        }

                        if (DateTimeOffset.UtcNow - lastChangeAt < TimeSpan.FromMilliseconds(300))
                        {
                            continue;
                        }

                        buildPending = false;
                        changedPath = latestChangedPath;
                    }

                    try
                    {
                        await output.WriteLineAsync($"Change detected: {changedPath}");
                        await BuildOnceAsync(settings, output, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        await output.WriteLineAsync($"Build failed: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void ConfigureStaticSiteMiddleware(WebApplication app, string siteRoot)
    {
        var fileProvider = new PhysicalFileProvider(siteRoot);
        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";

        app.Use(async (context, next) =>
        {
            var requestPath = context.Request.Path.Value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(requestPath)
                && !requestPath.EndsWith("/", StringComparison.Ordinal)
                && !Path.HasExtension(requestPath))
            {
                var candidateDirectory = Path.Combine(siteRoot, requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(candidateDirectory))
                {
                    context.Response.Redirect(requestPath + "/" + context.Request.QueryString, permanent: false);
                    return;
                }
            }

            await next(context);
        });

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            ContentTypeProvider = contentTypeProvider
        });

        app.Run(async context =>
        {
            var notFoundPath = Path.Combine(siteRoot, "404.html");
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            if (File.Exists(notFoundPath))
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(notFoundPath, context.RequestAborted);
                return;
            }

            await context.Response.WriteAsync("Not Found", context.RequestAborted);
        });
    }

    private static bool ShouldIgnoreWatchPath(string fullPath, BuildCommandSettings settings)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        var destinationPath = Path.GetFullPath(settings.DestinationDirectory);
        var sourceCachePath = Path.Combine(Path.GetFullPath(settings.SourceDirectory), ".jekyllnet");

        return IsUnderPath(normalizedPath, destinationPath)
            || IsUnderPath(normalizedPath, sourceCachePath)
            || normalizedPath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderPath(string candidatePath, string rootPath)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(candidatePath);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(rootPath);

        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildListenUrl(string host, int port)
        => $"http://{host}:{port}";

    private static string BuildDisplayUrl(string host, int port)
        => $"http://{(string.Equals(host, "0.0.0.0", StringComparison.Ordinal) ? "localhost" : host)}:{port}";
}

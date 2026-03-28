using System.CommandLine;
using JekyllNet.Cli;

var buildCommand = CreateBuildCommand();
var watchCommand = CreateWatchCommand();
var serveCommand = CreateServeCommand();

var rootCommand = new RootCommand("JekyllNet CLI")
{
    buildCommand,
    watchCommand,
    serveCommand
};

return await rootCommand.Parse(args).InvokeAsync();

static Command CreateBuildCommand()
{
    var sourceOption = CreateSourceOption();
    var destinationOption = CreateDestinationOption(sourceOption);
    var draftsOption = CreateDraftsOption();
    var futureOption = CreateFutureOption();
    var unpublishedOption = CreateUnpublishedOption();
    var postsPerPageOption = CreatePostsPerPageOption();
    var verboseOption = CreateVerboseOption();
    var githubOutputDestinationOption = CreateGitHubOutputDestinationOption();

    var buildCommand = new Command("build", "Build a Jekyll-compatible site")
    {
        sourceOption,
        destinationOption,
        draftsOption,
        futureOption,
        unpublishedOption,
        postsPerPageOption,
        verboseOption,
        githubOutputDestinationOption
    };

    buildCommand.SetAction(async parseResult =>
    {
        var settings = ReadBuildSettings(parseResult, sourceOption, destinationOption, draftsOption, futureOption, unpublishedOption, postsPerPageOption, verboseOption, githubOutputDestinationOption);
        await CliRuntime.BuildOnceAsync(settings, parseResult.InvocationConfiguration.Output, CancellationToken.None);
    });

    return buildCommand;
}

static Command CreateWatchCommand()
{
    var sourceOption = CreateSourceOption();
    var destinationOption = CreateDestinationOption(sourceOption);
    var draftsOption = CreateDraftsOption();
    var futureOption = CreateFutureOption();
    var unpublishedOption = CreateUnpublishedOption();
    var postsPerPageOption = CreatePostsPerPageOption();
    var verboseOption = CreateVerboseOption();

    var watchCommand = new Command("watch", "Rebuild the site when source files change")
    {
        sourceOption,
        destinationOption,
        draftsOption,
        futureOption,
        unpublishedOption,
        postsPerPageOption,
        verboseOption
    };

    watchCommand.SetAction(async parseResult =>
    {
        var settings = ReadBuildSettings(parseResult, sourceOption, destinationOption, draftsOption, futureOption, unpublishedOption, postsPerPageOption, verboseOption);
        using var cancellation = CreateConsoleCancellationTokenSource();
        await CliRuntime.WatchAsync(settings, parseResult.InvocationConfiguration.Output, cancellation.Token);
    });

    return watchCommand;
}

static Command CreateServeCommand()
{
    var sourceOption = CreateSourceOption();
    var destinationOption = CreateDestinationOption(sourceOption);
    var draftsOption = CreateDraftsOption();
    var futureOption = CreateFutureOption();
    var unpublishedOption = CreateUnpublishedOption();
    var postsPerPageOption = CreatePostsPerPageOption();
    var verboseOption = CreateVerboseOption();
    var hostOption = CreateHostOption();
    var portOption = CreatePortOption();
    var noWatchOption = CreateNoWatchOption();

    var serveCommand = new Command("serve", "Build, serve, and optionally watch a Jekyll-compatible site")
    {
        sourceOption,
        destinationOption,
        draftsOption,
        futureOption,
        unpublishedOption,
        postsPerPageOption,
        verboseOption,
        hostOption,
        portOption,
        noWatchOption
    };

    serveCommand.SetAction(async parseResult =>
    {
        var buildSettings = ReadBuildSettings(parseResult, sourceOption, destinationOption, draftsOption, futureOption, unpublishedOption, postsPerPageOption, verboseOption);
        var serveSettings = new ServeCommandSettings(
            buildSettings,
            parseResult.GetValue(hostOption) ?? "localhost",
            parseResult.GetValue(portOption),
            !parseResult.GetValue(noWatchOption));

        using var cancellation = CreateConsoleCancellationTokenSource();
        await CliRuntime.ServeAsync(serveSettings, parseResult.InvocationConfiguration.Output, cancellation.Token);
    });

    return serveCommand;
}

static Option<DirectoryInfo?> CreateSourceOption()
{
    var option = new Option<DirectoryInfo?>("--source")
    {
        DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory())
    };
    option.Description = "Jekyll site source directory";
    return option;
}

static Option<DirectoryInfo?> CreateDestinationOption(Option<DirectoryInfo?> sourceOption)
{
    var option = new Option<DirectoryInfo?>("--destination")
    {
        DefaultValueFactory = result => new DirectoryInfo(Path.Combine(result.GetValue(sourceOption)?.FullName ?? Directory.GetCurrentDirectory(), "_site"))
    };
    option.Description = "Build output directory";
    return option;
}

static Option<bool> CreateDraftsOption()
{
    var option = new Option<bool>("--drafts");
    option.Description = "Include content from _drafts";
    return option;
}

static Option<bool> CreateFutureOption()
{
    var option = new Option<bool>("--future");
    option.Description = "Include content dated in the future";
    return option;
}

static Option<bool> CreateUnpublishedOption()
{
    var option = new Option<bool>("--unpublished");
    option.Description = "Include content with published: false";
    return option;
}

static Option<int?> CreatePostsPerPageOption()
{
    var option = new Option<int?>("--posts-per-page");
    option.Description = "Override pagination page size";
    return option;
}

static Option<bool> CreateVerboseOption()
{
    var option = new Option<bool>("--verbose");
    option.Description = "Write per-file build progress logs";
    return option;
}

static Option<bool> CreateGitHubOutputDestinationOption()
{
    var option = new Option<bool>("--github-output-destination");
    option.Description = "Write the generated destination path to GITHUB_OUTPUT using the 'destination' output name";
    option.Hidden = true;
    return option;
}

static Option<string?> CreateHostOption()
{
    var option = new Option<string?>("--host")
    {
        DefaultValueFactory = _ => "localhost"
    };
    option.Description = "Host interface for the local development server";
    return option;
}

static Option<int> CreatePortOption()
{
    var option = new Option<int>("--port")
    {
        DefaultValueFactory = _ => 4000
    };
    option.Description = "Port for the local development server";
    return option;
}

static Option<bool> CreateNoWatchOption()
{
    var option = new Option<bool>("--no-watch");
    option.Description = "Disable rebuild-on-change while serving";
    return option;
}

static BuildCommandSettings ReadBuildSettings(
    ParseResult parseResult,
    Option<DirectoryInfo?> sourceOption,
    Option<DirectoryInfo?> destinationOption,
    Option<bool> draftsOption,
    Option<bool> futureOption,
    Option<bool> unpublishedOption,
    Option<int?> postsPerPageOption,
    Option<bool> verboseOption,
    Option<bool>? githubOutputDestinationOption = null)
{
    var source = parseResult.GetValue(sourceOption)?.FullName ?? Directory.GetCurrentDirectory();
    var destination = parseResult.GetValue(destinationOption)?.FullName ?? Path.Combine(source, "_site");
    var writeGitHubOutputDestination = githubOutputDestinationOption is not null && parseResult.GetValue(githubOutputDestinationOption);

    return new BuildCommandSettings(
        source,
        destination,
        parseResult.GetValue(draftsOption),
        parseResult.GetValue(futureOption),
        parseResult.GetValue(unpublishedOption),
        parseResult.GetValue(postsPerPageOption),
        writeGitHubOutputDestination,
        parseResult.GetValue(verboseOption));
}

static CancellationTokenSource CreateConsoleCancellationTokenSource()
{
    var cancellation = new CancellationTokenSource();

    Console.CancelKeyPress += HandleCancelKeyPress;
    cancellation.Token.Register(() => Console.CancelKeyPress -= HandleCancelKeyPress);
    return cancellation;

    void HandleCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        cancellation.Cancel();
    }
}

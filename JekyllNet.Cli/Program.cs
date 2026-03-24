using System.CommandLine;
using JekyllNet.Core.Models;
using JekyllNet.Core.Services;

var sourceOption = new Option<DirectoryInfo?>("--source")
{
	DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory())
};
sourceOption.Description = "Jekyll site source directory";

var destinationOption = new Option<DirectoryInfo?>("--destination")
{
	DefaultValueFactory = result => new DirectoryInfo(Path.Combine(result.GetValue(sourceOption)?.FullName ?? Directory.GetCurrentDirectory(), "_site"))
};
destinationOption.Description = "Build output directory";

var draftsOption = new Option<bool>("--drafts");
draftsOption.Description = "Include content from _drafts";

var futureOption = new Option<bool>("--future");
futureOption.Description = "Include content dated in the future";

var unpublishedOption = new Option<bool>("--unpublished");
unpublishedOption.Description = "Include content with published: false";

var buildCommand = new Command("build", "Build a Jekyll-compatible site")
{
	sourceOption,
	destinationOption,
	draftsOption,
	futureOption,
	unpublishedOption
};

buildCommand.SetAction(async parseResult =>
{
	var source = parseResult.GetValue(sourceOption)?.FullName ?? Directory.GetCurrentDirectory();
	var destination = parseResult.GetValue(destinationOption)?.FullName ?? Path.Combine(source, "_site");

	var builder = new JekyllSiteBuilder();
	await builder.BuildAsync(new JekyllSiteOptions
	{
		SourceDirectory = source,
		DestinationDirectory = destination,
		IncludeDrafts = parseResult.GetValue(draftsOption),
		IncludeFuture = parseResult.GetValue(futureOption),
		IncludeUnpublished = parseResult.GetValue(unpublishedOption)
	});

	parseResult.InvocationConfiguration.Output.WriteLine($"Build complete: {destination}");
});

var rootCommand = new RootCommand("Jekyll.Net CLI")
{
	buildCommand
};

return await rootCommand.Parse(args).InvokeAsync();

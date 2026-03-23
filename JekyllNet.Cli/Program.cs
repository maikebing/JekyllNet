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

var buildCommand = new Command("build", "Build a Jekyll-compatible site")
{
	sourceOption,
	destinationOption
};

buildCommand.SetAction(async parseResult =>
{
	var source = parseResult.GetValue(sourceOption)?.FullName ?? Directory.GetCurrentDirectory();
	var destination = parseResult.GetValue(destinationOption)?.FullName ?? Path.Combine(source, "_site");

	var builder = new JekyllSiteBuilder();
	await builder.BuildAsync(new JekyllSiteOptions
	{
		SourceDirectory = source,
		DestinationDirectory = destination
	});

	parseResult.InvocationConfiguration.Output.WriteLine($"Build complete: {destination}");
});

var rootCommand = new RootCommand("Jekyll.Net CLI")
{
	buildCommand
};

return await rootCommand.Parse(args).InvokeAsync();

namespace JekyllNet.Tests;

public sealed class SnapshotRegressionTests
{
    [Fact]
    public async Task SampleSite_OutputMatchesSnapshot()
    {
        var sourceDirectory = TestInfrastructure.GetRepoPath("sample-site");
        var expectedDirectory = TestInfrastructure.GetRepoPath("JekyllNet.Tests", "Snapshots", "sample-site");
        var actualDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);

        TestInfrastructure.AssertSnapshotDirectory(expectedDirectory, actualDirectory);
    }

    [Fact]
    public async Task DocsSite_OutputMatchesSnapshot()
    {
        var sourceDirectory = TestInfrastructure.GetRepoPath("docs");
        var expectedDirectory = TestInfrastructure.GetRepoPath("JekyllNet.Tests", "Snapshots", "docs");
        var actualDirectory = await TestInfrastructure.BuildSiteAsync(sourceDirectory);

        TestInfrastructure.AssertSnapshotDirectory(expectedDirectory, actualDirectory);
    }
}

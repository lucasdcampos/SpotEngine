using Spot.Build;
using Spot.Engine;

namespace Spot.Build.Tests;

public class ProjectBuilderTests
{
    [Theory]
    [InlineData(BuildPlatform.Windows, "win-x64")]
    [InlineData(BuildPlatform.Linux, "linux-x64")]
    public void RuntimeIdentifier_MapsPlatformToRid(BuildPlatform platform, string expected)
    {
        Assert.Equal(expected, ProjectBuilder.RuntimeIdentifier(platform));
    }

    [Theory]
    [InlineData(BuildPlatform.Windows, false, "windows")]
    [InlineData(BuildPlatform.Linux, false, "linux")]
    [InlineData(BuildPlatform.Mac, false, "mac")]
    [InlineData(BuildPlatform.Windows, true, "play")]
    public void OutputDirectory_IsAbsoluteEvenForARelativeProject(BuildPlatform platform, bool fastDebug, string folder)
    {
        string output = ProjectBuilder.OutputDirectory("sandbox", platform, fastDebug);

        // A relative path here used to nest under the publish working directory (sandbox/sandbox/Build/...).
        Assert.True(Path.IsPathRooted(output));
        Assert.Equal(Path.GetFullPath(Path.Combine("sandbox", "Build", folder)), output);
    }

    [Fact]
    public void BuildResult_CarriesItsValues()
    {
        var result = new BuildResult(true, 0, "/tmp/out");

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("/tmp/out", result.OutputDir);
        Assert.Equal(new BuildResult(true, 0, "/tmp/out"), result); // value equality
    }

    [Fact]
    public void Build_WithoutProjectDirectory_FailsFastWithoutSpawningDotnet()
    {
        // A project with no directory on disk must report the error through the callback and return a
        // failure result instead of attempting a real 'dotnet publish'.
        Project.New();
        Project.Active!.Config.Name = "NoDir";
        Project.Active.ProjectDirectory = string.Empty;

        string? reportedError = null;
        var result = ProjectBuilder.Build(
            Project.Active,
            BuildPlatform.Windows,
            onOutput: _ => { },
            onError: msg => reportedError = msg);

        Assert.False(result.Success);
        Assert.Equal(-1, result.ExitCode);
        Assert.NotNull(reportedError);
    }

    [Fact(Skip = "integration: runs a real 'dotnet publish' and needs the SDK + engine DLL on disk")]
    public void Build_PublishesSelfContainedApp()
    {
        // Documented out of scope for unit tests; covered by manual/integration runs of `spot build`.
    }
}

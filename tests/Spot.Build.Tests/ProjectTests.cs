using System.IO;
using Spot.Build;
using Spot.Core;
using Xunit;

namespace Spot.Build.Tests;

public class ProjectTests
{
    [Fact]
    public void Project_ConfigRoundTripsThroughJson()
    {
        using var temp = new TempDir();
        string sptproj = Path.Combine(temp.Path, "Game.sptproj");

        Project.New();
        Project.Active!.Config.Name = "My Game";
        Project.Active.Config.StartScene = "Scenes/Level1.sptscene";
        Project.Active.Config.AssetDirectory = "Content";
        Project.SaveActive(sptproj);

        var reloaded = Project.Load(sptproj);

        Assert.NotNull(reloaded);
        Assert.Equal("My Game", reloaded!.Config.Name);
        Assert.Equal("Scenes/Level1.sptscene", reloaded.Config.StartScene);
        Assert.Equal("Content", reloaded.Config.AssetDirectory);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsNullWithoutThrowing()
    {
        using var temp = new TempDir();
        string sptproj = Path.Combine(temp.Path, "Broken.sptproj");
        File.WriteAllText(sptproj, "{ this is not valid json ");

        // A corrupt .sptproj must be diagnosed (logged) and reported as a null load, never crash the caller.
        var loaded = Project.Load(sptproj);

        Assert.Null(loaded);
    }
}

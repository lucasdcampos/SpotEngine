namespace Spot.Engine.Tests;

/// <summary>
/// Where a game's cooked content is looked up: beside the <c>game.manifest</c> it loaded, which is not the
/// executable's folder under <c>spot run</c> (the game runs from <c>Build/run</c>, its executable stays in
/// <c>bin/</c>).
/// </summary>
public class ApplicationSpecTests
{
    [Fact]
    public void Load_RecordsTheManifestFolderAsTheContentBase()
    {
        string folder = Path.Combine(Path.GetTempPath(), "spot-spec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string manifest = Path.Combine(folder, "game.manifest");
            File.WriteAllText(manifest, """{ "Name": "Game", "ContentDirectory": "Content" }""");

            ApplicationSpec spec = ApplicationSpec.Load(manifest);

            Assert.Equal("Game", spec.Name);
            Assert.Equal(Path.GetFullPath(folder), spec.BaseDirectory);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Load_WithoutAFile_LeavesTheBaseToTheExecutableFolder()
    {
        ApplicationSpec spec = ApplicationSpec.Load(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N"), "game.manifest"));

        Assert.Null(spec.BaseDirectory);
    }

    [Fact]
    public void LocateManifest_PrefersTheWorkingDirectory()
    {
        using var working = new Spot.Tests.TempDir();
        using var executable = new Spot.Tests.TempDir();
        File.WriteAllText(Path.Combine(working.Path, ApplicationSpec.ManifestFileName), "{}");
        File.WriteAllText(Path.Combine(executable.Path, ApplicationSpec.ManifestFileName), "{}");

        Assert.Equal(Path.Combine(working.Path, ApplicationSpec.ManifestFileName),
            ApplicationSpec.LocateManifest(working.Path, executable.Path));
    }

    [Fact]
    public void LocateManifest_FallsBackToTheExecutablesFolder()
    {
        // A build started from a shortcut or another folder still finds the manifest that ships beside it.
        using var working = new Spot.Tests.TempDir();
        using var executable = new Spot.Tests.TempDir();
        File.WriteAllText(Path.Combine(executable.Path, ApplicationSpec.ManifestFileName), """{ "StartScene": "Scenes/Main.sptscene" }""");

        string path = ApplicationSpec.LocateManifest(working.Path, executable.Path);

        Assert.Equal(Path.Combine(executable.Path, ApplicationSpec.ManifestFileName), path);
        Assert.Equal("Scenes/Main.sptscene", ApplicationSpec.Load(path).StartScene);
    }

    [Fact]
    public void LocateManifest_WithNoManifestAnywhere_GivesASpecWithNoStartScene()
    {
        using var working = new Spot.Tests.TempDir();
        using var executable = new Spot.Tests.TempDir();

        ApplicationSpec spec = ApplicationSpec.Load(ApplicationSpec.LocateManifest(working.Path, executable.Path));

        Assert.Null(spec.StartScene);
        Assert.Null(spec.BaseDirectory);
    }
}

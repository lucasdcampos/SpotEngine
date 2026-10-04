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
}

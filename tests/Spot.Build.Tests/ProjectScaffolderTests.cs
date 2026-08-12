using System.IO;
using System.Text.Json;
using Spot.Build;

namespace Spot.Build.Tests;

public class ProjectScaffolderTests
{
    [Fact]
    public void Create_WritesFullProjectStructure()
    {
        using var temp = new TempDir();

        string sptproj = ProjectScaffolder.Create("MyGame", temp.Path);

        string projDir = Path.Combine(temp.Path, "MyGame");
        Assert.True(Directory.Exists(projDir));
        Assert.True(Directory.Exists(Path.Combine(projDir, "Assets")));

        // Returns the path to the .sptproj it created.
        Assert.Equal(Path.Combine(projDir, "MyGame.sptproj"), sptproj);
        Assert.True(File.Exists(sptproj));

        // The .sptproj holds the serialized project config.
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(sptproj));
        Assert.Equal("MyGame", doc.RootElement.GetProperty("Name").GetString());
        Assert.Equal("Scenes/Main.sptscene", doc.RootElement.GetProperty("StartScene").GetString());

        // A minimal empty start scene so the project runs immediately.
        string scenePath = Path.Combine(projDir, "Assets", "Scenes", "Main.sptscene");
        Assert.True(File.Exists(scenePath));
        Assert.Contains("\"Entities\"", File.ReadAllText(scenePath));

        // Generated build files and the bundled engine DLL.
        Assert.True(File.Exists(Path.Combine(projDir, "MyGame.csproj")));
        Assert.True(File.Exists(Path.Combine(projDir, "MyGame.sln")));
        Assert.True(File.Exists(Path.Combine(projDir, "Program.cs")));
        Assert.True(File.Exists(Path.Combine(projDir, "EngineBin", "Spot.Engine.dll")));
    }

    [Theory]
    [InlineData("My:Game")]      // invalid path character
    [InlineData("Test*Project")] // invalid path character
    [InlineData("My-Game")]      // valid path segment, but not a valid C# namespace
    [InlineData("2048")]         // starts with a digit
    [InlineData("   ")]          // whitespace only
    public void Create_RejectsInvalidProjectName(string name)
    {
        using var temp = new TempDir();

        // A bad name must fail fast with a clear error instead of scaffolding a broken project.
        Assert.Throws<ArgumentException>(() => ProjectScaffolder.Create(name, temp.Path));
        Assert.Empty(Directory.GetDirectories(temp.Path));
    }
}

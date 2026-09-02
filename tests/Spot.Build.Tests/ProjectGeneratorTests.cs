using System.IO;
using Spot.Build;
using Spot.Core;

namespace Spot.Build.Tests;

public class ProjectGeneratorTests
{
    private static Project NewProjectAt(string dir, string name)
    {
        Project.New();
        Project.Active!.Config.Name = name;
        Project.Active.ProjectDirectory = dir;
        return Project.Active;
    }

    [Fact]
    public void Generate_WritesCsprojSlnAndProgram()
    {
        using var temp = new TempDir();
        var project = NewProjectAt(temp.Path, "Arcade");

        ProjectGenerator.Generate(project);

        string csproj = File.ReadAllText(Path.Combine(temp.Path, "Arcade.csproj"));
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", csproj);
        Assert.Contains(@"EngineBin\Spot.Engine.dll", csproj); // references the copied engine DLL

        // Never compiles the source assets into the game.
        Assert.DoesNotContain(@"Assets\**\*.*", csproj);

        // Cooked content and game.manifest are staged into the build output by the pipeline, not copied by the
        // csproj, so Generate leaves the project root clean (only Build/ is produced by a build).
        Assert.DoesNotContain(@"Content\**\*.*", csproj);
        Assert.DoesNotContain(@"<Content Include=""game.manifest""", csproj);
        Assert.False(File.Exists(Path.Combine(temp.Path, "game.manifest")));

        // Wires the script source generator (Exists-guarded) and feeds it the .cs.meta sidecars.
        Assert.Contains(@"<Analyzer Include=""EngineBin\Spot.ScriptGen.dll"" />", csproj);
        Assert.Contains(@"<AdditionalFiles Include=""Assets\**\*.cs.meta"" />", csproj);

        Assert.True(File.Exists(Path.Combine(temp.Path, "Arcade.sln")));

        string program = File.ReadAllText(Path.Combine(temp.Path, "Program.cs"));
        Assert.Contains("class Program", program);
        Assert.Contains("SpotEngine.CreateApplication()", program);
    }

    [Fact]
    public void WriteManifest_WritesIntoOutputDirAndAlwaysOverwrites()
    {
        using var temp = new TempDir();
        var project = NewProjectAt(temp.Path, "Arcade");
        project.Config.StartScene = "Scenes/Boot.sptscene";

        string outputDir = Path.Combine(temp.Path, "Build", "play");
        ProjectGenerator.WriteManifest(project, outputDir);

        string manifestPath = Path.Combine(outputDir, "game.manifest");
        string manifest = File.ReadAllText(manifestPath);
        Assert.Contains("\"ContentDirectory\": \"Content\"", manifest);
        Assert.Contains("\"ManifestPath\": \"manifest.json\"", manifest);
        Assert.Contains("Scenes/Boot.sptscene", manifest);

        // Nothing is written to the project root.
        Assert.False(File.Exists(Path.Combine(temp.Path, "game.manifest")));

        // A changed start scene is reflected on the next write (no stale-manifest early-out).
        project.Config.StartScene = "Scenes/Level2.sptscene";
        ProjectGenerator.WriteManifest(project, outputDir);
        Assert.Contains("Scenes/Level2.sptscene", File.ReadAllText(manifestPath));
    }

    [Fact]
    public void Generate_SanitizesNamespaceFromProjectName()
    {
        using var temp = new TempDir();
        var project = NewProjectAt(temp.Path, "My Cool Game");

        ProjectGenerator.Generate(project, overwriteProgram: true);

        string program = File.ReadAllText(Path.Combine(temp.Path, "Program.cs"));
        Assert.Contains("namespace MyCoolGame;", program);
    }

    [Fact]
    public void Generate_PreservesProgramUnlessOverwriteRequested()
    {
        using var temp = new TempDir();
        var project = NewProjectAt(temp.Path, "Keeper");
        string programPath = Path.Combine(temp.Path, "Program.cs");
        File.WriteAllText(programPath, "// user edited entry point");

        ProjectGenerator.Generate(project); // overwriteProgram defaults to false
        Assert.Equal("// user edited entry point", File.ReadAllText(programPath));

        ProjectGenerator.Generate(project, overwriteProgram: true);
        Assert.DoesNotContain("user edited entry point", File.ReadAllText(programPath));
        Assert.Contains("class Program", File.ReadAllText(programPath));
    }
}

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

        // Ships cooked content, not source assets.
        Assert.Contains(@"Content\**\*.*", csproj);
        Assert.DoesNotContain(@"Assets\**\*.*", csproj);

        Assert.True(File.Exists(Path.Combine(temp.Path, "Arcade.sln")));

        string program = File.ReadAllText(Path.Combine(temp.Path, "Program.cs"));
        Assert.Contains("class Program", program);
        Assert.Contains("new Application(spec)", program);
        Assert.Contains("ContentDirectory = \"Content\"", program);
        Assert.Contains("ManifestPath = \"manifest.json\"", program);
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

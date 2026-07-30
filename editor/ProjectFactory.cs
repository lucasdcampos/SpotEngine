using System.IO;
using Spot.Core;

namespace Spot.Editor;

/// <summary>
/// Creates the on-disk structure for a new project (folder, Assets directory, .sptproj and .sln)
/// and makes it the active project. Shared by the launcher and the in-editor "New Project" flow.
/// </summary>
public static class ProjectFactory
{
    /// <summary>
    /// Creates a new project under <paramref name="location"/>/<paramref name="name"/>, sets it as
    /// <see cref="Project.Active"/>, and returns the path to the generated .sptproj file.
    /// </summary>
    public static string Create(string name, string location)
    {
        string projDir = Path.Combine(location, name);
        Directory.CreateDirectory(projDir);
        Directory.CreateDirectory(Path.Combine(projDir, "Assets"));

        string sptprojPath = Path.Combine(projDir, name + ".sptproj");

        Project.New();
        Project.Active!.Config.Name = name;
        Project.Active.Config.StartScene = "Scenes/Main.sptscene";
        Project.Active.ProjectDirectory = projDir;
        Project.SaveActive(sptprojPath); // Saves project config and generates .csproj

        string slnContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""{name}"", ""{name}.csproj"", ""{{{System.Guid.NewGuid().ToString().ToUpper()}}}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{{{System.Guid.NewGuid().ToString().ToUpper()}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{{{System.Guid.NewGuid().ToString().ToUpper()}}}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{{{System.Guid.NewGuid().ToString().ToUpper()}}}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{{{System.Guid.NewGuid().ToString().ToUpper()}}}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
";
        File.WriteAllText(Path.Combine(projDir, name + ".sln"), slnContent);

        return sptprojPath;
    }
}

using System;
using System.IO;
using Spot.DebugUI.UI;

namespace Spot.Engine.Tests;

public class ComponentScriptsTests
{
    [Theory]
    [InlineData("player movement", "PlayerMovement")]
    [InlineData("rocket-boots 2", "RocketBoots2")]
    [InlineData("  42 health", "Health")]
    [InlineData("Already_Fine", "Already_Fine")]
    [InlineData("!!", "")]
    public void ToClassName_MakesAnIdentifierFromFreeText(string text, string expected) =>
        Assert.Equal(expected, ComponentScripts.ToClassName(text));

    [Theory]
    [InlineData("PlayerMovement", true)]
    [InlineData("Health2", true)]
    [InlineData("2Health", false)]
    [InlineData("_Hidden", false)]
    [InlineData("Player Movement", false)]
    [InlineData("", false)]
    public void IsValidClassName_AcceptsOnlyIdentifiersStartingWithALetter(string name, bool valid) =>
        Assert.Equal(valid, ComponentScripts.IsValidClassName(name));

    [Fact]
    public void Template_DeclaresAComponentWithTheStartAndUpdateHooks()
    {
        string source = ComponentScripts.Template("PlayerMovement", "MyGame");

        Assert.Contains("namespace MyGame;", source);
        Assert.Contains("public class PlayerMovement : Component", source);
        Assert.Contains("public override void OnStart()", source);
        Assert.Contains("public override void OnUpdate(float deltaTime)", source);
    }

    [Fact]
    public void Create_WritesTheScriptAndItsMetaSidecar_AndRefusesToOverwrite()
    {
        string dir = Path.Combine(Path.GetTempPath(), "spot-component-scripts-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(ComponentScripts.Create("Jetpack", out string path, out string guid, dir));

            Assert.Equal(Path.Combine(dir, "Jetpack.cs"), path);
            Assert.Contains("public class Jetpack : Component", File.ReadAllText(path));
            Assert.True(File.Exists(path + ".meta"));
            Assert.False(string.IsNullOrEmpty(guid));

            Assert.False(ComponentScripts.Create("Jetpack", out _, out _, dir));
            Assert.False(ComponentScripts.Create("Not Valid", out _, out _, dir));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}

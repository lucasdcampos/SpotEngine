using System.Reflection;

namespace Spot.Engine.Tests;

/// <summary>
/// Pins Spot's layering: Core (level 1) ← Framework (level 2) ← Engine (level 3), with dependencies only ever
/// pointing down. A framework assembly that grows a reference to the engine — or the core to the framework —
/// fails here, long before a user who builds on the framework alone hits it.
/// </summary>
public class ArchitectureTests
{
    private const string Core = "Spot.Framework.Core";
    private const string Framework = "Spot.Framework";
    private const string Assimp = "Spot.Framework.Assimp";
    private const string Engine = "Spot.Engine";

    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    private static HashSet<string> SpotReferences(string assembly) =>
        Load(assembly).GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Spot.", StringComparison.Ordinal))
            .ToHashSet();

    private static HashSet<string> AllReferences(string assembly) =>
        Load(assembly).GetReferencedAssemblies().Select(a => a.Name!).ToHashSet();

    [Fact]
    public void Core_ReferencesNoOtherSpotAssembly()
    {
        Assert.Empty(SpotReferences(Core));
    }

    [Fact]
    public void Framework_ReferencesOnlyTheCore()
    {
        Assert.Equal(new HashSet<string> { Core }, SpotReferences(Framework));
    }

    [Fact]
    public void AssimpModule_ReferencesOnlyTheFrameworkLevels()
    {
        Assert.Subset(new HashSet<string> { Core, Framework }, SpotReferences(Assimp));
        Assert.Contains(Framework, SpotReferences(Assimp));
    }

    [Fact]
    public void Engine_IsBuiltOnTheFrameworkLevels()
    {
        HashSet<string> references = SpotReferences(Engine);

        Assert.Contains(Core, references);
        Assert.Contains(Framework, references);
    }

    [Theory]
    [InlineData(Core)]
    [InlineData(Framework)]
    [InlineData(Assimp)]
    public void FrameworkLevels_PickNoEngineLibraries(string assembly)
    {
        // The engine's opinions — its physics libraries, its logging stack, its editor UI — stay out of the
        // framework, so a framework user chooses their own.
        string[] engineOnly = { "BepuPhysics", "BepuUtilities", "Aether.Physics2D", "Serilog", "ImGui.NET" };

        Assert.Empty(AllReferences(assembly).Intersect(engineOnly));
    }

    [Fact]
    public void Core_HasNoDecoders()
    {
        // Decoding files is a framework feature; the core only talks to the OS and the hardware.
        string[] decoders = { "StbImageSharp", "StbTrueTypeSharp", "StbVorbisSharp", "Silk.NET.Assimp" };

        Assert.Empty(AllReferences(Core).Intersect(decoders));
    }

    [Fact]
    public void FrameworkLevels_ExposeNoEngineNamespaces()
    {
        // Types keep their namespaces until the namespace pass; what matters here is that nothing from the engine's
        // own namespaces (scenes, console, editor UI) was moved down by mistake.
        string[] engineNamespaces = { "Spot.Scenes", "Spot.Console", "Spot.UI", "Spot.Core.Services" };
        foreach (string assembly in new[] { Core, Framework, Assimp })
        {
            IEnumerable<string?> namespaces = Load(assembly).GetTypes().Select(t => t.Namespace).Distinct();
            Assert.DoesNotContain(namespaces, ns => ns is not null && engineNamespaces.Any(e => ns == e || ns.StartsWith(e + ".")));
        }
    }
}

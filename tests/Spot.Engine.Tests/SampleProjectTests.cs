using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Spot.Engine.Scenes;
using Spot.Engine.UI;
using Spot.Framework;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Guards the engine-level samples (the <c>.sptproj</c> projects under <c>samples/</c>) against engine changes:
/// their start scene and UI documents must load cleanly, and every script a scene names must be defined in the
/// project. Scripts compile with their project (<c>spot generate</c>/<c>build</c>), not with these tests, so a
/// scene's script types are checked against the project's sources instead of being resolved.
/// </summary>
public partial class SampleProjectTests
{
    public static TheoryData<string> Projects()
    {
        var data = new TheoryData<string>();
        foreach (string file in Directory.EnumerateFiles(SamplesDirectory, "*.sptproj", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Build" + Path.DirectorySeparatorChar))
                     .Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(SamplesDirectory, file));
        }

        return data;
    }

    [Fact]
    public void TheSamplesIncludeAnEngineProject() => Assert.NotEmpty(Projects());

    [Theory]
    [MemberData(nameof(Projects))]
    public void StartScene_LoadsWithoutWarnings(string project)
    {
        string scenePath = StartScenePath(project);
        Assert.True(File.Exists(scenePath), $"{project}: start scene '{scenePath}' is missing.");

        using RecordingLogSink log = RecordingLogSink.Capture();
        var scene = new Scene();
        Assert.True(new SceneSerializer(scene).DeserializeFromString(File.ReadAllText(scenePath)));

        // Script types live in the project, not in this test assembly; those are checked by the next test.
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warn && !e.Message.Contains("Type not found", StringComparison.Ordinal));
        Assert.NotEmpty(scene.View<TransformComponent>());
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void ComponentsNamedByScenes_AreDefinedInTheProject(string project)
    {
        string assets = AssetsDirectory(project);

        // Every class with its base: a component derives from Component directly or through another component.
        var bases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string source in Directory.EnumerateFiles(assets, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in ClassDeclaration().Matches(File.ReadAllText(source)))
            {
                bases[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }

        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in bases.Keys)
        {
            string? current = name;
            for (int depth = 0; current is not null && depth < 16; depth++)
            {
                if (!bases.TryGetValue(current, out string? baseName)) break;
                if (baseName is "Component" or "NetworkBehaviour")
                {
                    defined.Add(name);
                    break;
                }

                current = baseName;
            }
        }

        foreach (string scene in Directory.EnumerateFiles(assets, "*.sptscene", SearchOption.AllDirectories))
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(scene));
            foreach (string type in ComponentTypes(root?["Entities"] as JsonArray))
            {
                Assert.True(defined.Contains(type), $"{Path.GetFileName(scene)} names component '{type}', which no script in {project} defines.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void UIDocuments_LoadWithoutErrors(string project)
    {
        foreach (string document in Directory.EnumerateFiles(AssetsDirectory(project), "*.sptui", SearchOption.AllDirectories))
        {
            using RecordingLogSink log = RecordingLogSink.Capture();

            UIRoot root = UISerializer.Load(document);

            Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warn);
            Assert.NotEmpty(root.Children);
        }
    }

    [GeneratedRegex(@"class\s+(\w+)\s*:\s*([\w.]+)")]
    private static partial Regex ClassDeclaration();

    private static IEnumerable<string> ComponentTypes(JsonArray? entities)
    {
        foreach (JsonNode? entity in entities ?? new JsonArray())
        {
            if (entity?["Components"] is JsonArray items)
            {
                foreach (JsonNode? item in items)
                {
                    if (item?["Type"]?.GetValue<string>() is { Length: > 0 } type)
                    {
                        yield return type;
                    }
                }
            }

            foreach (string type in ComponentTypes(entity?["Children"] as JsonArray))
            {
                yield return type;
            }
        }
    }

    private static string ProjectFile(string project) => Path.Combine(SamplesDirectory, project);

    private static string AssetsDirectory(string project)
    {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(ProjectFile(project)));
        string assets = config.RootElement.TryGetProperty("AssetDirectory", out JsonElement dir) ? dir.GetString() ?? "Assets" : "Assets";
        return Path.Combine(Path.GetDirectoryName(ProjectFile(project))!, assets);
    }

    private static string StartScenePath(string project)
    {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(ProjectFile(project)));
        return Path.Combine(AssetsDirectory(project), config.RootElement.GetProperty("StartScene").GetString()!);
    }

    private static string SamplesDirectory { get; } = FindSamplesDirectory();

    private static string FindSamplesDirectory()
    {
        for (string? dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "SpotEngine.slnx")))
            {
                return Path.Combine(dir, "samples");
            }
        }

        throw new DirectoryNotFoundException("The repository root (SpotEngine.slnx) was not found above the test output.");
    }
}

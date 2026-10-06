using System.IO;
using Spot.Build;

namespace Spot.Build.Tests;

/// <summary>
/// Covers the codemod that moves user scripts from the pre-0.4 namespaces to Spot.Engine.* / Spot.Engine.*,
/// against the real public types of the loaded Spot assemblies.
/// </summary>
public class ScriptNamespaceMigratorTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Types = ScriptNamespaceMigrator.LoadedSpotTypes();

    private static string Migrate(string source) => ScriptNamespaceMigrator.Migrate(source, Types);

    [Fact]
    public void LoadedTypes_CoverEveryLevel()
    {
        Assert.Contains("Input", Types["Spot.Engine"]);
        Assert.Contains("Texture2D", Types["Spot.Engine.Graphics"]);
        Assert.Contains("Scene", Types["Spot.Engine.Scenes"]);
        Assert.Contains("Application", Types["Spot.Engine"]);
    }

    [Fact]
    public void OldUsings_BecomeOnlyTheNamespacesTheScriptUses()
    {
        const string source = """
            using System;
            using Spot.Core;
            using Spot.Scenes;

            public class Player : Component
            {
                public override void OnUpdate(float dt)
                {
                    if (Input.GetKey(Key.Space)) { }
                }
            }
            """;

        string migrated = Migrate(source);

        Assert.Contains("using System;\nusing Spot.Engine;\n\n", migrated.Replace("\r\n", "\n"));
        Assert.DoesNotContain("using Spot.Core;", migrated);
        
    }

    [Fact]
    public void SplitNamespaces_MapToEveryLevelThatIsUsed()
    {
        const string source = """
            using Spot.Rendering;

            class Sky
            {
                Texture2D? _clouds;
                void Tune() => RenderSettings.VSync = false;
            }
            """;

        string migrated = Migrate(source);

        Assert.Contains("using Spot.Engine.Graphics;", migrated);
        Assert.Contains("using Spot.Engine.Graphics;", migrated);
        Assert.DoesNotContain("using Spot.Engine.Mathematics;", migrated);
    }

    [Fact]
    public void ExtensionCalls_KeepTheirNamespace()
    {
        const string source = """
            using Spot.Assets;
            using Spot.Rendering;

            class Loader
            {
                object Get() => Texture2D.Load("guid:abc");
            }
            """;

        string migrated = Migrate(source);

        // Texture2D.Load is an engine extension on a framework type: the engine namespace must stay.
        Assert.Contains("using Spot.Engine.Assets;", migrated);
        Assert.Contains("using Spot.Engine.Graphics;", migrated);
    }

    [Fact]
    public void QualifiedNamesAndAliases_PointAtTheNewNamespace()
    {
        const string source = """
            using AudioApi = Spot.Audio.Audio;

            class A
            {
                bool Jump() => Spot.Core.Input.GetKey(Spot.Core.Key.Space);
                global::Spot.Scenes.Scene? _scene;
                Spot.Physics.Aabb _box;
            }
            """;

        string migrated = Migrate(source);

        Assert.Contains("using AudioApi = Spot.Engine.Audio.Audio;", migrated);
        Assert.Contains("Spot.Engine.Input.GetKey(Spot.Engine.Key.Space)", migrated);
        Assert.Contains("global::Spot.Engine.Scenes.Scene?", migrated);
        Assert.Contains("Spot.Engine.Mathematics.Aabb _box", migrated);
    }

    [Fact]
    public void NewStyleScripts_AreLeftUntouched()
    {
        const string source = """
            using Spot.Engine.Scenes;
            using Spot.Engine;

            public class Ok : Component { }
            """;

        Assert.Same(source, Migrate(source));
    }

    [Fact]
    public void Migrating_IsIdempotent()
    {
        const string source = """
            using Spot.Core;
            using Spot.Scenes;
            using Spot.UI;

            class Hud : Component { Button? _play; bool _held = Input.GetKey(Key.A); }
            """;

        string once = Migrate(source);

        Assert.Equal(once, Migrate(once));
        Assert.Contains("using Spot.Engine.UI;", once);
    }

    [Fact]
    public void TypesNamedOnlyInComments_DoNotKeepANamespace()
    {
        const string source = """
            using Spot.Physics;

            // Mentions Aabb, but only in a comment.
            class Plain { }
            """;

        string migrated = Migrate(source);

        Assert.DoesNotContain("using Spot.Engine.Mathematics;", migrated);
        Assert.DoesNotContain("using Spot.Physics;", migrated);
    }

    [Fact]
    public void MigrateDirectory_WritesChangedScriptsOnlyAndHonorsDryRun()
    {
        using var temp = new TempDir();
        string old = Path.Combine(temp.Path, "Old.cs");
        string current = Path.Combine(temp.Path, "Sub", "Current.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(current)!);
        File.WriteAllText(old, "using Spot.Scenes;\nclass A : Component { }\n");
        File.WriteAllText(current, "using Spot.Engine;\nclass B : Component { }\n");

        Assert.Equal(1, ScriptNamespaceMigrator.MigrateDirectory(temp.Path, dryRun: true));
        Assert.Contains("using Spot.Scenes;", File.ReadAllText(old));

        Assert.Equal(1, ScriptNamespaceMigrator.MigrateDirectory(temp.Path));
        Assert.Contains("using Spot.Engine;", File.ReadAllText(old));
        Assert.Equal(0, ScriptNamespaceMigrator.MigrateDirectory(temp.Path));
    }

    [Fact]
    public void MigrateDirectory_MissingDirectoryIsNotAnError()
    {
        Assert.Equal(0, ScriptNamespaceMigrator.MigrateDirectory(Path.Combine(Path.GetTempPath(), "spot-no-such-dir-" + Guid.NewGuid())));
    }

    [Fact]
    public void MigrateProject_CoversRootScriptsAndAssetsButNotBuildOutput()
    {
        using var temp = new TempDir();
        string assets = Path.Combine(temp.Path, "Assets");
        string build = Path.Combine(temp.Path, "Build", "play");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(temp.Path, "Program.cs"), "using Spot;\nclass P { void M() => SpotEngine.CreateApplication(); }\n");
        File.WriteAllText(Path.Combine(assets, "S.cs"), "using Spot.Scenes;\nclass S : Component { }\n");
        File.WriteAllText(Path.Combine(build, "Gen.cs"), "using Spot.Scenes;\n");

        Assert.Equal(2, ScriptNamespaceMigrator.MigrateProject(temp.Path, assets));

        Assert.Contains("using Spot.Engine;", File.ReadAllText(Path.Combine(temp.Path, "Program.cs")));
        Assert.Equal("using Spot.Scenes;\n", File.ReadAllText(Path.Combine(build, "Gen.cs")));
    }
}



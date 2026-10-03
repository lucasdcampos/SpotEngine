using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Spot.ScriptGen;

namespace Spot.Build.Tests;

public class ScriptRegistryGeneratorTests
{
    // A minimal in-memory .cs.meta additional file so the generator can map a script to its stable guid.
    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }

    private static string RunGenerator(string source, string sourcePath, params AdditionalText[] additionalTexts)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: sourcePath);

        // Reference every loaded assembly so the compilation resolves Spot.Engine.Scenes.Component and friends.
        // Load the engine and everything it references first (the framework levels its types derive from):
        // nothing else guarantees they are loaded when this test runs, and the test order varies.
        System.Reflection.Assembly engine = typeof(Spot.Engine.Scenes.Component).Assembly;
        foreach (System.Reflection.AssemblyName dependency in engine.GetReferencedAssemblies())
        {
            System.Reflection.Assembly.Load(dependency);
        }

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>();

        var compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new ScriptRegistryGenerator().AsSourceGenerator() },
            additionalTexts: ImmutableArray.Create(additionalTexts));

        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        return result.GeneratedTrees.Length == 0 ? string.Empty : result.GeneratedTrees.Single().ToString();
    }

    [Fact]
    public void Generates_ProviderWithGuidFromMeta()
    {
        const string scriptPath = "C:/proj/Assets/Scripts/Enemy.cs";
        const string source = """
            using Spot.Engine.Scenes;
            namespace Game
            {
                public class Enemy : Component { }
            }
            """;
        var meta = new InMemoryAdditionalText(scriptPath + ".meta", "{ \"guid\": \"enemy-guid-99\", \"importer\": \"script\" }");

        string generated = RunGenerator(source, scriptPath, meta);

        Assert.Contains("IScriptProvider", generated);
        Assert.Contains("global::Game.Enemy", generated);
        Assert.Contains("\"enemy-guid-99\"", generated);
        Assert.Contains("\"Enemy\"", generated);
        Assert.Contains("ModuleInitializer", generated);
    }

    [Fact]
    public void Generates_EmptyGuid_WhenNoMetaSidecar()
    {
        const string scriptPath = "C:/proj/Assets/Scripts/NoMeta.cs";
        const string source = """
            using Spot.Engine.Scenes;
            public class NoMeta : Component { }
            """;

        string generated = RunGenerator(source, scriptPath);

        Assert.Contains("global::NoMeta", generated);
        // No sidecar -> empty guid, still resolvable by name.
        Assert.Contains("new global::Spot.Engine.Scenes.ScriptDescriptor(\"\", \"NoMeta\"", generated);
    }

    [Fact]
    public void Ignores_AbstractAndNonScriptClasses()
    {
        const string scriptPath = "C:/proj/Assets/Scripts/Mixed.cs";
        const string source = """
            using Spot.Engine.Scenes;
            public abstract class BaseThing : Component { }
            public class PlainClass { }
            public class Concrete : Component { }
            """;

        string generated = RunGenerator(source, scriptPath);

        Assert.Contains("global::Concrete", generated);
        Assert.DoesNotContain("BaseThing", generated);
        Assert.DoesNotContain("PlainClass", generated);
    }

    [Fact]
    public void Generates_DescriptorsForUserComponents_IncludingThroughAbstractBases()
    {
        const string scriptPath = "C:/proj/Assets/Scripts/PlayerMovement.cs";
        const string source = """
            using Spot.Engine.Scenes;
            public abstract class Unit : Component { }
            public class PlayerMovement : Component { }
            public class Soldier : Unit { }
            """;
        var meta = new InMemoryAdditionalText(scriptPath + ".meta", "{ \"guid\": \"player-movement-guid\" }");

        string generated = RunGenerator(source, scriptPath, meta);

        Assert.Contains("ScriptDescriptor(\"player-movement-guid\", \"PlayerMovement\"", generated);
        Assert.Contains("global::Soldier", generated);
        Assert.DoesNotContain("global::Unit", generated);
    }
}

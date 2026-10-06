using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.Engine.Tests;

// The editor reloads the game's scripts by turning their components back into scene data, unloading the old
// assembly and rebuilding the components from the new one. These tests pin both halves.
public class ScriptReloadTests
{
    private sealed class CollectibleContext : AssemblyLoadContext
    {
        public CollectibleContext()
            : base("ScriptReloadTests", isCollectible: true)
        {
        }

        // Everything but the compiled script resolves from the default context, so engine types stay shared.
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }

    [Fact]
    public void Unresolve_ThenResolve_RebuildsComponentsWithTheirFieldsAndReferences()
    {
        var scene = new Scene();
        Entity target = scene.Instantiate("Target");
        Entity mover = scene.Instantiate("Mover");
        mover.AddComponent<SerializedTagAlong>();
        SerializedMover original = mover.AddComponent(new SerializedMover
        {
            Speed = 3.0f,
            Direction = Vector3.UnitX,
            Target = target,
            Enabled = false,
        });

        int detached = SceneSerializer.UnresolveUserComponents(scene, type => type == typeof(SerializedMover));

        Assert.Equal(1, detached);
        Assert.False(mover.HasComponent<SerializedMover>());
        Assert.True(mover.HasComponent<SerializedTagAlong>()); // not selected, left alone
        Assert.Equal(nameof(SerializedMover), mover.GetComponent<MissingComponents>().Items.Single().TypeName);

        Assert.Equal(1, SceneSerializer.ResolveMissingComponents(scene));

        var rebuilt = mover.GetComponent<SerializedMover>();
        Assert.NotSame(original, rebuilt);
        Assert.Equal(3.0f, rebuilt.Speed);
        Assert.Equal(Vector3.UnitX, rebuilt.Direction);
        Assert.Equal(target, rebuilt.Target);
        Assert.False(rebuilt.Enabled);
        Assert.False(mover.HasComponent<MissingComponents>());
    }

    [Fact]
    public void UnresolvingCollectibleComponents_LetsTheirLoadContextUnload()
    {
        var scene = new Scene();
        WeakReference context = AttachComponentFromCollectibleAssembly(scene, out Entity entity);

        for (int i = 0; i < 20 && context.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(context.IsAlive, "The scene still holds the unloaded script assembly alive.");

        // The component's data survived as scene data, ready to resolve against the next build.
        MissingComponent missing = entity.GetComponent<MissingComponents>().Items.Single();
        Assert.Equal("HotComponent", missing.TypeName);
        Assert.Equal(8, (int)missing.Data["Fields"]!["Value"]!); // 7, plus the one update it ran
    }

    // Kept out of line so no local in the test method roots the load context or its types.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AttachComponentFromCollectibleAssembly(Scene scene, out Entity entity)
    {
        var context = new CollectibleContext();
        Assembly assembly = context.LoadFromStream(new MemoryStream(Compile("""
            public class HotComponent : Spot.Engine.Component
            {
                public int Value = 7;
                public override void OnUpdate(float deltaTime) => Value++;
            }
            """)));

        entity = scene.Instantiate("Hot");
        var component = (Component)Activator.CreateInstance(assembly.GetType("HotComponent")!)!;
        entity.AddComponent(component);

        // Exercise the paths that cache per-type data: views, serialization and the per-frame update.
        _ = scene.View<Transform>();
        _ = new SceneSerializer(scene).SerializeToString();
        scene.UpdateRuntime(0.0f);

        SceneSerializer.UnresolveUserComponents(scene, type => type.Assembly.IsCollectible);
        ComponentSerialization.ClearTypeCaches();
        context.Unload();
        return new WeakReference(context);
    }

    private static byte[] Compile(string source)
    {
        Assembly engine = typeof(Component).Assembly;
        foreach (AssemblyName dependency in engine.GetReferencedAssemblies())
        {
            Assembly.Load(dependency);
        }

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "HotScripts" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }
}

using Spot.Scenes;

namespace Spot.Engine.Tests;

public class ScriptRegistryTests
{
    private sealed class RegisteredBehaviour : EntityBehaviour
    {
    }

    private sealed class StubProvider : IScriptProvider
    {
        private readonly ScriptDescriptor[] _scripts;

        public StubProvider(params ScriptDescriptor[] scripts) => _scripts = scripts;

        public IEnumerable<ScriptDescriptor> GetScripts() => _scripts;
    }

    private static ScriptDescriptor Descriptor(string guid, string name = nameof(RegisteredBehaviour)) =>
        new(guid, name, typeof(RegisteredBehaviour), () => new RegisteredBehaviour());

    [Fact]
    public void Create_PrefersRegistryByGuid_EvenWhenClassNameChanged()
    {
        var provider = new StubProvider(Descriptor("guid-prefers"));
        ScriptRegistry.Register(provider);
        try
        {
            var scene = new Scene();
            Entity entity = scene.Instantiate();

            // The class name no longer matches (simulating a rename); the guid still resolves.
            EntityBehaviour? instance = ScriptResolver.Create("guid-prefers", "SomeRenamedClass", entity);

            Assert.IsType<RegisteredBehaviour>(instance);
        }
        finally
        {
            ScriptRegistry.Unregister(provider);
        }
    }

    [Fact]
    public void Resolve_UsesRegistryByName_WithoutReflectionScan()
    {
        var provider = new StubProvider(Descriptor("guid-byname"));
        ScriptRegistry.Register(provider);
        try
        {
            Assert.Equal(typeof(RegisteredBehaviour), ScriptResolver.Resolve(null, nameof(RegisteredBehaviour)));
        }
        finally
        {
            ScriptRegistry.Unregister(provider);
        }
    }

    [Fact]
    public void Unregister_RemovesScripts()
    {
        var provider = new StubProvider(Descriptor("guid-removed"));
        ScriptRegistry.Register(provider);
        Assert.True(ScriptRegistry.TryGetByGuid("guid-removed", out _));

        ScriptRegistry.Unregister(provider);

        Assert.False(ScriptRegistry.TryGetByGuid("guid-removed", out _));
    }

    [Fact]
    public void Providers_SnapshotReflectsRegistration_ForHotReloadTracking()
    {
        // The hot-reload host diffs this snapshot across a load to learn which provider an assembly registered.
        var provider = new StubProvider(Descriptor("guid-snapshot"));
        Assert.DoesNotContain(provider, ScriptRegistry.Providers);

        ScriptRegistry.Register(provider);
        try
        {
            Assert.Contains(provider, ScriptRegistry.Providers);
        }
        finally
        {
            ScriptRegistry.Unregister(provider);
        }

        Assert.DoesNotContain(provider, ScriptRegistry.Providers);
    }

    [Fact]
    public void Create_FallsBackToReflection_WhenRegistryMisses()
    {
        // No provider registered for this type; the reflection scan of loaded assemblies still finds it.
        var scene = new Scene();
        Entity entity = scene.Instantiate();

        EntityBehaviour? instance = ScriptResolver.Create(nameof(RegisteredBehaviour), entity);

        Assert.IsType<RegisteredBehaviour>(instance);
    }
}

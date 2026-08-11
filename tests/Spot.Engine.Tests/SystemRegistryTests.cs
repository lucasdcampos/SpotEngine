using Spot.Scenes;

namespace Spot.Engine.Tests;

// Covers the ISystem registry: custom systems run in Order, can be removed, slot correctly against the
// built-ins, and a throwing system is contained so the tick keeps going (the engine's "never crash" rule).
public class SystemRegistryTests
{
    private const float Dt = 1f / 60f;

    private sealed class OrderProbe : EntityBehaviour
    {
        public int ObservedFlag = -1;
    }

    [Fact]
    public void CustomSystems_RunInAscendingOrder()
    {
        var scene = new Scene();
        var calls = new List<string>();
        scene.RegisterSystem(new DelegateSystem(50, (_, _) => calls.Add("early")));
        scene.RegisterSystem(new DelegateSystem(20, (_, _) => calls.Add("earlier")));

        scene.UpdateRuntime(Dt);

        Assert.Equal(new[] { "earlier", "early" }, calls);
    }

    [Fact]
    public void CustomSystem_BeforeScripts_IsObservedByScriptsSameFrame()
    {
        var scene = new Scene();
        var probe = scene.Instantiate().AddScript(new OrderProbe());

        // A system ordered just before the built-in script system flips a value the script then reads.
        int flag = 0;
        scene.RegisterSystem(new DelegateSystem(SystemOrder.Scripts - 1, (_, _) => flag = 42));
        // The script reads the flag during its own update (the built-in script system runs at Scripts).
        scene.RegisterSystem(new DelegateSystem(SystemOrder.Scripts + 1, (_, _) => probe.ObservedFlag = flag));

        scene.UpdateRuntime(Dt);

        Assert.Equal(42, flag);
        Assert.Equal(42, probe.ObservedFlag);
    }

    [Fact]
    public void RemovedSystem_DoesNotRun()
    {
        var scene = new Scene();
        int runs = 0;
        var system = new DelegateSystem(10, (_, _) => runs++);
        scene.RegisterSystem(system);

        scene.UpdateRuntime(Dt);
        Assert.Equal(1, runs);

        Assert.True(scene.Systems.Remove(system));
        scene.UpdateRuntime(Dt);
        Assert.Equal(1, runs); // no further runs after removal
    }

    [Fact]
    public void ThrowingSystem_IsContainedAndLaterSystemsStillRun()
    {
        var scene = new Scene();
        int laterRuns = 0;
        scene.RegisterSystem(new DelegateSystem(10, (_, _) => throw new InvalidOperationException("boom")));
        scene.RegisterSystem(new DelegateSystem(20, (_, _) => laterRuns++));

        var exception = Record.Exception(() =>
        {
            scene.UpdateRuntime(Dt);
            scene.UpdateRuntime(Dt);
        });

        Assert.Null(exception);            // a faulting system never takes the tick down
        Assert.Equal(2, laterRuns);        // and never stops the systems ordered after it
    }

    [Fact]
    public void Scene_StartsWithTheBuiltInSystemsRegistered()
    {
        var scene = new Scene();

        // Character controller, 2D physics, 3D physics, animation, particles, audio, scripts.
        Assert.Equal(7, scene.Systems.Ordered.Count);
        Assert.Equal(SystemOrder.CharacterController, scene.Systems.Ordered[0].Order);
        Assert.Equal(SystemOrder.Scripts, scene.Systems.Ordered[^1].Order);
    }
}

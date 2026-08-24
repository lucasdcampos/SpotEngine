using System.Collections.Generic;
using Spot.Scenes;

namespace Spot.Engine.Tests;

// A probe that records the order of every lifecycle hook it receives, so tests can assert the exact
// sequence the script system drives.
public sealed class LifecycleProbe : EntityBehaviour
{
    public List<string> Events { get; } = new();

    public override void OnCreate() => Events.Add("create");

    public override void OnEnable() => Events.Add("enable");

    public override void OnUpdate(float deltaTime) => Events.Add("update");

    public override void OnLateUpdate(float deltaTime) => Events.Add("late");

    public override void OnFixedUpdate(float deltaTime) => Events.Add("fixed");

    public override void OnDisable() => Events.Add("disable");

    public override void OnValidate() => Events.Add("validate");

    public override void OnDestroy() => Events.Add("destroy");
}

public class ScriptLifecycleTests
{
    [Fact]
    public void FirstFrame_RunsCreateEnableUpdateLate_InOrder()
    {
        var scene = new Scene();
        LifecycleProbe probe = scene.Instantiate("A").AddScript(new LifecycleProbe());

        scene.UpdateRuntime(0.016f);

        // OnFixedUpdate does not fire on the first frame: it runs before the script system, which is where the
        // script is created and enabled, so there is nothing started yet for it to tick.
        Assert.Equal(new[] { "create", "enable", "update", "late" }, probe.Events);
    }

    [Fact]
    public void SecondFrame_RunsFixedThenUpdateThenLate_WithoutRecreating()
    {
        var scene = new Scene();
        LifecycleProbe probe = scene.Instantiate("A").AddScript(new LifecycleProbe());

        scene.UpdateRuntime(0.016f);
        probe.Events.Clear();
        scene.UpdateRuntime(0.016f);

        // Fixed runs before the frame's Update (it is a system ordered ahead of the script system), and there
        // is no second create/enable.
        Assert.Equal(new[] { "fixed", "update", "late" }, probe.Events);
    }

    [Fact]
    public void Disabling_FiresOnDisable_AndStopsUpdates()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        LifecycleProbe probe = entity.AddScript(new LifecycleProbe());

        scene.UpdateRuntime(0.016f);
        entity.Enabled = false;
        probe.Events.Clear();

        scene.UpdateRuntime(0.016f);
        Assert.Equal(new[] { "disable" }, probe.Events);

        // Fully quiet while disabled — no update, late or fixed.
        probe.Events.Clear();
        scene.UpdateRuntime(0.016f);
        Assert.Empty(probe.Events);
    }

    [Fact]
    public void ReEnabling_FiresOnEnableAgain_ButNotOnCreate()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        LifecycleProbe probe = entity.AddScript(new LifecycleProbe());

        scene.UpdateRuntime(0.016f);
        entity.Enabled = false;
        scene.UpdateRuntime(0.016f);
        probe.Events.Clear();

        entity.Enabled = true;
        scene.UpdateRuntime(0.016f);

        // OnEnable fires again; OnCreate does not; the enable precedes this frame's update.
        Assert.Equal(new[] { "enable", "update", "late" }, probe.Events);
    }

    [Fact]
    public void DestroyingEntity_FiresOnDisableThenOnDestroy()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        LifecycleProbe probe = entity.AddScript(new LifecycleProbe());

        scene.UpdateRuntime(0.016f);
        probe.Events.Clear();

        // Destroy is deferred to the end of the frame, so the script still updates once this frame and is then
        // torn down (OnDisable before OnDestroy) when the scene flushes pending destroys.
        scene.Destroy(entity);
        scene.UpdateRuntime(0.016f);

        Assert.Equal(new[] { "fixed", "update", "late", "disable", "destroy" }, probe.Events);
    }

    [Fact]
    public void InvokeValidate_CallsOnValidate_Guarded()
    {
        var scene = new Scene();
        LifecycleProbe probe = scene.Instantiate("A").AddScript(new LifecycleProbe());

        ScriptSystem.InvokeValidate(probe);

        Assert.Equal(new[] { "validate" }, probe.Events);
    }

    [Fact]
    public void ThrowingHook_IsQuarantined_NotRethrown()
    {
        var scene = new Scene();
        scene.Instantiate("Boom").AddScript(new ThrowingProbe());

        // A script that throws from OnUpdate must be quarantined, never crashing the frame.
        var ex = Record.Exception(() =>
        {
            scene.UpdateRuntime(0.016f);
            scene.UpdateRuntime(0.016f);
        });

        Assert.Null(ex);
    }

    private sealed class ThrowingProbe : EntityBehaviour
    {
        public override void OnUpdate(float deltaTime) => throw new System.InvalidOperationException("boom");
    }
}

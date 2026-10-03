using Spot.Engine.Scenes;

namespace Spot.Engine.Tests;

public class ScriptSystemTests
{
    private sealed class CountingBehaviour : Component
    {
        public int Creates;
        public int Updates;
        public int Destroys;

        public override void OnStart() => Creates++;
        public override void OnUpdate(float deltaTime) => Updates++;
        public override void OnDestroy() => Destroys++;
    }

    private sealed class ThrowingOnUpdateBehaviour : Component
    {
        public int Updates;

        public override void OnUpdate(float deltaTime)
        {
            Updates++;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class ThrowingOnDestroyBehaviour : Component
    {
        public override void OnDestroy() => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void Update_RunsCreateOnceThenUpdateEachFrame()
    {
        var scene = new Scene();
        var script = scene.Instantiate().AddComponent(new CountingBehaviour());

        ComponentSystem.Update(scene, 0.1f);
        ComponentSystem.Update(scene, 0.1f);

        Assert.Equal(1, script.Creates);
        Assert.Equal(2, script.Updates);
        Assert.False(script.Faulted);
    }

    [Fact]
    public void Update_QuarantinesThrowingScriptAndKeepsOthersRunning()
    {
        var scene = new Scene();
        var bad = scene.Instantiate().AddComponent(new ThrowingOnUpdateBehaviour());
        var good = scene.Instantiate().AddComponent(new CountingBehaviour());

        ComponentSystem.Update(scene, 0.1f);
        Assert.True(bad.Faulted);
        Assert.Equal(1, bad.Updates);
        Assert.Equal(1, good.Updates); // a fault in one script does not stop the others

        ComponentSystem.Update(scene, 0.1f);
        Assert.Equal(1, bad.Updates);  // quarantined: skipped on later frames
        Assert.Equal(2, good.Updates); // unaffected
    }

    [Fact]
    public void DestroyAll_CallsOnDestroyOnlyForStartedScripts()
    {
        var scene = new Scene();
        var started = scene.Instantiate().AddComponent(new CountingBehaviour());
        ComponentSystem.Update(scene, 0.1f); // runs OnStart on 'started'

        // Attached after the update ran, so its OnStart never ran.
        var neverStarted = scene.Instantiate().AddComponent(new CountingBehaviour());

        ComponentSystem.DestroyAll(scene);

        Assert.Equal(1, started.Destroys);
        Assert.Equal(0, neverStarted.Destroys);
    }

    [Fact]
    public void DestroyAll_SwallowsThrowingOnDestroy()
    {
        var scene = new Scene();
        scene.Instantiate().AddComponent(new ThrowingOnDestroyBehaviour());
        var good = scene.Instantiate().AddComponent(new CountingBehaviour());
        ComponentSystem.Update(scene, 0.1f); // start both

        var exception = Record.Exception(() => ComponentSystem.DestroyAll(scene));

        Assert.Null(exception);
        Assert.Equal(1, good.Destroys); // a throwing teardown does not abort the rest
    }
}

using System.Numerics;
using Spot.Physics;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Engine.Tests;

// Characterizes Scene.UpdateRuntime — the play-mode tick that drives every scene system. Nothing else in
// the suite exercises it, yet it is what the ISystem-registry refactor rewrites (the hardcoded system calls
// become an ordered registry). These tests lock the observable contract so that refactor cannot silently
// change which systems run, or the order they run in.
public class UpdateRuntimeTests
{
    private const float Dt = 1f / 60f;

    private sealed class UpdateCounter : EntityBehaviour
    {
        public int Updates;
        public override void OnUpdate(float deltaTime) => Updates++;
    }

    // Destroys its own entity the first time it updates, so we can observe UpdateRuntime's end-of-frame flush.
    private sealed class SelfDestructOnUpdate : EntityBehaviour
    {
        public override void OnUpdate(float deltaTime) => Entity.Scene.Destroy(Entity);
    }

    // Records the sibling body's velocity the first time it updates. If physics stepped before scripts (the
    // intended order), gravity has already made Velocity.Y negative by the time this runs; if scripts ran
    // first it would still be exactly zero. A clean, magnitude-independent discriminator of the ordering.
    private sealed class VelocityProbe : EntityBehaviour
    {
        public bool Observed;
        public float ObservedVelocityY;

        public override void OnUpdate(float deltaTime)
        {
            if (Observed) return;
            Observed = true;
            ObservedVelocityY = Entity.GetComponent<PhysicsBody3DComponent>().Velocity.Y;
        }
    }

    [Fact]
    public void UpdateRuntime_RunsScriptsEveryFrame()
    {
        var scene = new Scene();
        var counter = scene.Instantiate().AddScript(new UpdateCounter());

        scene.UpdateRuntime(Dt);
        scene.UpdateRuntime(Dt);

        Assert.Equal(2, counter.Updates);
    }

    [Fact]
    public void UpdateRuntime_FlushesDestroyedEntitiesAtEndOfFrame()
    {
        var scene = new Scene();
        Entity doomed = scene.Instantiate("Doomed");
        doomed.AddScript(new SelfDestructOnUpdate());

        Assert.True(doomed.IsValid);
        scene.UpdateRuntime(Dt);

        // The script queued its own destruction during the tick; UpdateRuntime flushes it before returning.
        Assert.False(doomed.IsValid);
        Assert.Null(scene.Find("Doomed"));
    }

    [Fact]
    public void UpdateRuntime_StepsPhysics()
    {
        var scene = new Scene();
        Entity e = scene.Instantiate();
        e.AddComponent(new BoxCollider3DComponent { Size = Vector3.One });
        var body = e.AddComponent(new PhysicsBody3DComponent { IsDynamic = true });
        e.GetComponent<TransformComponent>().Position = new Vector3(0, 10, 0);

        for (int i = 0; i < 30; i++)
        {
            scene.UpdateRuntime(Dt);
        }

        Assert.True(e.GetComponent<TransformComponent>().Position.Y < 10f, "UpdateRuntime should step physics so a dynamic body falls");
        Assert.True(body.Velocity.Y < 0f);
    }

    [Fact]
    public void UpdateRuntime_StepsPhysicsBeforeRunningScripts()
    {
        var scene = new Scene();
        Entity e = scene.Instantiate();
        e.AddComponent(new BoxCollider3DComponent { Size = Vector3.One });
        e.AddComponent(new PhysicsBody3DComponent { IsDynamic = true });
        e.GetComponent<TransformComponent>().Position = new Vector3(0, 10, 0);
        var probe = e.AddScript(new VelocityProbe());

        scene.UpdateRuntime(Dt);

        Assert.True(probe.Observed);
        Assert.True(probe.ObservedVelocityY < 0f, "the script should observe gravity already applied, i.e. physics ran before scripts");
    }

    [Fact]
    public void UpdateRuntime_DoesNotThrowOnAMixedScene()
    {
        var scene = new Scene();

        Entity body = scene.Instantiate("Body");
        body.AddComponent(new BoxCollider3DComponent { Size = Vector3.One });
        body.AddComponent(new PhysicsBody3DComponent { IsDynamic = true });
        body.GetComponent<TransformComponent>().Position = new Vector3(0, 5, 0);

        scene.Instantiate("Emitter").AddComponent(new ParticleSystemComponent());
        scene.Instantiate("Speaker").AddComponent(new AudioSourceComponent());
        scene.Instantiate("Scripted").AddScript(new UpdateCounter());

        var exception = Record.Exception(() =>
        {
            for (int i = 0; i < 5; i++)
            {
                scene.UpdateRuntime(Dt);
            }
        });

        Assert.Null(exception);
    }
}

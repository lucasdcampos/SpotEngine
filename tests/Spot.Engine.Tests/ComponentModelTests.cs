using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine.Scenes;

namespace Spot.Engine.Tests;

// A user component that records the order of every lifecycle hook it receives.
public sealed class ComponentProbe : Component
{
    public List<string> Events { get; } = new();

    public override void OnStart() => Events.Add("start");

    public override void OnEnable() => Events.Add("enable");

    public override void OnUpdate(float deltaTime) => Events.Add("update");

    public override void OnLateUpdate(float deltaTime) => Events.Add("late");

    public override void OnFixedUpdate(float deltaTime) => Events.Add("fixed");

    public override void OnDisable() => Events.Add("disable");

    public override void OnDestroy() => Events.Add("destroy");
}

public interface IDamageable
{
    int Health { get; set; }
}

public abstract class Creature : Component, IDamageable
{
    public int Health { get; set; } = 10;
}

public sealed class Goblin : Creature
{
}

public class ComponentModelTests
{
    private sealed class Thrower : Component
    {
        public override void OnUpdate(float deltaTime) => throw new InvalidOperationException("boom");
    }

    private sealed class Counter : Component
    {
        public int Updates;

        public override void OnUpdate(float deltaTime) => Updates++;
    }

    // Records the global order components update in, across entities.
    private sealed class OrderRecorder : Component
    {
        public List<string>? Log;
        public string Name = "";

        public override void OnUpdate(float deltaTime) => Log!.Add(Name);
    }

    // Removes a component from another entity during its own update.
    private sealed class Remover : Component
    {
        public Entity Target;

        public override void OnUpdate(float deltaTime) => Target.RemoveComponent<ComponentProbe>();
    }

    private sealed class CollisionRecorder : Component
    {
        public int Enter;
        public int TriggerEnter;

        public override void OnCollisionEnter(Collision collision) => Enter++;

        public override void OnTriggerEnter(Entity other) => TriggerEnter++;
    }

    private sealed class Ticker : Component
    {
        public int Steps;

        public override void OnStart() => StartCoroutine(Routine());

        private IEnumerator Routine()
        {
            while (true)
            {
                Steps++;
                yield return null;
            }
        }
    }

    [Fact]
    public void FirstFrame_RunsStartEnableUpdateLate_InOrder()
    {
        var scene = new Scene();
        ComponentProbe probe = scene.Instantiate("A").AddComponent<ComponentProbe>();

        scene.UpdateRuntime(0.016f);

        Assert.Equal(new[] { "start", "enable", "update", "late" }, probe.Events);
    }

    [Fact]
    public void SecondFrame_RunsFixedThenUpdateThenLate_WithoutRestarting()
    {
        var scene = new Scene();
        ComponentProbe probe = scene.Instantiate("A").AddComponent<ComponentProbe>();

        scene.UpdateRuntime(0.016f);
        probe.Events.Clear();
        scene.UpdateRuntime(0.016f);

        Assert.Equal(new[] { "fixed", "update", "late" }, probe.Events);
    }

    [Fact]
    public void DisablingTheComponent_FiresOnDisable_AndReEnablingFiresOnEnable()
    {
        var scene = new Scene();
        ComponentProbe probe = scene.Instantiate("A").AddComponent<ComponentProbe>();
        scene.UpdateRuntime(0.016f);

        probe.Enabled = false;
        probe.Events.Clear();
        scene.UpdateRuntime(0.016f);
        scene.UpdateRuntime(0.016f);
        Assert.Equal(new[] { "disable" }, probe.Events);

        probe.Enabled = true;
        probe.Events.Clear();
        scene.UpdateRuntime(0.016f);
        Assert.Equal(new[] { "enable", "update", "late" }, probe.Events);
    }

    [Fact]
    public void DisablingTheEntity_FiresOnDisable()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        ComponentProbe probe = entity.AddComponent<ComponentProbe>();
        scene.UpdateRuntime(0.016f);

        entity.Enabled = false;
        probe.Events.Clear();
        scene.UpdateRuntime(0.016f);

        Assert.Equal(new[] { "disable" }, probe.Events);
    }

    [Fact]
    public void ThrowingComponent_IsQuarantined_AndOthersKeepRunning()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        Thrower thrower = entity.AddComponent<Thrower>();
        Counter counter = entity.AddComponent<Counter>();

        scene.UpdateRuntime(0.016f);
        scene.UpdateRuntime(0.016f);

        Assert.True(thrower.Faulted);
        Assert.Equal(2, counter.Updates);
    }

    [Fact]
    public void RemovingAStartedComponent_FiresDisableThenDestroy_AndStopsUpdates()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        ComponentProbe probe = entity.AddComponent<ComponentProbe>();
        scene.UpdateRuntime(0.016f);

        probe.Events.Clear();
        entity.RemoveComponent<ComponentProbe>();
        scene.UpdateRuntime(0.016f);

        Assert.Equal(new[] { "disable", "destroy" }, probe.Events);
        Assert.False(entity.HasComponent<ComponentProbe>());
    }

    [Fact]
    public void ComponentRemovedEarlierInTheFrame_IsNotUpdated()
    {
        var scene = new Scene();
        Entity target = scene.Instantiate("Target");
        Entity remover = scene.Instantiate("Remover");
        remover.AddComponent(new Remover { Target = target });
        ComponentProbe probe = target.AddComponent<ComponentProbe>();

        scene.UpdateRuntime(0.016f);

        Assert.Empty(probe.Events);
    }

    [Fact]
    public void DestroyingTheEntity_FiresDisableThenDestroy()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        ComponentProbe probe = entity.AddComponent<ComponentProbe>();
        scene.UpdateRuntime(0.016f);

        probe.Events.Clear();
        scene.Destroy(entity);
        scene.FlushDestroyed();

        Assert.Equal(new[] { "disable", "destroy" }, probe.Events);
        Assert.Empty(scene.UserComponents);
    }

    [Fact]
    public void AddingTheSameTypeAgain_ReplacesAndTearsDownTheOldInstance()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        ComponentProbe first = entity.AddComponent<ComponentProbe>();
        scene.UpdateRuntime(0.016f);

        first.Events.Clear();
        ComponentProbe second = entity.AddComponent<ComponentProbe>();

        Assert.Same(second, entity.GetComponent<ComponentProbe>());
        Assert.Equal(new[] { "disable", "destroy" }, first.Events);
        Assert.Single(scene.UserComponents);
    }

    [Fact]
    public void DestroyAll_TearsDownOnlyStartedComponents()
    {
        var scene = new Scene();
        ComponentProbe started = scene.Instantiate("A").AddComponent<ComponentProbe>();
        scene.UpdateRuntime(0.016f);
        ComponentProbe fresh = scene.Instantiate("B").AddComponent<ComponentProbe>();

        started.Events.Clear();
        ComponentSystem.DestroyAll(scene);

        Assert.Equal(new[] { "disable", "destroy" }, started.Events);
        Assert.Empty(fresh.Events);
    }

    [Fact]
    public void Components_UpdateInTheOrderTheyWereAdded()
    {
        var scene = new Scene();
        var log = new List<string>();
        Entity a = scene.Instantiate("A");
        Entity b = scene.Instantiate("B");
        b.AddComponent(new OrderRecorder { Log = log, Name = "b" });
        a.AddComponent(new OrderRecorder { Log = log, Name = "a" });

        scene.UpdateRuntime(0.016f);

        Assert.Equal(new[] { "b", "a" }, log);
    }

    [Fact]
    public void BuiltInComponents_AreNotUserComponents_ButKnowTheirEntity()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        Counter counter = entity.AddComponent<Counter>();

        Assert.Equal(new Component[] { counter }, scene.UserComponents);
        Assert.Equal(entity, entity.GetComponent<TransformComponent>().Entity);
        Assert.Equal(entity, counter.Entity);
    }

    [Fact]
    public void UnattachedComponent_HasAnInvalidEntity()
    {
        var counter = new Counter();

        Assert.False(counter.Entity.IsValid);
    }

    [Fact]
    public void GetComponent_ResolvesInterfacesAndBaseClasses()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        Goblin goblin = entity.AddComponent<Goblin>();

        Assert.Same(goblin, entity.GetComponent<IDamageable>());
        Assert.Same(goblin, entity.GetComponent<Creature>());
        Assert.True(entity.HasComponent<IDamageable>());
        Assert.True(entity.TryGetComponent(out Creature? creature));
        Assert.Same(goblin, creature);
        Assert.False(entity.HasComponent<ComponentProbe>());
    }

    [Fact]
    public void GetComponentInParent_FindsAnInterfaceOnAnAncestor()
    {
        var scene = new Scene();
        Entity parent = scene.Instantiate("Parent");
        Entity child = scene.Instantiate("Child");
        child.SetParent(parent);
        Goblin goblin = parent.AddComponent<Goblin>();

        Assert.Same(goblin, child.GetComponentInParent<IDamageable>());
    }

    [Fact]
    public void RemoveComponent_ByInterface_RemovesTheMatch()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        entity.AddComponent<Goblin>();

        entity.RemoveComponent<IDamageable>();

        Assert.False(entity.HasComponent<Goblin>());
    }

    [Fact]
    public void EntityGetComponents_ReturnsMatchesInAddOrder()
    {
        var scene = new Scene();
        Entity entity = scene.Instantiate("A");
        Counter counter = entity.AddComponent<Counter>();
        ComponentProbe probe = entity.AddComponent<ComponentProbe>();

        Assert.Equal(new Component[] { counter, probe }, entity.GetComponents<Component>()[^2..]);
        Assert.Equal(new[] { counter }, entity.GetComponents<Counter>());
        Assert.Same(probe, entity.Components[^1]);
    }

    [Fact]
    public void SceneGetComponents_FindsEveryMatchIncludingByInterface()
    {
        var scene = new Scene();
        Goblin a = scene.Instantiate("A").AddComponent<Goblin>();
        Goblin b = scene.Instantiate("B").AddComponent<Goblin>();
        scene.Instantiate("C").AddComponent<Counter>();

        List<IDamageable> damageables = scene.GetComponents<IDamageable>();

        Assert.Equal(2, damageables.Count);
        Assert.Contains(a, damageables);
        Assert.Contains(b, damageables);
        Assert.Equal(3, scene.GetComponents<TransformComponent>().Count);
    }

    [Fact]
    public void Collisions_AreDeliveredToStartedComponents()
    {
        var scene = new Scene();
        Entity a = scene.Instantiate("A");
        Entity b = scene.Instantiate("B");
        CollisionRecorder ra = a.AddComponent<CollisionRecorder>();
        CollisionRecorder rb = b.AddComponent<CollisionRecorder>();
        ComponentSystem.Update(scene, 0f);

        var dispatcher = new CollisionDispatcher();
        dispatcher.Dispatch(new[] { new ContactPair(a, b, isTrigger: false, Vector3.UnitY, Vector3.Zero) });
        dispatcher.Reset();
        dispatcher.Dispatch(new[] { new ContactPair(a, b, isTrigger: true, Vector3.UnitY, Vector3.Zero) });

        Assert.Equal(1, ra.Enter);
        Assert.Equal(1, rb.Enter);
        Assert.Equal(1, ra.TriggerEnter);
        Assert.Equal(1, rb.TriggerEnter);
    }

    [Fact]
    public void Coroutines_TickOnUserComponents()
    {
        var scene = new Scene();
        Ticker ticker = scene.Instantiate("A").AddComponent<Ticker>();

        scene.UpdateRuntime(0.016f);
        scene.UpdateRuntime(0.016f);
        scene.UpdateRuntime(0.016f);

        Assert.True(ticker.Steps >= 2);
    }

    [Fact]
    public void PersistentEntity_CarriesItsComponentsToTheNextScene_AndRebindsThem()
    {
        var first = new Scene();
        Entity entity = first.Instantiate("Keeper");
        Counter counter = entity.AddComponent<Counter>();
        entity.DontDestroyOnLoad();

        var second = new Scene();
        first.MigratePersistentEntitiesTo(second);

        Assert.Empty(first.UserComponents);
        Assert.Equal(new Component[] { counter }, second.UserComponents);
        Assert.Same(second, counter.Entity.Scene);
        Assert.True(counter.Entity.IsValid);
        Assert.Same(counter, counter.Entity.GetComponent<Counter>());
    }
}

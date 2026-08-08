using System;
using System.Numerics;
using Spot.Physics;
using Spot.Physics.Bepu;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Engine.Tests;

public class CollisionEventsTests
{
    private const float Dt = 1f / 60f;

    private sealed class CollisionRecorder : EntityBehaviour
    {
        public int Enter;
        public int Stay;
        public int Exit;
        public int TriggerEnter;
        public int TriggerStay;
        public int TriggerExit;
        public Entity? LastOther;

        public override void OnCollisionEnter(Collision collision)
        {
            Enter++;
            LastOther = collision.Other;
        }

        public override void OnCollisionStay(Collision collision) => Stay++;

        public override void OnCollisionExit(Collision collision) => Exit++;

        public override void OnTriggerEnter(Entity other)
        {
            TriggerEnter++;
            LastOther = other;
        }

        public override void OnTriggerStay(Entity other) => TriggerStay++;

        public override void OnTriggerExit(Entity other) => TriggerExit++;
    }

    // --- Backend contact reporting (Bepu) -------------------------------------------------------

    [Fact]
    public void Backend_ReportsCollisionContact_BetweenBoxAndFloor()
    {
        var scene = new Scene();
        var floor = scene.Instantiate("floor");
        floor.AddComponent(new BoxCollider3DComponent { Size = new Vector3(20, 1, 20) });
        floor.GetComponent<TransformComponent>().Position = Vector3.Zero;

        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider3DComponent { Size = Vector3.One });
        box.AddComponent(new PhysicsBody3DComponent { IsDynamic = true });
        box.GetComponent<TransformComponent>().Position = new Vector3(0, 3, 0);

        using var physics = new BepuPhysics3D();

        bool sawContact = false;
        for (int i = 0; i < 300 && !sawContact; i++)
        {
            physics.Step(scene, Dt);
            foreach (ContactPair pair in physics.Contacts)
            {
                if (!pair.IsTrigger &&
                    (pair.A.Id == floor.Id || pair.B.Id == floor.Id) &&
                    (pair.A.Id == box.Id || pair.B.Id == box.Id))
                {
                    sawContact = true;
                }
            }
        }

        Assert.True(sawContact, "the falling box should produce a non-trigger contact with the floor");
        Assert.True(box.GetComponent<TransformComponent>().Position.Y > 0.5f, "a solid box should rest on the floor, not pass through");
    }

    [Fact]
    public void Backend_ReportsTriggerOverlap_AndBodyPassesThrough()
    {
        var scene = new Scene();
        var zone = scene.Instantiate("zone");
        zone.AddComponent(new BoxCollider3DComponent { Size = new Vector3(2, 2, 2), IsTrigger = true });
        zone.GetComponent<TransformComponent>().Position = Vector3.Zero;

        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider3DComponent { Size = Vector3.One });
        box.AddComponent(new PhysicsBody3DComponent { IsDynamic = true });
        box.GetComponent<TransformComponent>().Position = new Vector3(0, 5, 0);

        using var physics = new BepuPhysics3D();

        bool sawTrigger = false;
        for (int i = 0; i < 200; i++)
        {
            physics.Step(scene, Dt);
            foreach (ContactPair pair in physics.Contacts)
            {
                if (pair.IsTrigger &&
                    (pair.A.Id == zone.Id || pair.B.Id == zone.Id) &&
                    (pair.A.Id == box.Id || pair.B.Id == box.Id))
                {
                    sawTrigger = true;
                }
            }
        }

        Assert.True(sawTrigger, "the box should register a trigger overlap while passing through the zone");
        Assert.True(box.GetComponent<TransformComponent>().Position.Y < -1f, "a trigger must not block the body; it should fall through");
    }

    // --- Dispatcher enter/stay/exit diffing -----------------------------------------------------

    private static (Scene scene, Entity a, Entity b, CollisionRecorder ra, CollisionRecorder rb) TwoScriptedEntities()
    {
        var scene = new Scene();
        var a = scene.Instantiate("A");
        var b = scene.Instantiate("B");
        var ra = a.AddScript<CollisionRecorder>();
        var rb = b.AddScript<CollisionRecorder>();
        ScriptSystem.Update(scene, 0f); // start the scripts so callbacks are delivered
        return (scene, a, b, ra, rb);
    }

    [Fact]
    public void Dispatcher_RaisesCollisionEnterStayExit_OnBothEntities()
    {
        (Scene _, Entity a, Entity b, CollisionRecorder ra, CollisionRecorder rb) = TwoScriptedEntities();
        var dispatcher = new CollisionDispatcher();
        var pair = new[] { new ContactPair(a, b, isTrigger: false, Vector3.UnitY, Vector3.Zero) };

        dispatcher.Dispatch(pair);   // first appearance -> enter
        dispatcher.Dispatch(pair);   // still present -> stay
        dispatcher.Dispatch(Array.Empty<ContactPair>()); // gone -> exit

        Assert.Equal(1, ra.Enter);
        Assert.Equal(1, ra.Stay);
        Assert.Equal(1, ra.Exit);
        Assert.Equal(1, rb.Enter);
        Assert.Equal(1, rb.Stay);
        Assert.Equal(1, rb.Exit);
        Assert.Equal(b.Id, ra.LastOther!.Value.Id);
        Assert.Equal(a.Id, rb.LastOther!.Value.Id);
    }

    [Fact]
    public void Dispatcher_RaisesTriggerCallbacks_ForTriggerPairs()
    {
        (Scene _, Entity a, Entity b, CollisionRecorder ra, CollisionRecorder rb) = TwoScriptedEntities();
        var dispatcher = new CollisionDispatcher();
        var pair = new[] { new ContactPair(a, b, isTrigger: true, Vector3.UnitY, Vector3.Zero) };

        dispatcher.Dispatch(pair);
        dispatcher.Dispatch(Array.Empty<ContactPair>());

        Assert.Equal(1, ra.TriggerEnter);
        Assert.Equal(1, ra.TriggerExit);
        Assert.Equal(1, rb.TriggerEnter);
        Assert.Equal(1, rb.TriggerExit);
        Assert.Equal(0, ra.Enter); // trigger pairs must not fire collision callbacks
    }

    [Fact]
    public void Dispatcher_PairOrderIsStable_NoDuplicateEnter()
    {
        (Scene _, Entity a, Entity b, CollisionRecorder ra, CollisionRecorder _) = TwoScriptedEntities();
        var dispatcher = new CollisionDispatcher();

        // The backend may report the same pair with A/B swapped between steps; that must still be "stay".
        dispatcher.Dispatch(new[] { new ContactPair(a, b, false, Vector3.UnitY, Vector3.Zero) });
        dispatcher.Dispatch(new[] { new ContactPair(b, a, false, Vector3.UnitY, Vector3.Zero) });

        Assert.Equal(1, ra.Enter);
        Assert.Equal(1, ra.Stay);
        Assert.Equal(0, ra.Exit);
    }
}

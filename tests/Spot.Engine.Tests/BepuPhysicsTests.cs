using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine.Physics.Bepu;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.Engine.Tests;

public class BepuPhysicsTests
{
    private const float Dt = 1f / 60f;

    private static void Run(BepuPhysics3D physics, Scene scene, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            physics.Step(scene, Dt);
        }
    }

    [Fact]
    public void DynamicBody_FallsUnderGravity()
    {
        var scene = new Scene();
        var e = scene.Instantiate();
        e.AddComponent(new BoxCollider3D { Size = Vector3.One });
        var body = e.AddComponent(new PhysicsBody3D { IsDynamic = true });
        var t = e.GetComponent<Transform>();
        t.Position = new Vector3(0, 10, 0);

        using var physics = new BepuPhysics3D();
        Run(physics, scene, 30);

        Assert.True(t.Position.Y < 10f, "a dynamic body should fall");
        Assert.True(body.Velocity.Y < 0f, "a falling body should have downward velocity");
    }

    [Fact]
    public void DynamicBox_RestsOnStaticFloor()
    {
        var scene = new Scene();

        var floor = scene.Instantiate("floor");
        floor.AddComponent(new BoxCollider3D { Size = new Vector3(20, 1, 20) }); // top at y = 0.5
        floor.GetComponent<Transform>().Position = Vector3.Zero;

        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider3D { Size = Vector3.One });
        var body = box.AddComponent(new PhysicsBody3D { IsDynamic = true });
        var t = box.GetComponent<Transform>();
        t.Position = new Vector3(0, 5, 0);

        using var physics = new BepuPhysics3D();
        Run(physics, scene, 240); // ~4 seconds

        // Floor top (0.5) + box half-height (0.5) = 1.0.
        Assert.InRange(t.Position.Y, 0.8f, 1.3f);
        Assert.True(MathF.Abs(body.Velocity.Y) < 0.5f, "a rested body should have near-zero vertical velocity");
    }

    [Fact]
    public void Raycast_HitsStaticFloorAndReportsEntity()
    {
        var scene = new Scene();
        var floor = scene.Instantiate("floor");
        floor.AddComponent(new BoxCollider3D { Size = new Vector3(20, 1, 20) }); // top at y = 0.5
        floor.GetComponent<Transform>().Position = Vector3.Zero;

        using var physics = new BepuPhysics3D();
        physics.Step(scene, Dt); // populate the simulation

        bool hit = physics.Raycast(scene, new Vector3(0, 5, 0), new Vector3(0, -1, 0), 10f, out RaycastHit info);

        Assert.True(hit, "a downward ray should hit the floor");
        Assert.Equal(floor.Id, info.Entity.Id);
        Assert.InRange(info.Distance, 4.3f, 4.7f); // 5 - 0.5
        Assert.True(info.Normal.Y > 0.5f, "the floor normal should point up");
    }

    [Fact]
    public void Raycast_MissesWhenNothingInPath()
    {
        var scene = new Scene();
        var floor = scene.Instantiate("floor");
        floor.AddComponent(new BoxCollider3D { Size = new Vector3(2, 1, 2) });
        floor.GetComponent<Transform>().Position = Vector3.Zero;

        using var physics = new BepuPhysics3D();
        physics.Step(scene, Dt);

        bool hit = physics.Raycast(scene, new Vector3(50, 5, 50), new Vector3(0, -1, 0), 10f, out _);

        Assert.False(hit);
    }

    [Fact]
    public void Raycast_SkipsCollidersOutsideTheLayerMask()
    {
        var scene = new Scene();
        var floor = scene.Instantiate("floor");
        floor.AddComponent(new BoxCollider3D { Size = new Vector3(20, 1, 20) }); // top at y = 0.5

        // A body on layer 1 sits between the ray's origin and the floor, like a shooter's own capsule.
        var shooter = scene.Instantiate("shooter");
        shooter.AddComponent(new BoxCollider3D { Size = Vector3.One, Layer = 1 });
        shooter.GetComponent<Transform>().Position = new Vector3(0, 3, 0);

        using var physics = new BepuPhysics3D();
        physics.Step(scene, Dt);

        Assert.True(physics.Raycast(scene, new Vector3(0, 5, 0), -Vector3.UnitY, 10f, PhysicsSettings.AllLayers, true, out RaycastHit all));
        Assert.Equal(shooter.Id, all.Entity.Id);

        Assert.True(physics.Raycast(scene, new Vector3(0, 5, 0), -Vector3.UnitY, 10f, ~(1u << 1), true, out RaycastHit masked));
        Assert.Equal(floor.Id, masked.Entity.Id);
    }

    [Fact]
    public void Raycast_PassesThroughTriggersUnlessAsked()
    {
        var scene = new Scene();
        var floor = scene.Instantiate("floor");
        floor.AddComponent(new BoxCollider3D { Size = new Vector3(20, 1, 20) });

        var volume = scene.Instantiate("volume");
        volume.AddComponent(new BoxCollider3D { Size = new Vector3(4, 1, 4), IsTrigger = true });
        volume.GetComponent<Transform>().Position = new Vector3(0, 3, 0);

        using var physics = new BepuPhysics3D();
        physics.Step(scene, Dt);

        Assert.True(physics.Raycast(scene, new Vector3(0, 5, 0), -Vector3.UnitY, 10f, PhysicsSettings.AllLayers, false, out RaycastHit solid));
        Assert.Equal(floor.Id, solid.Entity.Id);

        Assert.True(physics.Raycast(scene, new Vector3(0, 5, 0), -Vector3.UnitY, 10f, PhysicsSettings.AllLayers, true, out RaycastHit any));
        Assert.Equal(volume.Id, any.Entity.Id);
    }

    [Fact]
    public void AddImpulse_ChangesVelocityByImpulseOverMass()
    {
        var scene = new Scene();
        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider3D { Size = Vector3.One });
        var body = box.AddComponent(new PhysicsBody3D { Mass = 2f });

        using var physics = new BepuPhysics3D();
        physics.Step(scene, Dt);

        // Gravity only acts on Y, so the horizontal velocity is the impulse's alone.
        body.AddImpulse(new Vector3(10f, 0f, 0f));
        physics.Step(scene, Dt);

        Assert.InRange(body.Velocity.X, 4.9f, 5.1f); // 10 N·s / 2 kg
        Assert.True(box.GetComponent<Transform>().Position.X > 0f, "the body should move along the impulse");
        Assert.False(body.HasPendingImpulses, "an applied impulse is consumed");
    }

    [Fact]
    public void AddImpulseAtPosition_OffCenterSpinsTheBody()
    {
        var scene = new Scene();
        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider3D { Size = Vector3.One });
        var body = box.AddComponent(new PhysicsBody3D { Mass = 1f });
        var transform = box.GetComponent<Transform>();

        using var physics = new BepuPhysics3D();
        physics.Step(scene, Dt);

        // A push along +X on the top edge tips the box around Z.
        body.AddImpulseAtPosition(new Vector3(2f, 0f, 0f), new Vector3(0f, 0.5f, 0f));
        Run(physics, scene, 10);

        Assert.True(body.Velocity.X > 1.5f, "the box should move along the impulse");
        Assert.True(MathF.Abs(transform.Rotation.Z) > 1f, $"an off-center impulse should rotate the box (rotation {transform.Rotation})");
    }

    [Fact]
    public void SettingADynamicBodysTransform_TeleportsIt()
    {
        var scene = new Scene();
        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider3D { Size = Vector3.One });
        var body = box.AddComponent(new PhysicsBody3D());
        var transform = box.GetComponent<Transform>();
        transform.Position = new Vector3(0, 10, 0);

        using var physics = new BepuPhysics3D();
        Run(physics, scene, 60); // falls a few meters

        // A respawn: put it back up and stop it. Without the teleport the simulation's pose would win.
        transform.Position = new Vector3(5, 20, 0);
        transform.Rotation = Vector3.Zero;
        body.Velocity = Vector3.Zero;
        physics.Step(scene, Dt);

        Assert.InRange(transform.Position.X, 4.99f, 5.01f);
        Assert.InRange(transform.Position.Y, 19.9f, 20.01f);
    }

    [Fact]
    public void ADynamicBodyLeftAlone_IsNotPulledBackToItsTransform()
    {
        var scene = new Scene();
        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider3D { Size = Vector3.One });
        box.AddComponent(new PhysicsBody3D());
        var transform = box.GetComponent<Transform>();
        transform.Position = new Vector3(0, 10, 0);

        using var physics = new BepuPhysics3D();
        Run(physics, scene, 30);
        float y = transform.Position.Y;
        Run(physics, scene, 30);

        Assert.True(transform.Position.Y < y - 1.0f, "a falling body keeps falling when no script moves it");
    }

    [Fact]
    public void ARisingKinematicPlatform_CarriesTheBodyOnIt()
    {
        var scene = new Scene();
        var lift = scene.Instantiate("lift");
        lift.AddComponent(new BoxCollider3D { Size = new Vector3(4, 0.5f, 4) });
        lift.AddComponent(new PhysicsBody3D { IsKinematic = true });
        var liftTransform = lift.GetComponent<Transform>();

        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider3D { Size = Vector3.One });
        box.AddComponent(new PhysicsBody3D { Mass = 5f });
        var boxTransform = box.GetComponent<Transform>();
        boxTransform.Position = new Vector3(0, 0.75f, 0); // resting on the platform's top at 0.25

        using var physics = new BepuPhysics3D();
        Run(physics, scene, 30);

        // Raise the lift 6 m at 3 m/s, moving it by its transform as a script would.
        for (int i = 1; i <= 120; i++)
        {
            liftTransform.Position = new Vector3(0, i * 3f * Dt, 0);
            physics.Step(scene, Dt);
        }

        Assert.InRange(boxTransform.Position.Y, 6.5f, 7.0f); // the lift's top is at 6.25: the box rode up on it
    }

    [Fact]
    public void ABodyInsideATriggerVolume_IsNotGrounded()
    {
        var scene = new Scene();
        var floor = scene.Instantiate("floor");
        floor.AddComponent(new BoxCollider3D { Size = new Vector3(20, 1, 20) }); // top at y = 0.5

        // A tall zone volume, like an area trigger the player walks and jumps through.
        var zone = scene.Instantiate("zone");
        zone.AddComponent(new BoxCollider3D { Size = new Vector3(20, 8, 20), IsTrigger = true });
        zone.GetComponent<Transform>().Position = new Vector3(0, 4, 0);

        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider3D { Size = Vector3.One });
        var body = box.AddComponent(new PhysicsBody3D());
        box.GetComponent<Transform>().Position = new Vector3(0, 5, 0);

        using var physics = new BepuPhysics3D();
        Run(physics, scene, 5); // still high in the air, inside the volume
        Assert.False(body.Grounded, "a body in mid-air must not stand on the trigger it is inside");

        Run(physics, scene, 120); // landed on the floor
        Assert.True(body.Grounded, "a body resting on the floor is grounded");
    }

    [Fact]
    public void AddImpulse_IsDroppedForStaticBodies()
    {
        var scene = new Scene();
        var wall = scene.Instantiate("wall");
        wall.AddComponent(new BoxCollider3D { Size = Vector3.One });
        var body = wall.AddComponent(new PhysicsBody3D { IsDynamic = false });

        using var physics = new BepuPhysics3D();
        body.AddImpulse(Vector3.UnitX);
        physics.Step(scene, Dt);

        Assert.False(body.HasPendingImpulses);
        Assert.Equal(Vector3.Zero, wall.GetComponent<Transform>().Position);
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(15f, 40f, 0f)]
    [InlineData(-20f, 120f, 10f)]
    [InlineData(30f, -80f, -25f)]
    public void QuaternionToEuler_RoundTripsWithCreateFromYawPitchRoll(float pitch, float yaw, float roll)
    {
        const float d2r = MathF.PI / 180f;
        var q = Quaternion.CreateFromYawPitchRoll(yaw * d2r, pitch * d2r, roll * d2r);

        Vector3 euler = BepuPhysics3D.QuaternionToEuler(q);

        // Compare via the reconstructed quaternion so equivalent angle representations still match.
        var back = Quaternion.CreateFromYawPitchRoll(euler.Y * d2r, euler.X * d2r, euler.Z * d2r);
        float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(q), Quaternion.Normalize(back)));
        Assert.True(dot > 0.999f, $"round-trip mismatch: in=({pitch},{yaw},{roll}) out=({euler.X},{euler.Y},{euler.Z}) dot={dot}");
    }
}

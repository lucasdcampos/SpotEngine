using System.Numerics;
using Spot.Physics;
using Spot.Physics.Aether;
using Spot.Scenes;

namespace Spot.Engine.Tests;

public class AetherPhysics2DTests
{
    private const float Dt = 1f / 60f;

    private static void Run(AetherPhysics2D physics, Scene scene, int steps)
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
        e.AddComponent(new BoxCollider2DComponent { Size = Vector2.One });
        var body = e.AddComponent(new PhysicsBody2DComponent { IsDynamic = true });
        e.GetComponent<TransformComponent>().Position = new Vector3(0, 10, 0);

        using var physics = new AetherPhysics2D();
        Run(physics, scene, 30);

        var t = e.GetComponent<TransformComponent>();
        Assert.True(t.Position.Y < 10f, "a dynamic body should fall");
        Assert.True(body.Velocity.Y < 0f, "a falling body should have downward velocity");
    }

    [Fact]
    public void DynamicBox_RestsOnStaticFloor()
    {
        var scene = new Scene();

        var floor = scene.Instantiate("floor");
        floor.AddComponent(new BoxCollider2DComponent { Size = new Vector2(20, 1) }); // top at y = 0.5
        floor.GetComponent<TransformComponent>().Position = Vector3.Zero;

        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider2DComponent { Size = Vector2.One });
        var body = box.AddComponent(new PhysicsBody2DComponent { IsDynamic = true });
        box.GetComponent<TransformComponent>().Position = new Vector3(0, 5, 0);

        using var physics = new AetherPhysics2D();
        Run(physics, scene, 240); // ~4 seconds

        // Floor top (0.5) + box half-height (0.5) = 1.0.
        float y = box.GetComponent<TransformComponent>().Position.Y;
        Assert.InRange(y, 0.8f, 1.25f);
        Assert.True(MathF.Abs(body.Velocity.Y) < 0.5f, "a rested body should have near-zero vertical velocity");
    }

    [Fact]
    public void CircleCollider_IsSimulated()
    {
        var scene = new Scene();

        var floor = scene.Instantiate("floor");
        floor.AddComponent(new BoxCollider2DComponent { Size = new Vector2(20, 1) });
        floor.GetComponent<TransformComponent>().Position = Vector3.Zero;

        var ball = scene.Instantiate("ball");
        ball.AddComponent(new CircleCollider2DComponent { Radius = 0.5f });
        ball.AddComponent(new PhysicsBody2DComponent { IsDynamic = true });
        ball.GetComponent<TransformComponent>().Position = new Vector3(0, 5, 0);

        using var physics = new AetherPhysics2D();
        Run(physics, scene, 240);

        // Floor top (0.5) + ball radius (0.5) = 1.0.
        float y = ball.GetComponent<TransformComponent>().Position.Y;
        Assert.InRange(y, 0.8f, 1.25f);
    }

    [Fact]
    public void Raycast_HitsStaticFloorAndReportsEntity()
    {
        var scene = new Scene();
        var floor = scene.Instantiate("floor");
        floor.AddComponent(new BoxCollider2DComponent { Size = new Vector2(20, 1) }); // top at y = 0.5
        floor.GetComponent<TransformComponent>().Position = Vector3.Zero;

        using var physics = new AetherPhysics2D();
        physics.Step(scene, Dt); // populate the simulation

        bool hit = physics.Raycast(scene, new Vector2(0, 5), new Vector2(0, -1), 10f, out RaycastHit2D info);

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
        floor.AddComponent(new BoxCollider2DComponent { Size = new Vector2(2, 1) });
        floor.GetComponent<TransformComponent>().Position = Vector3.Zero;

        using var physics = new AetherPhysics2D();
        physics.Step(scene, Dt);

        bool hit = physics.Raycast(scene, new Vector2(50, 5), new Vector2(0, -1), 10f, out _);

        Assert.False(hit);
    }

    [Fact]
    public void Trigger_ReportsSensorContact()
    {
        Vector2 savedGravity = PhysicsSettings.Gravity2D;
        PhysicsSettings.Gravity2D = Vector2.Zero; // isolate the overlap from falling
        try
        {
            var scene = new Scene();

            var sensor = scene.Instantiate("sensor");
            sensor.AddComponent(new BoxCollider2DComponent { Size = Vector2.One, IsTrigger = true });
            sensor.GetComponent<TransformComponent>().Position = Vector3.Zero;

            var mover = scene.Instantiate("mover");
            mover.AddComponent(new BoxCollider2DComponent { Size = Vector2.One });
            mover.AddComponent(new PhysicsBody2DComponent { IsDynamic = true });
            mover.GetComponent<TransformComponent>().Position = new Vector3(0.3f, 0, 0);

            using var physics = new AetherPhysics2D();
            Run(physics, scene, 3);

            bool found = false;
            foreach (ContactPair pair in physics.Contacts)
            {
                bool involvesBoth =
                    (pair.A.Id == sensor.Id && pair.B.Id == mover.Id) ||
                    (pair.A.Id == mover.Id && pair.B.Id == sensor.Id);
                if (involvesBoth && pair.IsTrigger)
                {
                    found = true;
                    break;
                }
            }

            Assert.True(found, "an overlapping sensor should report a trigger contact");
        }
        finally
        {
            PhysicsSettings.Gravity2D = savedGravity;
        }
    }
}

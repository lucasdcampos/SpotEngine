using System.Numerics;
using Spot.Physics;
using Spot.Physics.Bepu;
using Spot.Rendering;
using Spot.Scenes;

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
        e.AddComponent(new BoxCollider3DComponent { Size = Vector3.One });
        var body = e.AddComponent(new PhysicsBody3DComponent { IsDynamic = true });
        var t = e.GetComponent<TransformComponent>();
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
        floor.AddComponent(new BoxCollider3DComponent { Size = new Vector3(20, 1, 20) }); // top at y = 0.5
        floor.GetComponent<TransformComponent>().Position = Vector3.Zero;

        var box = scene.Instantiate("box");
        box.AddComponent(new BoxCollider3DComponent { Size = Vector3.One });
        var body = box.AddComponent(new PhysicsBody3DComponent { IsDynamic = true });
        var t = box.GetComponent<TransformComponent>();
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
        floor.AddComponent(new BoxCollider3DComponent { Size = new Vector3(20, 1, 20) }); // top at y = 0.5
        floor.GetComponent<TransformComponent>().Position = Vector3.Zero;

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
        floor.AddComponent(new BoxCollider3DComponent { Size = new Vector3(2, 1, 2) });
        floor.GetComponent<TransformComponent>().Position = Vector3.Zero;

        using var physics = new BepuPhysics3D();
        physics.Step(scene, Dt);

        bool hit = physics.Raycast(scene, new Vector3(50, 5, 50), new Vector3(0, -1, 0), 10f, out _);

        Assert.False(hit);
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

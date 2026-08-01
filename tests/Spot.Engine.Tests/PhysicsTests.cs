using System.Numerics;
using Spot.Physics;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Engine.Tests;

public class PhysicsTests
{
    [Fact]
    public void Aabb_ExposesMinMaxAndContains()
    {
        var box = new Aabb(new Vector2(0, 0), new Vector2(2, 2)); // half extents (1, 1)

        Assert.Equal(new Vector2(-1, -1), box.Min);
        Assert.Equal(new Vector2(1, 1), box.Max);
        Assert.True(box.Contains(new Vector2(0.5f, -0.5f)));
        Assert.False(box.Contains(new Vector2(2, 2)));
    }

    [Fact]
    public void Aabb_IntersectsOverlappingButNotDistant()
    {
        var a = new Aabb(new Vector2(0, 0), new Vector2(2, 2));
        var overlapping = new Aabb(new Vector2(1.5f, 0), new Vector2(2, 2));
        var distant = new Aabb(new Vector2(10, 0), new Vector2(2, 2));

        Assert.True(a.Intersects(overlapping));
        Assert.False(a.Intersects(distant));
    }

    [Fact]
    public void FromTransform_UsesPositionAndScale()
    {
        var t = new TransformComponent { Position = new Vector3(3, 4, 0), Scale = new Vector3(2, 2, 1) };

        var box = Aabb.FromTransform(t);

        Assert.Equal(new Vector2(3, 4), box.Center);
        Assert.Equal(new Vector2(1, 1), box.HalfExtents);
    }

    [Fact]
    public void Update_AppliesGravityToDynamicBody()
    {
        var scene = new Scene();
        var e = scene.Instantiate();
        var body = e.AddComponent(new PhysicsBody2DComponent { IsDynamic = true });
        var t = e.GetComponent<TransformComponent>();

        Physics2DSystem.Update(scene, 0.1f);

        Assert.True(body.Velocity.Y < 0, "a dynamic body should accelerate downward");
        Assert.True(t.Position.Y < 0, "a dynamic body should move downward");
    }

    [Fact]
    public void Update_LeavesStaticBodyInPlace()
    {
        var scene = new Scene();
        var e = scene.Instantiate();
        var body = e.AddComponent(new PhysicsBody2DComponent { IsDynamic = false });
        var t = e.GetComponent<TransformComponent>();

        Physics2DSystem.Update(scene, 0.1f);

        Assert.Equal(Vector2.Zero, body.Velocity);
        Assert.Equal(Vector3.Zero, t.Position);
    }

    [Fact]
    public void Update_SeparatesOverlappingColliders()
    {
        Vector2 savedGravity = Physics2DSystem.Gravity;
        Physics2DSystem.Gravity = Vector2.Zero; // isolate collision resolution from gravity
        try
        {
            var scene = new Scene();

            var floor = scene.Instantiate("static");
            floor.AddComponent(new BoxCollider2DComponent { Size = Vector2.One });
            floor.AddComponent(new PhysicsBody2DComponent { IsDynamic = false });
            floor.GetComponent<TransformComponent>().Position = Vector3.Zero;

            var mover = scene.Instantiate("dynamic");
            mover.AddComponent(new BoxCollider2DComponent { Size = Vector2.One });
            mover.AddComponent(new PhysicsBody2DComponent { IsDynamic = true });
            mover.GetComponent<TransformComponent>().Position = new Vector3(0.4f, 0, 0);

            Physics2DSystem.Update(scene, 0.016f);

            // Penetration along X (0.6) is smaller than along Y (1.0), so the dynamic box is pushed out
            // along +X until the two unit boxes just touch (centers one full width apart).
            float x = mover.GetComponent<TransformComponent>().Position.X;
            Assert.True(x > 0.4f, "the dynamic collider should be pushed away from the static one");
            Assert.Equal(1.0, x, 3);
            Assert.Equal(Vector3.Zero, floor.GetComponent<TransformComponent>().Position); // static body unmoved
        }
        finally
        {
            Physics2DSystem.Gravity = savedGravity;
        }
    }
}

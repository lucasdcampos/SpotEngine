using System;
using System.Numerics;
using Spot.Scenes;
using Spot.Rendering;

namespace Spot.Physics;

/// <summary>
/// A basic 2D physics system that applies gravity, updates positions, and resolves simple AABB overlaps.
/// </summary>
internal static class Physics2DSystem
{
    public static Vector2 Gravity = new Vector2(0, -9.81f);

    public static void Update(Scene scene, float deltaTime)
    {
        // 1. Update velocities and positions for dynamic bodies
        foreach (var entity in scene.View<PhysicsBody2DComponent, Transform>())
        {
            var body = entity.GetComponent<PhysicsBody2DComponent>();
            var transform = entity.GetComponent<Transform>();

            if (body.IsDynamic)
            {
                body.Velocity += Gravity * body.GravityScale * deltaTime;
                transform.Position += new Vector3(body.Velocity * deltaTime, 0);
            }
        }

        // 2. Resolve Collisions (Basic Iterative AABB resolution)
        var colliders = scene.View<BoxCollider2DComponent, Transform>();
        
        for (int i = 0; i < colliders.Count; i++)
        {
            var e1 = colliders[i];
            var col1 = e1.GetComponent<BoxCollider2DComponent>();
            var t1 = e1.GetComponent<Transform>();
            var b1 = col1.GetWorldBounds(new Vector2(t1.Position.X, t1.Position.Y));
            bool isDynamic1 = e1.TryGetComponent(out PhysicsBody2DComponent? body1) && body1.IsDynamic;

            for (int j = i + 1; j < colliders.Count; j++)
            {
                var e2 = colliders[j];
                var col2 = e2.GetComponent<BoxCollider2DComponent>();
                var t2 = e2.GetComponent<Transform>();
                var b2 = col2.GetWorldBounds(new Vector2(t2.Position.X, t2.Position.Y));
                bool isDynamic2 = e2.TryGetComponent(out PhysicsBody2DComponent? body2) && body2.IsDynamic;

                if (!isDynamic1 && !isDynamic2) continue; // Static vs Static

                if (b1.Intersects(b2))
                {
                    ResolveCollision(t1, body1, b1, isDynamic1, t2, body2, b2, isDynamic2);
                    
                    // Update bounds after resolution for further checks
                    b1 = col1.GetWorldBounds(new Vector2(t1.Position.X, t1.Position.Y));
                }
            }
        }
    }

    private static void ResolveCollision(Transform t1, PhysicsBody2DComponent? body1, Aabb b1, bool isDynamic1, 
                                         Transform t2, PhysicsBody2DComponent? body2, Aabb b2, bool isDynamic2)
    {
        // Calculate penetration depths along X and Y
        float dx1 = b2.Max.X - b1.Min.X;
        float dx2 = b1.Max.X - b2.Min.X;
        float dy1 = b2.Max.Y - b1.Min.Y;
        float dy2 = b1.Max.Y - b2.Min.Y;

        float penX = Math.Min(dx1, dx2);
        float penY = Math.Min(dy1, dy2);

        Vector2 resolution = Vector2.Zero;

        if (penX < penY)
        {
            // Resolve along X axis
            float sign = (b1.Center.X < b2.Center.X) ? -1 : 1;
            resolution = new Vector2(penX * sign, 0);

            if (isDynamic1 && body1 != null) body1.Velocity = new Vector2(0, body1.Velocity.Y);
            if (isDynamic2 && body2 != null) body2.Velocity = new Vector2(0, body2.Velocity.Y);
        }
        else
        {
            // Resolve along Y axis
            float sign = (b1.Center.Y < b2.Center.Y) ? -1 : 1;
            resolution = new Vector2(0, penY * sign);

            if (isDynamic1 && body1 != null) body1.Velocity = new Vector2(body1.Velocity.X, 0);
            if (isDynamic2 && body2 != null) body2.Velocity = new Vector2(body2.Velocity.X, 0);
        }

        // Apply resolution
        if (isDynamic1 && isDynamic2)
        {
            t1.Position += new Vector3(resolution * 0.5f, 0);
            t2.Position -= new Vector3(resolution * 0.5f, 0);
        }
        else if (isDynamic1)
        {
            t1.Position += new Vector3(resolution, 0);
        }
        else if (isDynamic2)
        {
            t2.Position -= new Vector3(resolution, 0);
        }
    }
}

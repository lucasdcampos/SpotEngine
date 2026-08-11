using System;
using System.Numerics;
using Spot.Scenes;
using Spot.Rendering;

namespace Spot.Physics;

/// <summary>
/// A basic 3D physics system that applies gravity, updates positions, and resolves simple AABB overlaps.
/// </summary>
internal static class Physics3DSystem
{
    public static void Update(Scene scene, float deltaTime)
    {
        // 1. Update velocities and positions for dynamic bodies
        foreach (var entity in scene.View<PhysicsBody3DComponent, TransformComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var body = entity.GetComponent<PhysicsBody3DComponent>();
            var transform = entity.GetComponent<TransformComponent>();
            if (!body.Enabled || !transform.Enabled) continue;

            if (body.IsDynamic)
            {
                // Cleared each step; a floor contact in the resolution pass below sets it back to true.
                body.Grounded = false;

                if (body.LinearDrag > 0f)
                {
                    // Apply linear drag (damping) to gradually reduce velocity over time
                    body.Velocity -= body.Velocity * body.LinearDrag * deltaTime;
                }
                body.Velocity += PhysicsSettings.Gravity * body.GravityScale * deltaTime;
                transform.Position += body.Velocity * deltaTime;
            }
        }

        // 2. Resolve Collisions (Basic Iterative AABB resolution)
        var colliders = scene.View<BoxCollider3DComponent, TransformComponent>();
        
        for (int i = 0; i < colliders.Count; i++)
        {
            var e1 = colliders[i];
            if (!e1.IsActiveInHierarchy()) continue;
            var col1 = e1.GetComponent<BoxCollider3DComponent>();
            var t1 = e1.GetComponent<TransformComponent>();
            if (!col1.Enabled || !t1.Enabled) continue;
            var b1 = col1.GetWorldBounds(t1.WorldPosition, t1.WorldScale);
            bool isDynamic1 = e1.TryGetComponent(out PhysicsBody3DComponent? body1) && body1.Enabled && body1.IsDynamic;

            for (int j = i + 1; j < colliders.Count; j++)
            {
                var e2 = colliders[j];
                if (!e2.IsActiveInHierarchy()) continue;
                var col2 = e2.GetComponent<BoxCollider3DComponent>();
                var t2 = e2.GetComponent<TransformComponent>();
                if (!col2.Enabled || !t2.Enabled) continue;
                var b2 = col2.GetWorldBounds(t2.WorldPosition, t2.WorldScale);
                bool isDynamic2 = e2.TryGetComponent(out PhysicsBody3DComponent? body2) && body2.Enabled && body2.IsDynamic;

                if (!isDynamic1 && !isDynamic2) continue; // Static vs Static

                if (b1.Intersects(b2))
                {
                    ResolveCollision(t1, body1, b1, isDynamic1, t2, body2, b2, isDynamic2);
                    
                    // Update bounds after resolution for further checks
                    b1 = col1.GetWorldBounds(t1.WorldPosition, t1.WorldScale);
                }
            }
        }
    }

    private static void ResolveCollision(TransformComponent t1, PhysicsBody3DComponent? body1, Aabb3d b1, bool isDynamic1, 
                                         TransformComponent t2, PhysicsBody3DComponent? body2, Aabb3d b2, bool isDynamic2)
    {
        // Calculate penetration depths along X, Y and Z
        float dx1 = b2.Max.X - b1.Min.X;
        float dx2 = b1.Max.X - b2.Min.X;
        float dy1 = b2.Max.Y - b1.Min.Y;
        float dy2 = b1.Max.Y - b2.Min.Y;
        float dz1 = b2.Max.Z - b1.Min.Z;
        float dz2 = b1.Max.Z - b2.Min.Z;

        float penX = Math.Min(dx1, dx2);
        float penY = Math.Min(dy1, dy2);
        float penZ = Math.Min(dz1, dz2);

        Vector3 resolution = Vector3.Zero;

        if (penX < penY && penX < penZ)
        {
            // Resolve along X axis
            float sign = (b1.Center.X < b2.Center.X) ? -1 : 1;
            resolution = new Vector3(penX * sign, 0, 0);

            if (isDynamic1 && body1 != null) body1.Velocity = new Vector3(0, body1.Velocity.Y, body1.Velocity.Z);
            if (isDynamic2 && body2 != null) body2.Velocity = new Vector3(0, body2.Velocity.Y, body2.Velocity.Z);
        }
        else if (penY < penX && penY < penZ)
        {
            // Resolve along Y axis
            float sign = (b1.Center.Y < b2.Center.Y) ? -1 : 1;
            resolution = new Vector3(0, penY * sign, 0);

            if (isDynamic1 && body1 != null) body1.Velocity = new Vector3(body1.Velocity.X, 0, body1.Velocity.Z);
            if (isDynamic2 && body2 != null) body2.Velocity = new Vector3(body2.Velocity.X, 0, body2.Velocity.Z);

            // A positive Y resolution pushes body1 up (it landed on body2); a negative one pushes
            // body2 up. Mark whichever body was lifted as grounded for this step.
            if (isDynamic1 && body1 != null && resolution.Y > 0f) body1.Grounded = true;
            if (isDynamic2 && body2 != null && resolution.Y < 0f) body2.Grounded = true;
        }
        else
        {
            // Resolve along Z axis
            float sign = (b1.Center.Z < b2.Center.Z) ? -1 : 1;
            resolution = new Vector3(0, 0, penZ * sign);

            if (isDynamic1 && body1 != null) body1.Velocity = new Vector3(body1.Velocity.X, body1.Velocity.Y, 0);
            if (isDynamic2 && body2 != null) body2.Velocity = new Vector3(body2.Velocity.X, body2.Velocity.Y, 0);
        }

        // Apply resolution
        if (isDynamic1 && isDynamic2)
        {
            t1.Position += resolution * 0.5f;
            t2.Position -= resolution * 0.5f;
        }
        else if (isDynamic1)
        {
            t1.Position += resolution;
        }
        else if (isDynamic2)
        {
            t2.Position -= resolution;
        }
    }
}

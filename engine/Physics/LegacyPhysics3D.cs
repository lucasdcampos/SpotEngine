using System;
using System.Collections.Generic;
using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Framework;

namespace Spot.Engine.Physics;

/// <summary>
/// Adapts the engine's built-in AABB solver (<see cref="Physics3DSystem"/>) to the
/// <see cref="IPhysics3D"/> interface so it can serve as a selectable fallback backend. Supports only
/// axis-aligned box colliders and linear motion; raycasting and collision callbacks are not implemented, and
/// impulses only change a body's linear velocity.
/// </summary>
internal sealed class LegacyPhysics3D : IPhysics3D
{
    private bool _warnedRaycast;

    public void Step(Scene scene, float deltaTime)
    {
        ApplyImpulses(scene);
        Physics3DSystem.Update(scene, deltaTime);
    }

    // The AABB solver has no rotation, so an impulse is just a change in linear velocity, wherever it lands.
    private static void ApplyImpulses(Scene scene)
    {
        foreach (Entity entity in scene.View<PhysicsBody3DComponent>())
        {
            PhysicsBody3DComponent body = entity.GetComponent<PhysicsBody3DComponent>();
            if (!body.HasPendingImpulses) continue;

            if (body.IsDynamic && !body.IsKinematic)
            {
                body.Velocity += body.PendingLinearImpulse() / MathF.Max(0.0001f, body.Mass);
            }

            body.ClearPendingImpulses();
        }
    }

    /// <summary>The legacy solver does not report contacts; switch to Bepu for collision/trigger callbacks.</summary>
    public IReadOnlyList<ContactPair> Contacts => Array.Empty<ContactPair>();

    public bool Raycast(Scene scene, Vector3 origin, Vector3 direction, float maxDistance, uint layerMask, bool hitTriggers, out RaycastHit hit)
    {
        hit = default;
        if (!_warnedRaycast)
        {
            Log.CoreWarn("Raycast is not supported by the legacy 3D physics backend; switch to Bepu to use it.");
            _warnedRaycast = true;
        }
        return false;
    }

    public void Dispose()
    {
    }
}

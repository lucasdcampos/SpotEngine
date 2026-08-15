using System.Numerics;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// Adapts the engine's built-in AABB solver (<see cref="Physics2DSystem"/>) to the
/// <see cref="IPhysics2D"/> interface so it can serve as a selectable fallback backend. Supports only
/// axis-aligned box colliders and linear motion; raycasting and collision callbacks are not implemented.
/// Mirrors <see cref="LegacyPhysics3D"/>.
/// </summary>
internal sealed class LegacyPhysics2D : IPhysics2D
{
    private bool _warnedRaycast;

    public void Step(Scene scene, float deltaTime) => Physics2DSystem.Update(scene, deltaTime);

    /// <summary>The legacy solver does not report contacts; switch to Aether for collision/trigger callbacks.</summary>
    public IReadOnlyList<ContactPair> Contacts => Array.Empty<ContactPair>();

    public bool Raycast(Scene scene, Vector2 origin, Vector2 direction, float maxDistance, out RaycastHit2D hit)
    {
        hit = default;
        if (!_warnedRaycast)
        {
            Log.CoreWarn("Raycast2D is not supported by the legacy 2D physics backend; switch to Aether to use it.");
            _warnedRaycast = true;
        }
        return false;
    }

    public void Dispose()
    {
    }
}

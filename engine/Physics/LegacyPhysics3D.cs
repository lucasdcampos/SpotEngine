using System.Numerics;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// Adapts the engine's built-in AABB solver (<see cref="Physics3DSystem"/>) to the
/// <see cref="IPhysics3D"/> interface so it can serve as a selectable fallback backend. Supports only
/// axis-aligned box colliders and linear motion; raycasting is not implemented.
/// </summary>
internal sealed class LegacyPhysics3D : IPhysics3D
{
    private bool _warnedRaycast;

    public void Step(Scene scene, float deltaTime) => Physics3DSystem.Update(scene, deltaTime);

    public bool Raycast(Scene scene, Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
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

using System.Numerics;

namespace Spot.Physics;

/// <summary>
/// Selects which 3D physics backend a scene uses for its runtime simulation.
/// </summary>
public enum Physics3DBackend
{
    /// <summary>BepuPhysics v2 — a full rigid-body simulation (default).</summary>
    Bepu,

    /// <summary>The engine's built-in AABB solver — boxes only, kept as a fallback.</summary>
    Legacy,
}

/// <summary>
/// Global, engine-wide knobs for 3D physics. Following the engine's "graphics customization"
/// convention, physics is tuned through this single static surface rather than per-scene wiring.
/// Values are read when a scene builds its physics backend (and gravity is read every step), so
/// changes take effect on the next scene that starts play mode.
/// </summary>
public static class PhysicsSettings
{
    /// <summary>The backend new scenes create for their 3D simulation. Defaults to <see cref="Physics3DBackend.Bepu"/>.</summary>
    public static Physics3DBackend Backend { get; set; } = Physics3DBackend.Bepu;

    /// <summary>World gravity applied to dynamic bodies, in units/second². Read every physics step.</summary>
    public static Vector3 Gravity { get; set; } = new Vector3(0f, -9.81f, 0f);
}

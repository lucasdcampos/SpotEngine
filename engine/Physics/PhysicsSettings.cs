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
/// Selects which 2D physics backend a scene uses for its runtime simulation.
/// </summary>
public enum Physics2DBackend
{
    /// <summary>Aether.Physics2D — a full rigid-body simulation (default).</summary>
    Aether,

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
    /// <summary>The number of collision layers, matching the 32-bit collision mask.</summary>
    public const int LayerCount = 32;

    // Per-layer bitmask of which other layers it collides with; symmetric. All-ones = everything collides.
    private static readonly uint[] s_layerMask = CreateFullMask();

    /// <summary>The backend new scenes create for their 3D simulation. Defaults to <see cref="Physics3DBackend.Bepu"/>.</summary>
    public static Physics3DBackend Backend { get; set; } = Physics3DBackend.Bepu;

    /// <summary>The backend new scenes create for their 2D simulation. Defaults to <see cref="Physics2DBackend.Aether"/>.</summary>
    public static Physics2DBackend Backend2D { get; set; } = Physics2DBackend.Aether;

    /// <summary>World gravity applied to dynamic bodies, in units/second². Read every physics step.</summary>
    public static Vector3 Gravity { get; set; } = new Vector3(0f, -9.81f, 0f);

    /// <summary>World gravity applied to 2D dynamic bodies, in units/second². Read every 2D physics step.</summary>
    public static Vector2 Gravity2D { get; set; } = new Vector2(0f, -9.81f);

    /// <summary>
    /// Enables or disables collisions between two layers (0..<see cref="LayerCount"/>-1). Symmetric, so
    /// <c>SetLayerCollision(a, b, …)</c> also affects <c>(b, a)</c>. A collider's layer is set on its
    /// component (<see cref="Collider3DComponent.Layer"/>, <see cref="Collider2DComponent.Layer"/>). Honored by
    /// the Bepu and Aether backends (the 2D backend maps layers 0..30 onto collision categories).
    /// </summary>
    public static void SetLayerCollision(int layerA, int layerB, bool shouldCollide)
    {
        if ((uint)layerA >= LayerCount || (uint)layerB >= LayerCount) return;

        uint bitB = 1u << layerB;
        uint bitA = 1u << layerA;
        if (shouldCollide)
        {
            s_layerMask[layerA] |= bitB;
            s_layerMask[layerB] |= bitA;
        }
        else
        {
            s_layerMask[layerA] &= ~bitB;
            s_layerMask[layerB] &= ~bitA;
        }
    }

    /// <summary>Whether two layers are set to collide. Out-of-range layers are treated as colliding.</summary>
    public static bool LayersCollide(int layerA, int layerB)
    {
        if ((uint)layerA >= LayerCount || (uint)layerB >= LayerCount) return true;
        return (s_layerMask[layerA] & (1u << layerB)) != 0;
    }

    /// <summary>Restores the default matrix where every layer collides with every other layer.</summary>
    public static void ResetLayerCollisions()
    {
        for (int i = 0; i < LayerCount; i++)
        {
            s_layerMask[i] = 0xFFFFFFFF;
        }
    }

    private static uint[] CreateFullMask()
    {
        var mask = new uint[LayerCount];
        for (int i = 0; i < LayerCount; i++)
        {
            mask[i] = 0xFFFFFFFF;
        }

        return mask;
    }
}

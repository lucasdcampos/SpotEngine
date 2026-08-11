using System.Numerics;

namespace Spot.Core;

/// <summary>
/// Small numeric helpers shared across the engine's systems and renderers.
/// </summary>
public static class SpotMath
{
    /// <summary>
    /// Normalizes <paramref name="v"/>, returning <paramref name="fallback"/> when it is too close to zero
    /// to have a stable direction. Avoids the NaNs that <see cref="Vector3.Normalize(Vector3)"/> produces
    /// for a (near-)zero vector.
    /// </summary>
    /// <param name="v">The vector to normalize.</param>
    /// <param name="fallback">The direction to return when <paramref name="v"/> is degenerate.</param>
    public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback) =>
        v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : fallback;
}

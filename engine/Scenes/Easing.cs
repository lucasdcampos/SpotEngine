namespace Spot.Scenes;

/// <summary>
/// An easing curve applied to a tween's normalized progress, shaping how a value accelerates and
/// decelerates over its duration.
/// </summary>
public enum Ease
{
    /// <summary>Constant rate; a straight line from start to end.</summary>
    Linear,

    /// <summary>Quadratic ease-in: starts slow.</summary>
    InQuad,

    /// <summary>Quadratic ease-out: ends slow.</summary>
    OutQuad,

    /// <summary>Quadratic ease-in and ease-out: slow at both ends.</summary>
    InOutQuad,

    /// <summary>Cubic ease-in: starts slower.</summary>
    InCubic,

    /// <summary>Cubic ease-out: ends slower.</summary>
    OutCubic,

    /// <summary>Cubic ease-in and ease-out.</summary>
    InOutCubic,

    /// <summary>Sinusoidal ease-in.</summary>
    InSine,

    /// <summary>Sinusoidal ease-out.</summary>
    OutSine,

    /// <summary>Sinusoidal ease-in and ease-out.</summary>
    InOutSine,

    /// <summary>Overshoots slightly past the end before settling (anticipation on the way out).</summary>
    OutBack,

    /// <summary>Settles onto the end value with a decaying bounce.</summary>
    OutBounce,
}

/// <summary>
/// Evaluates <see cref="Ease"/> curves. Each function maps a normalized time <c>t</c> in
/// [0, 1] to an eased value, generally also in [0, 1] (some curves briefly overshoot).
/// </summary>
public static class Easing
{
    private const float BackOvershoot = 1.70158f;

    /// <summary>
    /// Maps normalized progress <paramref name="t"/> through the given easing curve. Input is clamped
    /// to [0, 1].
    /// </summary>
    /// <param name="ease">The curve to apply.</param>
    /// <param name="t">Normalized progress in [0, 1].</param>
    /// <returns>The eased progress.</returns>
    public static float Evaluate(Ease ease, float t)
    {
        t = Math.Clamp(t, 0.0f, 1.0f);
        return ease switch
        {
            Ease.Linear => t,
            Ease.InQuad => t * t,
            Ease.OutQuad => 1.0f - (1.0f - t) * (1.0f - t),
            Ease.InOutQuad => t < 0.5f ? 2.0f * t * t : 1.0f - MathF.Pow(-2.0f * t + 2.0f, 2.0f) / 2.0f,
            Ease.InCubic => t * t * t,
            Ease.OutCubic => 1.0f - MathF.Pow(1.0f - t, 3.0f),
            Ease.InOutCubic => t < 0.5f ? 4.0f * t * t * t : 1.0f - MathF.Pow(-2.0f * t + 2.0f, 3.0f) / 2.0f,
            Ease.InSine => 1.0f - MathF.Cos(t * MathF.PI / 2.0f),
            Ease.OutSine => MathF.Sin(t * MathF.PI / 2.0f),
            Ease.InOutSine => -(MathF.Cos(MathF.PI * t) - 1.0f) / 2.0f,
            Ease.OutBack => OutBack(t),
            Ease.OutBounce => OutBounce(t),
            _ => t,
        };
    }

    private static float OutBack(float t)
    {
        const float c1 = BackOvershoot;
        const float c3 = c1 + 1.0f;
        float p = t - 1.0f;
        return 1.0f + c3 * p * p * p + c1 * p * p;
    }

    private static float OutBounce(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;

        if (t < 1.0f / d1)
        {
            return n1 * t * t;
        }

        if (t < 2.0f / d1)
        {
            t -= 1.5f / d1;
            return n1 * t * t + 0.75f;
        }

        if (t < 2.5f / d1)
        {
            t -= 2.25f / d1;
            return n1 * t * t + 0.9375f;
        }

        t -= 2.625f / d1;
        return n1 * t * t + 0.984375f;
    }
}

namespace Spot.Core;

/// <summary>
/// The engine's frame clock, queryable from anywhere (typically a script's update). Values are set
/// once per frame by <see cref="Application"/> and hold steady for the duration of that frame, so
/// every system sees a consistent delta.
/// </summary>
/// <remarks>
/// Gameplay (scenes, scripts, physics) advances on the <em>scaled</em> clock — multiply per-frame
/// motion by <see cref="DeltaTime"/> and it automatically respects <see cref="TimeScale"/> (set it to
/// 0 to pause, 0.5 for slow motion). For things that must ignore pause/slow-mo — UI animation, a
/// pause menu, camera smoothing — use <see cref="UnscaledDeltaTime"/> instead.
/// </remarks>
public static class Time
{
    private static float _timeScale = 1.0f;

    /// <summary>
    /// Gets the scaled time in seconds since the previous frame: <see cref="UnscaledDeltaTime"/>
    /// multiplied by <see cref="TimeScale"/>. This is the delta gameplay should use.
    /// </summary>
    public static float DeltaTime { get; private set; }

    /// <summary>
    /// Gets the real time in seconds since the previous frame, unaffected by <see cref="TimeScale"/>.
    /// </summary>
    public static float UnscaledDeltaTime { get; private set; }

    /// <summary>
    /// Gets or sets the multiplier applied to <see cref="DeltaTime"/>. 1 is normal speed, 0 pauses
    /// gameplay, values in between are slow motion. Negative values are clamped to 0.
    /// </summary>
    public static float TimeScale
    {
        get => _timeScale;
        set => _timeScale = value < 0.0f ? 0.0f : value;
    }

    /// <summary>
    /// Gets or sets the fixed timestep, in seconds, used by systems that advance on a deterministic
    /// clock (physics). Defaults to 0.02 (50 Hz).
    /// </summary>
    public static float FixedDeltaTime { get; set; } = 0.02f;

    /// <summary>
    /// Gets the total scaled time, in seconds, accumulated since startup. Advances by
    /// <see cref="DeltaTime"/> each frame, so it stops while paused and slows during slow motion.
    /// </summary>
    public static float ElapsedTime { get; private set; }

    /// <summary>
    /// Gets the total real time, in seconds, since startup, unaffected by <see cref="TimeScale"/>.
    /// </summary>
    public static float UnscaledTime { get; private set; }

    /// <summary>
    /// Gets the number of frames rendered since startup.
    /// </summary>
    public static long FrameCount { get; private set; }

    /// <summary>
    /// Advances the clock for a new frame from the real (already delta-clamped) elapsed time. Called
    /// by the application once per frame before any system updates.
    /// </summary>
    /// <param name="unscaledDeltaTime">The real elapsed seconds since the previous frame.</param>
    internal static void NewFrame(float unscaledDeltaTime)
    {
        UnscaledDeltaTime = unscaledDeltaTime;
        DeltaTime = unscaledDeltaTime * _timeScale;
        UnscaledTime += unscaledDeltaTime;
        ElapsedTime += DeltaTime;
        FrameCount++;
    }
}

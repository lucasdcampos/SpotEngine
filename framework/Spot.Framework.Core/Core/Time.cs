using System.Diagnostics;

namespace Spot.Framework;

/// <summary>
/// The frame clock, queryable from anywhere. Advance it once per frame — <see cref="Tick"/> measures the real
/// time itself, <see cref="NewFrame"/> takes a delta you measured — and its values hold steady for the rest of
/// the frame, so everything sees a consistent delta. The engine advances it for you.
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
    private static readonly Stopwatch s_clock = new();
    private static TimeSpan s_lastTick;

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
    /// If true, the next frame will advance by a fixed delta time regardless of TimeScale, 
    /// then reset this flag to false. Useful for step-by-step debugging while paused.
    /// </summary>
    public static bool StepNextFrame { get; set; }

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
    /// Advances the clock for a new frame from the real elapsed time (clamp it first if a hitch must not
    /// feed a huge step into your simulation). Call it once per frame before updating.
    /// </summary>
    /// <param name="unscaledDeltaTime">The real elapsed seconds since the previous frame.</param>
    public static void NewFrame(float unscaledDeltaTime)
    {
        unscaledDeltaTime = Math.Max(0.0f, unscaledDeltaTime);
        UnscaledDeltaTime = unscaledDeltaTime;

        if (StepNextFrame)
        {
            DeltaTime = FixedDeltaTime;
            StepNextFrame = false;
        }
        else
        {
            DeltaTime = unscaledDeltaTime * _timeScale;
        }

        UnscaledTime += unscaledDeltaTime;
        ElapsedTime += DeltaTime;
        FrameCount++;
    }

    /// <summary>
    /// Advances the clock by the real time elapsed since the previous <see cref="Tick"/> (zero on the first
    /// call), clamped to <paramref name="maxDeltaTime"/> so a hitch — a window drag, a GC pause, a breakpoint —
    /// can't feed a huge step into your simulation. Call it once per frame in your own loop.
    /// </summary>
    /// <param name="maxDeltaTime">The largest real delta a single frame may advance, in seconds.</param>
    /// <returns>The frame's scaled <see cref="DeltaTime"/>.</returns>
    public static float Tick(float maxDeltaTime = 0.1f)
    {
        float real = 0.0f;
        if (s_clock.IsRunning)
        {
            TimeSpan now = s_clock.Elapsed;
            real = (float)(now - s_lastTick).TotalSeconds;
            s_lastTick = now;
        }
        else
        {
            s_clock.Start();
            s_lastTick = TimeSpan.Zero;
        }

        NewFrame(Math.Min(real, maxDeltaTime));
        return DeltaTime;
    }

    /// <summary>
    /// Restores the clock to its startup state: zero time and frames, a time scale of 1, and the next
    /// <see cref="Tick"/> treated as the first.
    /// </summary>
    public static void Reset()
    {
        s_clock.Reset();
        s_lastTick = TimeSpan.Zero;
        _timeScale = 1.0f;
        DeltaTime = 0.0f;
        UnscaledDeltaTime = 0.0f;
        ElapsedTime = 0.0f;
        UnscaledTime = 0.0f;
        FrameCount = 0;
        StepNextFrame = false;
    }
}

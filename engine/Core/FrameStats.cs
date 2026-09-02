namespace Spot.Core;

/// <summary>
/// Lightweight rolling frame-time statistics for profiling. The application loop feeds it the real
/// (unclamped) wall-clock delta once per frame — before the simulation's delta clamp — so a genuine
/// stall shows up here instead of being hidden as a steady <c>MaxDeltaTime</c>. Read it from an overlay,
/// the <c>stats</c> console command, or a runtime debug panel.
/// </summary>
/// <remarks>
/// The smoothed value is an exponential moving average, which is cheaper and allocation-free compared to
/// keeping a ring buffer of samples, and steady enough to read on a HUD. To measure the engine's true
/// headroom, turn VSync off (<see cref="Spot.Rendering.RenderSettings.VSync"/> or the <c>vsync</c>
/// command) first — otherwise the blocking buffer swap caps the frame rate at the refresh rate and this
/// reports the cap, not the cost.
/// </remarks>
public static class FrameStats
{
    // ~0.1 tracks changes over roughly ten frames: responsive without the number jittering every frame.
    private const float Smoothing = 0.1f;

    /// <summary>The most recent frame's wall-clock duration, in milliseconds (unsmoothed).</summary>
    public static float LastFrameMs { get; private set; }

    /// <summary>The smoothed frame time, in milliseconds (exponential moving average of the real delta).</summary>
    public static float FrameTimeMs { get; private set; }

    /// <summary>The smoothed frames per second, derived from <see cref="FrameTimeMs"/>.</summary>
    public static float Fps => FrameTimeMs > 0.0001f ? 1000.0f / FrameTimeMs : 0.0f;

    /// <summary>
    /// Records one frame's real (unclamped) delta, in seconds. Called by the application loop each frame.
    /// </summary>
    /// <param name="realDeltaSeconds">The measured wall-clock time since the previous frame, in seconds.</param>
    public static void Record(float realDeltaSeconds)
    {
        float ms = realDeltaSeconds * 1000.0f;
        LastFrameMs = ms;

        // Seed the average on the first frame so it converges from a real sample rather than from zero.
        FrameTimeMs = FrameTimeMs <= 0.0001f ? ms : FrameTimeMs + (Smoothing * (ms - FrameTimeMs));
    }
}

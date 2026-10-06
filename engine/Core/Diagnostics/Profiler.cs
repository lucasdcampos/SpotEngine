using System.Collections.Generic;
using System.Diagnostics;

namespace Spot.Engine;

/// <summary>
/// Lightweight per-frame CPU profiler. Wrap any code block with <see cref="BeginSample"/> /
/// <see cref="EndSample"/> to record its cost; the most recent completed frame's timings are then
/// available via <see cref="Samples"/> for display in a profiler panel or overlay.
/// </summary>
/// <remarks>
/// Timings accumulate within the current frame and are snapped to <see cref="Samples"/> once per
/// frame via <see cref="FlushFrame"/>. Calling <c>BeginSample</c> twice on the same name without
/// an intervening <c>EndSample</c> restarts the timer for that name (the earlier start is lost).
/// </remarks>
public static class Profiler
{
    private static readonly Dictionary<string, float> s_current = new();
    private static readonly Dictionary<string, float> s_last    = new();
    private static readonly Dictionary<string, long>  s_starts  = new();

    /// <summary>The samples from the most recently completed frame, keyed by sample name.</summary>
    public static IReadOnlyDictionary<string, float> Samples => s_last;

    /// <summary>Starts timing a named sample. Pair with <see cref="EndSample"/>.</summary>
    public static void BeginSample(string name) =>
        s_starts[name] = Stopwatch.GetTimestamp();

    /// <summary>Stops timing a named sample and accumulates its duration into the current frame.</summary>
    public static void EndSample(string name)
    {
        if (!s_starts.Remove(name, out long start))
        {
            return;
        }

        float ms = (Stopwatch.GetTimestamp() - start) * 1000f / Stopwatch.Frequency;
        s_current.TryGetValue(name, out float prev);
        s_current[name] = prev + ms;
    }

    /// <summary>
    /// Snapshots the current frame's accumulated timings into <see cref="Samples"/> and resets
    /// the accumulator for the next frame. Called once per frame by the application loop.
    /// </summary>
    public static void FlushFrame()
    {
        foreach (KeyValuePair<string, float> kv in s_current)
        {
            s_last[kv.Key] = kv.Value;
        }
        s_current.Clear();
        s_starts.Clear();
    }
}

using System.Numerics;

namespace Spot.Animation;

/// <summary>A single keyframe: a value paired with the time (in seconds) it takes effect.</summary>
/// <typeparam name="T">The keyed value type (a <see cref="Vector3"/> or <see cref="Quaternion"/>).</typeparam>
public readonly struct Keyframe<T>
{
    /// <summary>Initializes a keyframe at <paramref name="time"/> seconds holding <paramref name="value"/>.</summary>
    public Keyframe(float time, T value)
    {
        Time = time;
        Value = value;
    }

    /// <summary>Gets the time, in seconds from the start of the clip, this key applies at.</summary>
    public float Time { get; }

    /// <summary>Gets the keyed value.</summary>
    public T Value { get; }
}

/// <summary>
/// The animation of one node (bone): its position, rotation and scale keyframes over time. Channels target a
/// node by <see cref="NodeName"/>; the animation system resolves that name to the entity of the same name in
/// the instantiated model and writes the sampled local transform onto it — exactly how the source rig posed
/// the node.
/// </summary>
public sealed class AnimationChannel
{
    /// <summary>Initializes a channel targeting <paramref name="nodeName"/> with its keyframe tracks.</summary>
    public AnimationChannel(
        string nodeName,
        IReadOnlyList<Keyframe<Vector3>> positionKeys,
        IReadOnlyList<Keyframe<Quaternion>> rotationKeys,
        IReadOnlyList<Keyframe<Vector3>> scaleKeys)
    {
        NodeName = nodeName;
        PositionKeys = positionKeys;
        RotationKeys = rotationKeys;
        ScaleKeys = scaleKeys;
    }

    /// <summary>Gets the name of the node (bone) this channel animates.</summary>
    public string NodeName { get; }

    /// <summary>Gets the position keyframes, in time order.</summary>
    public IReadOnlyList<Keyframe<Vector3>> PositionKeys { get; }

    /// <summary>Gets the rotation keyframes, in time order.</summary>
    public IReadOnlyList<Keyframe<Quaternion>> RotationKeys { get; }

    /// <summary>Gets the scale keyframes, in time order.</summary>
    public IReadOnlyList<Keyframe<Vector3>> ScaleKeys { get; }

    /// <summary>
    /// Samples the channel's local position at <paramref name="time"/> seconds, linearly interpolating
    /// between the surrounding keys. Returns <see langword="false"/> when the channel has no position track,
    /// so the caller keeps the node's existing value.
    /// </summary>
    public bool TrySamplePosition(float time, out Vector3 position) => TrySampleVector(PositionKeys, time, out position);

    /// <summary>
    /// Samples the channel's local scale at <paramref name="time"/> seconds, linearly interpolating between
    /// the surrounding keys. Returns <see langword="false"/> when the channel has no scale track.
    /// </summary>
    public bool TrySampleScale(float time, out Vector3 scale) => TrySampleVector(ScaleKeys, time, out scale);

    /// <summary>
    /// Samples the channel's local rotation at <paramref name="time"/> seconds, spherically interpolating
    /// (slerp) between the surrounding keys. Returns <see langword="false"/> when the channel has no rotation
    /// track.
    /// </summary>
    public bool TrySampleRotation(float time, out Quaternion rotation)
    {
        IReadOnlyList<Keyframe<Quaternion>> keys = RotationKeys;
        if (keys.Count == 0)
        {
            rotation = Quaternion.Identity;
            return false;
        }

        if (keys.Count == 1 || time <= keys[0].Time)
        {
            rotation = Quaternion.Normalize(keys[0].Value);
            return true;
        }

        if (time >= keys[^1].Time)
        {
            rotation = Quaternion.Normalize(keys[^1].Value);
            return true;
        }

        int next = FindNextIndex(keys, time, static k => k.Time);
        Keyframe<Quaternion> a = keys[next - 1];
        Keyframe<Quaternion> b = keys[next];
        float t = InverseLerp(a.Time, b.Time, time);
        rotation = Quaternion.Normalize(Quaternion.Slerp(a.Value, b.Value, t));
        return true;
    }

    private static bool TrySampleVector(IReadOnlyList<Keyframe<Vector3>> keys, float time, out Vector3 value)
    {
        if (keys.Count == 0)
        {
            value = Vector3.Zero;
            return false;
        }

        if (keys.Count == 1 || time <= keys[0].Time)
        {
            value = keys[0].Value;
            return true;
        }

        if (time >= keys[^1].Time)
        {
            value = keys[^1].Value;
            return true;
        }

        int next = FindNextIndex(keys, time, static k => k.Time);
        Keyframe<Vector3> a = keys[next - 1];
        Keyframe<Vector3> b = keys[next];
        float t = InverseLerp(a.Time, b.Time, time);
        value = Vector3.Lerp(a.Value, b.Value, t);
        return true;
    }

    // Returns the index of the first key whose time is strictly greater than the sample time; the caller has
    // already handled the before-first and after-last cases, so this always lands on an interior segment.
    private static int FindNextIndex<T>(IReadOnlyList<Keyframe<T>> keys, float time, Func<Keyframe<T>, float> timeOf)
    {
        for (int i = 1; i < keys.Count; i++)
        {
            if (timeOf(keys[i]) > time)
            {
                return i;
            }
        }

        return keys.Count - 1;
    }

    private static float InverseLerp(float a, float b, float value)
    {
        float span = b - a;
        return span > 1e-6f ? (value - a) / span : 0.0f;
    }
}

/// <summary>
/// A named animation: a set of per-node <see cref="AnimationChannel"/> tracks with a fixed duration. Clips
/// come baked into a model file (FBX/glTF) or from a separate animation-only file; either way the
/// <see cref="AnimatorComponent"/> plays them by name and the <see cref="AnimationSystem"/> applies each
/// channel to the bone entity of the matching name.
/// </summary>
public sealed class AnimationClip
{
    /// <summary>Initializes a clip with its name, duration and channels.</summary>
    /// <param name="name">The clip name, used to play it (for example "Run").</param>
    /// <param name="duration">The clip length in seconds; clamped to a tiny positive value.</param>
    /// <param name="channels">The per-node animation tracks.</param>
    public AnimationClip(string name, float duration, IReadOnlyList<AnimationChannel> channels)
    {
        Name = name;
        Duration = MathF.Max(duration, 1e-4f);
        Channels = channels;
    }

    /// <summary>Gets the clip name.</summary>
    public string Name { get; }

    /// <summary>Gets the clip length in seconds.</summary>
    public float Duration { get; }

    /// <summary>Gets the per-node animation channels.</summary>
    public IReadOnlyList<AnimationChannel> Channels { get; }

    /// <summary>
    /// Maps an elapsed play time onto the clip's timeline: wrapping into <c>[0, Duration]</c> when
    /// <paramref name="loop"/> is set, otherwise clamping to the end so a one-shot clip holds its final pose.
    /// </summary>
    /// <param name="time">Elapsed time in seconds since the clip started.</param>
    /// <param name="loop">Whether the clip repeats.</param>
    /// <returns>The local sample time within <c>[0, Duration]</c>.</returns>
    public float WrapTime(float time, bool loop)
    {
        if (loop)
        {
            float wrapped = time % Duration;
            return wrapped < 0.0f ? wrapped + Duration : wrapped;
        }

        return Math.Clamp(time, 0.0f, Duration);
    }
}

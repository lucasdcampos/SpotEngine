using System;
using System.Collections.Generic;
using System.Numerics;
using Spot.Animation;
using Spot.Assets;
using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// Plays skeletal animations on an imported model. It lives on the model's root entity and, each frame in
/// play mode, samples the current clip and writes the pose onto the bone entities (matched by name) — the
/// same tree the model was instantiated as. Clips come from the model's own file and from any
/// <see cref="ExtraClipPaths"/> (separate animation files, matched to the skeleton by bone name). Play from
/// code with <see cref="Play(string, bool?)"/> or set a <see cref="DefaultClip"/> with
/// <see cref="PlayOnStart"/>.
/// </summary>
[ComponentMenu("Animator", Order = 22)]
[SceneComponent("Animator")]
public sealed class AnimatorComponent : Component
{
    /// <summary>Gets or sets the model whose baked clips (and skeleton) drive this animator.</summary>
    [AssetReference(nameof(ModelPath))]
    public Model? Model { get; set; }

    /// <summary>Gets or sets the path to the model file, used for serialization.</summary>
    [HideInInspector]
    public string? ModelPath { get; set; }

    /// <summary>Gets or sets the clip played automatically when <see cref="PlayOnStart"/> is set.</summary>
    public string? DefaultClip { get; set; }

    /// <summary>Gets or sets whether the <see cref="DefaultClip"/> plays as soon as the scene runs.</summary>
    public bool PlayOnStart { get; set; } = true;

    /// <summary>Gets or sets the playback speed multiplier (1 = normal, 2 = twice as fast).</summary>
    public float Speed { get; set; } = 1.0f;

    /// <summary>Gets or sets whether playback loops (the default) or holds the final pose once.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>
    /// Gets or sets extra animation files whose clips are added to this animator, keyed by clip name. Each
    /// path points at a model/animation file (for example a Mixamo FBX); its clips drive this model's
    /// skeleton as long as the bone names match. Paths are project-relative.
    /// </summary>
    public string[] ExtraClipPaths { get; set; } = Array.Empty<string>();

    // ----- Runtime state (not serialized: dictionaries/arrays and private fields are skipped) -----
    private readonly Dictionary<string, AnimationClip> _clips = new(StringComparer.Ordinal);
    private Dictionary<string, Entity>? _nodesByName;
    private bool _clipsResolved;
    private bool _started;
    private float _time;

    /// <summary>Gets whether a clip is currently playing.</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>Gets the name of the clip currently playing (or paused), if any.</summary>
    public string? CurrentClip { get; private set; }

    /// <summary>Gets the names of every clip available to this animator (model clips plus extra files).</summary>
    public IEnumerable<string> ClipNames => _clips.Keys;

    /// <summary>Starts playing a clip by name from its beginning.</summary>
    /// <param name="clip">The clip name (see <see cref="ClipNames"/>).</param>
    /// <param name="loop">Optional override for <see cref="Loop"/>; keeps the current value when omitted.</param>
    public void Play(string clip, bool? loop = null)
    {
        CurrentClip = clip;
        _time = 0.0f;
        IsPlaying = true;
        if (loop.HasValue)
        {
            Loop = loop.Value;
        }
    }

    /// <summary>Stops playback and rewinds to the start of the current clip.</summary>
    public void Stop()
    {
        IsPlaying = false;
        _time = 0.0f;
    }

    /// <summary>Pauses playback, holding the current pose.</summary>
    public void Pause() => IsPlaying = false;

    /// <summary>Resumes playback of the current clip after a <see cref="Pause"/>.</summary>
    public void Resume()
    {
        if (CurrentClip != null)
        {
            IsPlaying = true;
        }
    }

    // Advances and applies the current clip, resolving clips and bone entities lazily. Called each frame by
    // AnimationSystem inside its per-animator try/catch, so this may throw without taking the engine down.
    internal void Tick(Entity self, float deltaTime)
    {
        EnsureClips();

        if (!_started)
        {
            _started = true;
            if (PlayOnStart && !string.IsNullOrEmpty(DefaultClip))
            {
                Play(DefaultClip);
            }
        }

        if (!IsPlaying || CurrentClip is null || !_clips.TryGetValue(CurrentClip, out AnimationClip? clip))
        {
            return;
        }

        _time += deltaTime * Speed;
        float local = clip.WrapTime(_time, Loop);

        _nodesByName ??= AnimationSystem.MapDescendantsByName(self);
        foreach (AnimationChannel channel in clip.Channels)
        {
            if (!_nodesByName.TryGetValue(AnimationSystem.NormalizeBoneName(channel.NodeName), out Entity node) ||
                !node.TryGetComponent(out Rendering.TransformComponent? transform))
            {
                continue;
            }

            if (channel.TrySamplePosition(local, out Vector3 position))
            {
                transform.Position = position;
            }

            if (channel.TrySampleRotation(local, out Quaternion rotation))
            {
                transform.Rotation = AnimationMath.ToEulerDegrees(rotation);
            }

            if (channel.TrySampleScale(local, out Vector3 scale))
            {
                transform.Scale = scale;
            }
        }

        // A finished one-shot clip holds its last pose.
        if (!Loop && _time >= clip.Duration)
        {
            IsPlaying = false;
        }
    }

    // Builds the clip set from the base model and any extra clip files, once the base model is ready. Retries
    // on later frames until the (async) base model resolves.
    private void EnsureClips()
    {
        if (_clipsResolved)
        {
            return;
        }

        if (Model is null && !string.IsNullOrEmpty(ModelPath))
        {
            Model = ModelImporter.RequestAsync(ModelPath);
        }

        // Wait for the base model to load before building the clip set (so its clips are included).
        if (Model is null && !string.IsNullOrEmpty(ModelPath))
        {
            return;
        }

        if (Model is not null)
        {
            foreach (AnimationClip clip in Model.Animations)
            {
                _clips[clip.Name] = clip;
            }
        }

        foreach (string path in ExtraClipPaths)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            try
            {
                Assets.Model extra = Assets.Model.Load(path);
                foreach (AnimationClip clip in extra.Animations)
                {
                    _clips[clip.Name] = clip;
                }
            }
            catch (Exception ex)
            {
                Log.CoreError("Animator failed to load clip file '{0}': {1}", path, ex.Message);
            }
        }

        _clipsResolved = true;
    }
}

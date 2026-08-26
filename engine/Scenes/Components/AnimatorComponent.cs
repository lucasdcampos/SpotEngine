using System;
using System.Collections.Generic;
using System.Numerics;
using Spot.Animation;
using Spot.Assets;
using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// Plays skeletal animation on an imported model by posing its bone entities (the tree the model was
/// instantiated as, matched by name). The component itself is deliberately small: it either references an
/// <see cref="AnimatorController"/> that drives which clip plays from parameters and transitions, or it is
/// driven entirely from a script that owns the clips and calls <see cref="Play(string, bool)"/> /
/// <see cref="Play(AnimationClip, bool)"/>. With neither, the model simply rests in its bind pose.
/// </summary>
/// <remarks>
/// Clip data comes from the rigged model this animator was instantiated from (its baked clips) plus the file
/// each controller state names as the source of its clip (<see cref="AnimatorState.ClipSource"/>). Clips are
/// resolved by name, so a script plays one with <c>animator.Play("Run")</c> or by handing over an
/// <see cref="AnimationClip"/> it loaded itself.
/// </remarks>
[ComponentMenu("Animator", Order = 22)]
[SceneComponent("Animator")]
public sealed class AnimatorComponent : Component
{
    /// <summary>
    /// Gets or sets an optional controller that drives which clip plays from parameters and transitions. When
    /// set, the state machine has authority (drive it with <see cref="SetFloat"/> and friends); when null, the
    /// animator is driven from code with <see cref="Play(string, bool)"/>.
    /// </summary>
    [AssetReference(nameof(ControllerPath))]
    public AnimatorController? Controller { get; set; }

    /// <summary>Gets or sets the path to the controller asset, used for serialization.</summary>
    [HideInInspector]
    public string? ControllerPath { get; set; }

    /// <summary>
    /// Gets or sets the rigged model this animator was instantiated from — the source of its baked clips and
    /// skeleton. Set automatically on import; hidden from the inspector but serialized so scenes reload
    /// correctly. Clips are still played by name, so this is not authoring surface.
    /// </summary>
    [HideInInspector]
    [SerializeHidden]
    public string? ModelPath { get; set; }

    // ----- Runtime state (not serialized: dictionaries and private fields are skipped) -----
    private readonly Dictionary<string, AnimationClip> _clips = new(StringComparer.Ordinal);
    private Model? _model;
    private Dictionary<string, Entity>? _nodesByName;
    private AnimatorControllerRuntime? _runtime;
    private bool _clipsResolved;
    private bool _controllerResolved;

    // Manual (code-driven) playback state, used when no controller is assigned.
    private AnimationClip? _current;
    private bool _loop = true;
    private float _time;

    /// <summary>Gets whether a clip is currently playing (either code-driven or via a controller state).</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>Gets the name of the clip currently playing — the controller's active clip, or the code-driven one.</summary>
    public string? CurrentClip => _runtime?.CurrentClip ?? _current?.Name;

    /// <summary>Gets the name of the state the controller is in, or null when no controller drives this animator.</summary>
    public string? CurrentState => _runtime?.CurrentState;

    /// <summary>Gets the names of every clip available to this animator (its model's clips plus controller sources).</summary>
    public IEnumerable<string> ClipNames
    {
        get
        {
            EnsureClips();
            return _clips.Keys;
        }
    }

    /// <summary>
    /// Plays a clip by name, resolved from the animator's available clips (<see cref="ClipNames"/>). Logs and
    /// does nothing when no clip of that name exists. Ignored while a controller drives this animator.
    /// </summary>
    /// <param name="clipName">The clip name to play.</param>
    /// <param name="loop">Whether the clip loops (the default) or holds its final pose once.</param>
    public void Play(string clipName, bool loop = true)
    {
        EnsureClips();
        if (_clips.TryGetValue(clipName, out AnimationClip? clip))
        {
            Play(clip, loop);
        }
        else
        {
            Log.CoreWarn("Animator has no clip named '{0}'.", clipName);
        }
    }

    /// <summary>
    /// Plays a clip the caller supplies directly — the code path where a script owns its clips (for example one
    /// obtained from a loaded model). Ignored while a controller drives this animator.
    /// </summary>
    /// <param name="clip">The clip to play.</param>
    /// <param name="loop">Whether the clip loops (the default) or holds its final pose once.</param>
    public void Play(AnimationClip clip, bool loop = true)
    {
        _current = clip;
        _loop = loop;
        _time = 0.0f;
        IsPlaying = true;
    }

    /// <summary>Stops code-driven playback and rewinds to the start of the current clip.</summary>
    public void Stop()
    {
        IsPlaying = false;
        _time = 0.0f;
    }

    /// <summary>Pauses code-driven playback, holding the current pose.</summary>
    public void Pause() => IsPlaying = false;

    /// <summary>Resumes code-driven playback of the current clip after a <see cref="Pause"/>.</summary>
    public void Resume()
    {
        if (_current != null)
        {
            IsPlaying = true;
        }
    }

    /// <summary>Sets a controller float parameter. No-op when this animator has no controller.</summary>
    public void SetFloat(string name, float value) { EnsureController(); _runtime?.SetFloat(name, value); }

    /// <summary>Gets a controller float parameter, or <c>0</c> when there is no controller or parameter.</summary>
    public float GetFloat(string name) { EnsureController(); return _runtime?.GetFloat(name) ?? 0.0f; }

    /// <summary>Sets a controller int parameter. No-op when this animator has no controller.</summary>
    public void SetInt(string name, int value) { EnsureController(); _runtime?.SetInt(name, value); }

    /// <summary>Gets a controller int parameter, or <c>0</c> when there is no controller or parameter.</summary>
    public int GetInt(string name) { EnsureController(); return _runtime?.GetInt(name) ?? 0; }

    /// <summary>Sets a controller bool parameter. No-op when this animator has no controller.</summary>
    public void SetBool(string name, bool value) { EnsureController(); _runtime?.SetBool(name, value); }

    /// <summary>Gets a controller bool parameter, or <see langword="false"/> when there is no controller or parameter.</summary>
    public bool GetBool(string name) { EnsureController(); return _runtime?.GetBool(name) ?? false; }

    /// <summary>Raises a controller trigger, consumed by the next transition that tests it. No-op without a controller.</summary>
    public void SetTrigger(string name) { EnsureController(); _runtime?.SetTrigger(name); }

    /// <summary>Clears a controller trigger without taking a transition. No-op without a controller.</summary>
    public void ResetTrigger(string name) { EnsureController(); _runtime?.ResetTrigger(name); }

    // Advances and applies the current clip, resolving clips and bone entities lazily. Called each frame by
    // AnimationSystem inside its per-animator try/catch, so this may throw without taking the engine down.
    internal void Tick(Entity self, float deltaTime)
    {
        EnsureClips();
        EnsureController();

        // Controller path: the state machine decides which clip plays; we just apply its pose.
        if (_runtime is not null)
        {
            _runtime.Tick(deltaTime, _clips);
            if (_runtime.CurrentClip is string stateClip && _clips.TryGetValue(stateClip, out AnimationClip? active))
            {
                ApplyClip(self, active, _runtime.LocalTime, _runtime.Loop);
            }

            return;
        }

        // Code path: a single clip started via Play.
        if (!IsPlaying || _current is null)
        {
            return;
        }

        _time += deltaTime;
        ApplyClip(self, _current, _time, _loop);

        // A finished one-shot clip holds its last pose.
        if (!_loop && _time >= _current.Duration)
        {
            IsPlaying = false;
        }
    }

    // Samples a clip at an elapsed time and writes the sampled local transform onto each matching bone entity.
    // Shared by the controller and code playback paths; wrapping (loop vs hold) happens here.
    private void ApplyClip(Entity self, AnimationClip clip, float elapsed, bool loop)
    {
        float local = clip.WrapTime(elapsed, loop);

        _nodesByName ??= AnimationSystem.MapDescendantsByName(self);
        foreach (AnimationChannel channel in clip.Channels)
        {
            if (!_nodesByName.TryGetValue(AnimationSystem.NormalizeBoneName(channel.NodeName), out Entity node) ||
                !node.TryGetComponent(out TransformComponent? transform))
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
    }

    // Resolves the controller asset (lazily) and builds its runtime once. Cheap after the first resolve; safe
    // to call from the parameter setters so scripts can drive parameters before the first tick.
    private void EnsureController()
    {
        if (_controllerResolved)
        {
            return;
        }

        if (Controller is null && !string.IsNullOrEmpty(ControllerPath))
        {
            Controller = AnimatorController.Load(ControllerPath);
        }

        if (Controller is not null)
        {
            _runtime = new AnimatorControllerRuntime(Controller);
        }

        _controllerResolved = true;
    }

    // Builds the clip set from the animator's model plus any clip sources declared on the controller, once the
    // (async) base model resolves. Retries on later frames until then.
    private void EnsureClips()
    {
        if (_clipsResolved)
        {
            return;
        }

        if (_model is null && !string.IsNullOrEmpty(ModelPath))
        {
            _model = ModelImporter.RequestAsync(ModelPath);

            // Wait for the base model to load before building the clip set (so its clips are included).
            if (_model is null)
            {
                return;
            }
        }

        if (_model is not null)
        {
            foreach (AnimationClip clip in _model.Animations)
            {
                _clips[clip.Name] = clip;
            }
        }

        // Each controller state references the file that provides its clip, so the data behind the state's
        // clip name loads directly — no separate source list. Files are loaded once (Model.Load is cached).
        if (Controller is null && !string.IsNullOrEmpty(ControllerPath))
        {
            Controller = AnimatorController.Load(ControllerPath);
        }

        if (Controller is not null)
        {
            foreach (AnimatorState state in Controller.States)
            {
                if (string.IsNullOrEmpty(state.ClipSource))
                {
                    continue;
                }

                try
                {
                    foreach (AnimationClip clip in Model.Load(state.ClipSource).Animations)
                    {
                        _clips[clip.Name] = clip;
                    }
                }
                catch (Exception ex)
                {
                    Log.CoreError("Animator state '{0}' failed to load clip source '{1}': {2}", state.Name, state.ClipSource, ex.Message);
                }
            }
        }

        _clipsResolved = true;
    }
}

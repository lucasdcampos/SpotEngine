using System.Collections.Generic;

namespace Spot.Animation;

/// <summary>
/// The per-instance evaluator for an <see cref="AnimatorController"/>: it holds the live parameter values and
/// the current state, advances the active clip's time, and each tick picks the first transition whose
/// conditions pass — switching state instantly (no crossfade yet). It is pure logic with no engine or GL
/// dependency (clips are supplied by the caller), so the state machine can be unit-tested on its own; the
/// <see cref="Spot.Scenes.AnimatorComponent"/> owns one and applies the resulting pose to the bones.
/// </summary>
public sealed class AnimatorControllerRuntime
{
    private readonly AnimatorController _controller;
    private readonly Dictionary<string, float> _values = new(StringComparer.Ordinal);

    /// <summary>Creates a runtime for <paramref name="controller"/>, seeding parameters and entering the default state.</summary>
    /// <param name="controller">The controller asset this runtime evaluates.</param>
    public AnimatorControllerRuntime(AnimatorController controller)
    {
        _controller = controller;
        foreach (AnimatorParameter parameter in controller.Parameters)
        {
            if (!string.IsNullOrEmpty(parameter.Name))
            {
                _values[parameter.Name] = parameter.DefaultValue;
            }
        }

        EnterState(controller.FindState(controller.DefaultState) ?? FirstState());
    }

    /// <summary>Gets the name of the state the machine is currently in, if any.</summary>
    public string? CurrentState { get; private set; }

    /// <summary>Gets the clip name the current state plays, if any.</summary>
    public string? CurrentClip { get; private set; }

    /// <summary>Gets the elapsed play time (seconds) within the current clip.</summary>
    public float LocalTime { get; private set; }

    /// <summary>Gets whether the current state's clip loops.</summary>
    public bool Loop { get; private set; } = true;

    /// <summary>Gets the current state's speed multiplier.</summary>
    public float StateSpeed { get; private set; } = 1.0f;

    /// <summary>Sets a float parameter.</summary>
    public void SetFloat(string name, float value) => _values[name] = value;

    /// <summary>Gets a float parameter, or <c>0</c> when it is unknown.</summary>
    public float GetFloat(string name) => _values.TryGetValue(name, out float v) ? v : 0.0f;

    /// <summary>Sets an int parameter.</summary>
    public void SetInt(string name, int value) => _values[name] = value;

    /// <summary>Gets an int parameter, or <c>0</c> when it is unknown.</summary>
    public int GetInt(string name) => _values.TryGetValue(name, out float v) ? (int)MathF.Round(v) : 0;

    /// <summary>Sets a bool parameter (stored as 0/1).</summary>
    public void SetBool(string name, bool value) => _values[name] = value ? 1.0f : 0.0f;

    /// <summary>Gets a bool parameter, or <see langword="false"/> when it is unknown.</summary>
    public bool GetBool(string name) => _values.TryGetValue(name, out float v) && v != 0.0f;

    /// <summary>Raises a trigger, which the next transition that consumes it will reset.</summary>
    public void SetTrigger(string name) => _values[name] = 1.0f;

    /// <summary>Clears a trigger without taking a transition.</summary>
    public void ResetTrigger(string name) => _values[name] = 0.0f;

    /// <summary>
    /// Advances the current clip and evaluates transitions. Any-state transitions are considered before the
    /// current state's own; the first whose conditions all pass (and whose exit time, if any, is reached) is
    /// taken, resetting time and consuming the triggers it used.
    /// </summary>
    /// <param name="deltaTime">Elapsed seconds since the previous tick.</param>
    /// <param name="clips">The clips available to the animator, keyed by name; used for durations.</param>
    public void Tick(float deltaTime, IReadOnlyDictionary<string, AnimationClip> clips)
    {
        // Advance the active clip so exit-time transitions can see how far it has played.
        float duration = ClipDuration(clips, CurrentClip);
        LocalTime += deltaTime * StateSpeed;

        // Evaluate any-state transitions first, then the ones leaving the current state.
        foreach (AnimatorTransition transition in _controller.Transitions)
        {
            if (!transition.FromAnyState)
            {
                continue;
            }

            if (TryTake(transition, duration))
            {
                return;
            }
        }

        foreach (AnimatorTransition transition in _controller.Transitions)
        {
            if (transition.FromAnyState || !string.Equals(transition.From, CurrentState, StringComparison.Ordinal))
            {
                continue;
            }

            if (TryTake(transition, duration))
            {
                return;
            }
        }
    }

    private bool TryTake(AnimatorTransition transition, float currentDuration)
    {
        if (transition.HasExitTime)
        {
            float normalized = currentDuration > 0.0f ? LocalTime / currentDuration : 0.0f;
            if (normalized < transition.ExitTime)
            {
                return false;
            }
        }

        foreach (AnimatorCondition condition in transition.Conditions)
        {
            if (!Evaluate(condition))
            {
                return false;
            }
        }

        AnimatorState? target = _controller.FindState(transition.To);
        if (target is null)
        {
            return false;
        }

        // Consume any triggers this transition tested, so a one-shot fires exactly once.
        foreach (AnimatorCondition condition in transition.Conditions)
        {
            if (_controller.FindParameter(condition.Parameter)?.Type == AnimatorParameterType.Trigger)
            {
                _values[condition.Parameter] = 0.0f;
            }
        }

        EnterState(target);
        return true;
    }

    private bool Evaluate(AnimatorCondition condition)
    {
        AnimatorParameter? parameter = _controller.FindParameter(condition.Parameter);
        float value = GetFloat(condition.Parameter);

        // A trigger passes purely on being set; its comparison mode/threshold are ignored.
        if (parameter?.Type == AnimatorParameterType.Trigger)
        {
            return value != 0.0f;
        }

        return condition.Mode switch
        {
            AnimatorConditionMode.Greater => value > condition.Threshold,
            AnimatorConditionMode.Less => value < condition.Threshold,
            AnimatorConditionMode.Equals => value == condition.Threshold,
            AnimatorConditionMode.NotEquals => value != condition.Threshold,
            _ => false,
        };
    }

    private void EnterState(AnimatorState? state)
    {
        CurrentState = state?.Name;
        CurrentClip = state?.Clip;
        Loop = state?.Loop ?? true;
        StateSpeed = state?.Speed ?? 1.0f;
        LocalTime = 0.0f;
    }

    private AnimatorState? FirstState() => _controller.States.Count > 0 ? _controller.States[0] : null;

    private static float ClipDuration(IReadOnlyDictionary<string, AnimationClip> clips, string? clipName) =>
        !string.IsNullOrEmpty(clipName) && clips.TryGetValue(clipName, out AnimationClip? clip) ? clip.Duration : 0.0f;
}

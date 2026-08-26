using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spot.Assets;
using Spot.Core;

namespace Spot.Animation;

/// <summary>The kind of a controller parameter, which decides how conditions compare against it.</summary>
public enum AnimatorParameterType
{
    /// <summary>A continuous value, compared with <see cref="AnimatorConditionMode.Greater"/>/<see cref="AnimatorConditionMode.Less"/>.</summary>
    Float = 0,

    /// <summary>A whole number, compared with any <see cref="AnimatorConditionMode"/>.</summary>
    Int = 1,

    /// <summary>A boolean, held as 0/1 and compared for equality.</summary>
    Bool = 2,

    /// <summary>A one-shot flag that a taken transition consumes (resets) automatically.</summary>
    Trigger = 3,
}

/// <summary>How a condition compares a parameter's value against its threshold.</summary>
public enum AnimatorConditionMode
{
    /// <summary>Passes when the parameter is greater than the threshold (Float/Int).</summary>
    Greater = 0,

    /// <summary>Passes when the parameter is less than the threshold (Float/Int).</summary>
    Less = 1,

    /// <summary>Passes when the parameter equals the threshold (Int/Bool).</summary>
    Equals = 2,

    /// <summary>Passes when the parameter does not equal the threshold (Int/Bool).</summary>
    NotEquals = 3,
}

/// <summary>A named tunable a state machine reads to decide transitions. Bool/Trigger values are held as 0/1.</summary>
public sealed class AnimatorParameter
{
    /// <summary>Gets or sets the parameter name scripts and conditions refer to.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the parameter type.</summary>
    public AnimatorParameterType Type { get; set; } = AnimatorParameterType.Float;

    /// <summary>Gets or sets the value a fresh runtime seeds this parameter with (bool/trigger use 0/1).</summary>
    public float DefaultValue { get; set; }
}

/// <summary>A state in the machine: one animation clip (its name plus the file that provides it).</summary>
public sealed class AnimatorState
{
    /// <summary>Gets or sets the state name, unique within the controller and used by transitions.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the clip name this state plays. Clips retarget by name across matching skeletons.</summary>
    public string Clip { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a portable reference to the model/animation file that provides <see cref="Clip"/>, so the
    /// runtime can load its data. Empty when the clip is baked into the animator's own model.
    /// </summary>
    public string? ClipSource { get; set; }

    /// <summary>Gets or sets the playback speed multiplier applied while in this state.</summary>
    public float Speed { get; set; } = 1.0f;

    /// <summary>Gets or sets whether the clip loops (the default) or holds its last pose.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>Gets or sets the node's X position in the editor graph (authoring only).</summary>
    public float EditorX { get; set; }

    /// <summary>Gets or sets the node's Y position in the editor graph (authoring only).</summary>
    public float EditorY { get; set; }
}

/// <summary>One clause of a transition: a parameter compared against a threshold.</summary>
public sealed class AnimatorCondition
{
    /// <summary>Gets or sets the parameter this condition tests.</summary>
    public string Parameter { get; set; } = string.Empty;

    /// <summary>Gets or sets the comparison mode (ignored for <see cref="AnimatorParameterType.Trigger"/>).</summary>
    public AnimatorConditionMode Mode { get; set; } = AnimatorConditionMode.Greater;

    /// <summary>Gets or sets the value the parameter is compared against.</summary>
    public float Threshold { get; set; }
}

/// <summary>
/// A directed edge between two states. It is taken when every <see cref="Conditions"/> clause passes and,
/// when <see cref="HasExitTime"/> is set, the source clip has played past <see cref="ExitTime"/> (normalized).
/// </summary>
public sealed class AnimatorTransition
{
    /// <summary>Gets or sets the source state name (ignored when <see cref="FromAnyState"/> is set).</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Gets or sets whether this transition may fire from any state (evaluated before per-state ones).</summary>
    public bool FromAnyState { get; set; }

    /// <summary>Gets or sets the destination state name.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Gets or sets whether the transition waits for <see cref="ExitTime"/> before it can fire.</summary>
    public bool HasExitTime { get; set; }

    /// <summary>Gets or sets the normalized time (0..1 of the clip) after which an exit-time transition may fire.</summary>
    public float ExitTime { get; set; } = 1.0f;

    /// <summary>Gets or sets the condition clauses; all must pass for the transition to be taken.</summary>
    public List<AnimatorCondition> Conditions { get; set; } = new();
}

/// <summary>
/// A reusable animation state machine asset: parameters, states (each a clip by name), and the transitions
/// between them. Assign one to an <see cref="Spot.Scenes.AnimatorComponent"/> to drive which clip plays from
/// parameters instead of calling <c>Play</c> by hand; because states reference clips by name, one controller
/// drives any model whose clips share those names. Stored on disk as ".sptcontroller" JSON, this class mirrors
/// <see cref="Spot.Assets.Material"/>: <see cref="Load"/> caches by path so edits show up everywhere live.
/// </summary>
public sealed class AnimatorController
{
    private static readonly Dictionary<string, AnimatorController> s_cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Gets or sets the controller's parameters, seeded into each runtime as its variables.</summary>
    public List<AnimatorParameter> Parameters { get; set; } = new();

    /// <summary>Gets or sets the machine's states.</summary>
    public List<AnimatorState> States { get; set; } = new();

    /// <summary>Gets or sets the transitions between states.</summary>
    public List<AnimatorTransition> Transitions { get; set; } = new();

    /// <summary>Gets or sets the name of the state entered when the machine starts.</summary>
    public string DefaultState { get; set; } = string.Empty;

    /// <summary>Gets the file this controller was loaded from or last saved to, if any.</summary>
    [JsonIgnore]
    public string? SourcePath { get; internal set; }

    /// <summary>Finds a state by name, or <see langword="null"/> when none matches.</summary>
    /// <param name="name">The state name to look up.</param>
    public AnimatorState? FindState(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (AnimatorState state in States)
        {
            if (string.Equals(state.Name, name, StringComparison.Ordinal))
            {
                return state;
            }
        }

        return null;
    }

    /// <summary>Finds a parameter by name, or <see langword="null"/> when none matches.</summary>
    /// <param name="name">The parameter name to look up.</param>
    public AnimatorParameter? FindParameter(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (AnimatorParameter parameter in Parameters)
        {
            if (string.Equals(parameter.Name, name, StringComparison.Ordinal))
            {
                return parameter;
            }
        }

        return null;
    }

    /// <summary>
    /// Loads a controller from a ".sptcontroller" file, caching by full path so the same file yields the same
    /// instance. A guid reference resolves through the content manifest; anything else is a project path. A load
    /// failure logs and returns an empty controller rather than throwing.
    /// </summary>
    /// <param name="path">The controller file path or <c>guid:</c> reference.</param>
    /// <returns>The loaded (or cached) controller.</returns>
    public static AnimatorController Load(string path)
    {
        if (s_cache.TryGetValue(path, out AnimatorController? refCached))
        {
            return refCached;
        }

        string full;
        if (AssetRef.IsGuidRef(path))
        {
            string? cooked = AssetPath.ResolveContent(path);
            if (cooked is null)
            {
                Log.CoreError("Unresolved animator controller reference '{0}'; using an empty controller.", path);
                var missing = new AnimatorController { SourcePath = path };
                s_cache[path] = missing;
                return missing;
            }

            full = Path.GetFullPath(cooked);
        }
        else
        {
            full = Path.GetFullPath(AssetPath.Resolve(path));
        }

        if (s_cache.TryGetValue(full, out AnimatorController? cached))
        {
            return cached;
        }

        var controller = new AnimatorController { SourcePath = full };
        try
        {
            AnimatorController? data = JsonSerializer.Deserialize<AnimatorController>(
                AssetProvider.Current.ReadAllText(full), s_options);
            if (data != null)
            {
                controller.Parameters = data.Parameters;
                controller.States = data.States;
                controller.Transitions = data.Transitions;
                controller.DefaultState = data.DefaultState;
            }
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to load animator controller '{0}': {1}", full, ex.Message);
        }

        s_cache[full] = controller;
        s_cache[path] = controller;
        return controller;
    }

    /// <summary>
    /// Writes this controller to a ".sptcontroller" file, remembers the path as its source, and caches it so
    /// subsequent <see cref="Load"/> calls for that path return this instance.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    public void Save(string path)
    {
        string full = Path.GetFullPath(path);
        File.WriteAllText(full, JsonSerializer.Serialize(this, s_options));
        SourcePath = full;
        s_cache[full] = this;
    }
}

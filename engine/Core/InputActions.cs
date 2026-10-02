namespace Spot.Core;

/// <summary>
/// Named input actions: game-level names ("jump", "forward") mapped to the physical inputs that trigger them, so
/// code asks about intent instead of keys and players can rebind. Built on the raw <see cref="Input"/> state —
/// every query reports nothing while input is blocked — and exposed on <see cref="Input"/> itself
/// (<c>Input.GetAction</c>, <c>Input.Bind</c>, ...) wherever the framework is referenced.
/// </summary>
public static class InputActions
{
    // Named actions mapped to the physical inputs that trigger them (an action can have several, e.g.
    // "forward" -> W and Up). Names are compared case-insensitively so console usage is forgiving.
    // s_defaults holds the startup bindings so a runtime "reset bindings" can restore them.
    private static readonly Dictionary<string, HashSet<InputBinding>> s_actions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<InputBinding>> s_defaults = new(StringComparer.OrdinalIgnoreCase);

    // Actions driven directly through SetActionState (virtual buttons, custom devices), with their own
    // per-frame edges.
    private static readonly HashSet<string> s_activeCustom = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_customPressed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_customReleased = new(StringComparer.OrdinalIgnoreCase);

    // Gamepad axes count as "held" past this deflection.
    private const float AxisThreshold = 0.5f;

    static InputActions()
    {
        // Keep the custom-action edges in step with Input's frames, and drop everything when Input resets.
        Input.FrameStarted += () =>
        {
            s_customPressed.Clear();
            s_customReleased.Clear();
        };
        Input.Cleared += () =>
        {
            s_activeCustom.Clear();
            s_customPressed.Clear();
            s_customReleased.Clear();
            s_actions.Clear();
            s_defaults.Clear();
        };
    }

    /// <summary>
    /// Returns whether any input bound to the named action is currently held down.
    /// </summary>
    /// <param name="action">The action name (case-insensitive), e.g. "forward".</param>
    /// <returns><see langword="true"/> while any bound key/button is down.</returns>
    public static bool GetAction(string action)
    {
        if (Input.IsBlocked)
        {
            return false;
        }

        if (s_activeCustom.Contains(action))
        {
            return true;
        }

        if (!s_actions.TryGetValue(action, out HashSet<InputBinding>? bindings))
        {
            return false;
        }

        foreach (InputBinding binding in bindings)
        {
            if (IsHeld(binding))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether the named action became active during this frame.
    /// </summary>
    /// <remarks>
    /// Fires only on the inactive-to-active transition: pressing a second bound key while the action is
    /// already held does not re-fire it.
    /// </remarks>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <returns><see langword="true"/> on the frame the action goes active.</returns>
    public static bool GetActionDown(string action)
    {
        if (Input.IsBlocked)
        {
            return false;
        }

        if (s_customPressed.Contains(action))
        {
            return true;
        }

        if (!s_actions.TryGetValue(action, out HashSet<InputBinding>? bindings))
        {
            return false;
        }

        bool pressedThisFrame = false;
        bool heldBefore = false;
        foreach (InputBinding binding in bindings)
        {
            bool pressed = IsPressed(binding);
            pressedThisFrame |= pressed;

            // Held coming into this frame (down now but not from this frame's press) means the action
            // was already active, so this isn't a fresh activation.
            heldBefore |= IsHeld(binding) && !pressed;
        }

        return pressedThisFrame && !heldBefore;
    }

    /// <summary>
    /// Returns whether the named action became inactive during this frame.
    /// </summary>
    /// <remarks>
    /// Fires only on the active-to-inactive transition: releasing one bound key while another is still
    /// held keeps the action active and does not fire.
    /// </remarks>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <returns><see langword="true"/> on the frame the action goes inactive.</returns>
    public static bool GetActionUp(string action)
    {
        if (Input.IsBlocked)
        {
            return false;
        }

        if (s_customReleased.Contains(action))
        {
            return true;
        }

        if (!s_actions.TryGetValue(action, out HashSet<InputBinding>? bindings))
        {
            return false;
        }

        bool releasedThisFrame = false;
        bool stillHeld = false;
        foreach (InputBinding binding in bindings)
        {
            releasedThisFrame |= IsReleased(binding);
            stillHeld |= IsHeld(binding);
        }

        return releasedThisFrame && !stillHeld;
    }

    /// <summary>
    /// Explicitly sets the state of an action. This allows developers to trigger actions via custom input devices
    /// or virtual UI buttons without needing to emulate a physical key press.
    /// </summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <param name="isActive">Whether the action should be considered active.</param>
    public static void SetActionState(string action, bool isActive)
    {
        if (isActive)
        {
            if (s_activeCustom.Add(action))
            {
                s_customPressed.Add(action);
            }
        }
        else
        {
            if (s_activeCustom.Remove(action))
            {
                s_customReleased.Add(action);
            }
        }
    }

    /// <summary>
    /// Binds a physical input to an action. An action may have several bindings; binding one that is
    /// already present is a no-op.
    /// </summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <param name="binding">The key or mouse button to bind.</param>
    public static void Bind(string action, InputBinding binding)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        if (!s_actions.TryGetValue(action, out HashSet<InputBinding>? bindings))
        {
            bindings = new HashSet<InputBinding>();
            s_actions[action] = bindings;
        }

        bindings.Add(binding);
    }

    /// <summary>Binds a keyboard key to an action.</summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <param name="key">The key to bind.</param>
    public static void Bind(string action, Key key) => Bind(action, InputBinding.Key(key));

    /// <summary>Binds a mouse button to an action.</summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <param name="button">The button to bind.</param>
    public static void Bind(string action, MouseButton button) => Bind(action, InputBinding.Mouse(button));

    /// <summary>
    /// Removes a physical input from every action it is bound to.
    /// </summary>
    /// <param name="binding">The key or mouse button to unbind.</param>
    /// <returns><see langword="true"/> if the binding was removed from at least one action.</returns>
    public static bool Unbind(InputBinding binding)
    {
        bool removed = false;
        foreach (string action in s_actions.Keys.ToList())
        {
            HashSet<InputBinding> bindings = s_actions[action];
            if (bindings.Remove(binding))
            {
                removed = true;
                if (bindings.Count == 0)
                {
                    s_actions.Remove(action);
                }
            }
        }

        return removed;
    }

    /// <summary>
    /// Removes an action and all of its bindings.
    /// </summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <returns><see langword="true"/> if the action existed.</returns>
    public static bool UnbindAction(string action) => s_actions.Remove(action);

    /// <summary>
    /// Gets the bindings currently mapped to an action.
    /// </summary>
    /// <param name="action">The action name (case-insensitive).</param>
    /// <returns>A snapshot of the action's bindings, or an empty list if it has none.</returns>
    public static IReadOnlyList<InputBinding> GetBindings(string action)
        => s_actions.TryGetValue(action, out HashSet<InputBinding>? bindings) ? bindings.ToArray() : Array.Empty<InputBinding>();

    /// <summary>
    /// Gets the names of all actions that currently have at least one binding.
    /// </summary>
    /// <returns>A snapshot of the action names.</returns>
    public static IReadOnlyList<string> GetActionNames() => s_actions.Keys.ToArray();

    /// <summary>Removes every action binding.</summary>
    public static void ClearBindings() => s_actions.Clear();

    /// <summary>
    /// Replaces the default bindings and applies them, discarding any current bindings (the engine calls it
    /// at startup with the project's bindings); the snapshot is what <see cref="ResetBindingsToDefaults"/>
    /// restores.
    /// </summary>
    /// <param name="defaults">Actions mapped to their default bindings. May be <see langword="null"/>.</param>
    public static void SetDefaultBindings(IReadOnlyDictionary<string, InputBinding[]>? defaults)
    {
        s_defaults.Clear();
        if (defaults != null)
        {
            foreach ((string action, InputBinding[] bindings) in defaults)
            {
                if (string.IsNullOrWhiteSpace(action) || bindings == null)
                {
                    continue;
                }

                s_defaults[action] = new HashSet<InputBinding>(bindings);
            }
        }

        ResetBindingsToDefaults();
    }

    /// <summary>
    /// Discards current bindings and restores the defaults set by <see cref="SetDefaultBindings"/>.
    /// </summary>
    public static void ResetBindingsToDefaults()
    {
        s_actions.Clear();
        foreach ((string action, HashSet<InputBinding> bindings) in s_defaults)
        {
            s_actions[action] = new HashSet<InputBinding>(bindings);
        }
    }

    // Whether a single binding is held / went down / went up this frame, read from the raw Input state.
    private static bool IsHeld(InputBinding binding) => binding.Device switch
    {
        InputDeviceKind.Keyboard => Input.GetKey((Key)binding.Code),
        InputDeviceKind.Mouse => Input.GetMouseButton((MouseButton)binding.Code),
        InputDeviceKind.GamepadButton => Input.GetGamepadButton((GamepadButton)binding.Code),
        InputDeviceKind.GamepadAxis => MathF.Abs(Input.GetGamepadAxis((GamepadAxis)binding.Code)) >= AxisThreshold,
        _ => false,
    };

    private static bool IsPressed(InputBinding binding) => binding.Device switch
    {
        InputDeviceKind.Keyboard => Input.GetKeyDown((Key)binding.Code),
        InputDeviceKind.Mouse => Input.GetMouseButtonDown((MouseButton)binding.Code),
        InputDeviceKind.GamepadButton => Input.GetGamepadButtonDown((GamepadButton)binding.Code),
        InputDeviceKind.GamepadAxis =>
            MathF.Abs(Input.GetGamepadAxis((GamepadAxis)binding.Code)) >= AxisThreshold
            && MathF.Abs(Input.GetPreviousGamepadAxis((GamepadAxis)binding.Code)) < AxisThreshold,
        _ => false,
    };

    private static bool IsReleased(InputBinding binding) => binding.Device switch
    {
        InputDeviceKind.Keyboard => Input.GetKeyUp((Key)binding.Code),
        InputDeviceKind.Mouse => Input.GetMouseButtonUp((MouseButton)binding.Code),
        InputDeviceKind.GamepadButton => Input.GetGamepadButtonUp((GamepadButton)binding.Code),
        InputDeviceKind.GamepadAxis =>
            MathF.Abs(Input.GetGamepadAxis((GamepadAxis)binding.Code)) < AxisThreshold
            && MathF.Abs(Input.GetPreviousGamepadAxis((GamepadAxis)binding.Code)) >= AxisThreshold,
        _ => false,
    };
}

/// <summary>
/// Exposes <see cref="InputActions"/> on <see cref="Input"/>, so actions read like the rest of the input API
/// (<c>Input.GetAction("jump")</c>).
/// </summary>
public static class InputActionExtensions
{
    extension(Input)
    {
        /// <summary>
        /// Returns whether any input bound to the named action is currently held down.
        /// </summary>
        /// <param name="action">The action name (case-insensitive), e.g. "forward".</param>
        /// <returns><see langword="true"/> while any bound key/button is down.</returns>
        public static bool GetAction(string action) =>
            InputActions.GetAction(action);

        /// <summary>
        /// Returns whether the named action became active during this frame.
        /// </summary>
        /// <remarks>
        /// Fires only on the inactive-to-active transition: pressing a second bound key while the action is
        /// already held does not re-fire it.
        /// </remarks>
        /// <param name="action">The action name (case-insensitive).</param>
        /// <returns><see langword="true"/> on the frame the action goes active.</returns>
        public static bool GetActionDown(string action) =>
            InputActions.GetActionDown(action);

        /// <summary>
        /// Returns whether the named action became inactive during this frame.
        /// </summary>
        /// <remarks>
        /// Fires only on the active-to-inactive transition: releasing one bound key while another is still
        /// held keeps the action active and does not fire.
        /// </remarks>
        /// <param name="action">The action name (case-insensitive).</param>
        /// <returns><see langword="true"/> on the frame the action goes inactive.</returns>
        public static bool GetActionUp(string action) =>
            InputActions.GetActionUp(action);

        /// <summary>
        /// Explicitly sets the state of an action. This allows developers to trigger actions via custom input devices
        /// or virtual UI buttons without needing to emulate a physical key press.
        /// </summary>
        /// <param name="action">The action name (case-insensitive).</param>
        /// <param name="isActive">Whether the action should be considered active.</param>
        public static void SetActionState(string action, bool isActive) =>
            InputActions.SetActionState(action, isActive);

        /// <summary>
        /// Binds a physical input to an action. An action may have several bindings; binding one that is
        /// already present is a no-op.
        /// </summary>
        /// <param name="action">The action name (case-insensitive).</param>
        /// <param name="binding">The key or mouse button to bind.</param>
        public static void Bind(string action, InputBinding binding) =>
            InputActions.Bind(action, binding);

        /// <summary>Binds a keyboard key to an action.</summary>
        /// <param name="action">The action name (case-insensitive).</param>
        /// <param name="key">The key to bind.</param>
        public static void Bind(string action, Key key) =>
            InputActions.Bind(action, key);

        /// <summary>Binds a mouse button to an action.</summary>
        /// <param name="action">The action name (case-insensitive).</param>
        /// <param name="button">The button to bind.</param>
        public static void Bind(string action, MouseButton button) =>
            InputActions.Bind(action, button);

        /// <summary>
        /// Removes a physical input from every action it is bound to.
        /// </summary>
        /// <param name="binding">The key or mouse button to unbind.</param>
        /// <returns><see langword="true"/> if the binding was removed from at least one action.</returns>
        public static bool Unbind(InputBinding binding) =>
            InputActions.Unbind(binding);

        /// <summary>
        /// Removes an action and all of its bindings.
        /// </summary>
        /// <param name="action">The action name (case-insensitive).</param>
        /// <returns><see langword="true"/> if the action existed.</returns>
        public static bool UnbindAction(string action) =>
            InputActions.UnbindAction(action);

        /// <summary>
        /// Gets the bindings currently mapped to an action.
        /// </summary>
        /// <param name="action">The action name (case-insensitive).</param>
        /// <returns>A snapshot of the action's bindings, or an empty list if it has none.</returns>
        public static IReadOnlyList<InputBinding> GetBindings(string action) =>
            InputActions.GetBindings(action);

        /// <summary>
        /// Gets the names of all actions that currently have at least one binding.
        /// </summary>
        /// <returns>A snapshot of the action names.</returns>
        public static IReadOnlyList<string> GetActionNames() =>
            InputActions.GetActionNames();

        /// <summary>Removes every action binding.</summary>
        public static void ClearBindings() =>
            InputActions.ClearBindings();

        /// <summary>
        /// Replaces the default bindings and applies them, discarding any current bindings (the engine calls it
        /// at startup with the project's bindings); the snapshot is what <see cref="ResetBindingsToDefaults"/>
        /// restores.
        /// </summary>
        /// <param name="defaults">Actions mapped to their default bindings. May be <see langword="null"/>.</param>
        public static void SetDefaultBindings(IReadOnlyDictionary<string, InputBinding[]>? defaults) =>
            InputActions.SetDefaultBindings(defaults);

        /// <summary>
        /// Discards current bindings and restores the defaults set by <see cref="SetDefaultBindings"/>.
        /// </summary>
        public static void ResetBindingsToDefaults() =>
            InputActions.ResetBindingsToDefaults();
    }
}

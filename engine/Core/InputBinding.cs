using SKey = Spot.Core.Key;
using SMouse = Spot.Core.MouseButton;

namespace Spot.Core;

/// <summary>
/// The kind of physical device an <see cref="InputBinding"/> refers to.
/// </summary>
public enum InputDeviceKind
{
    /// <summary>A keyboard key (see <see cref="Key"/>).</summary>
    Keyboard,

    /// <summary>A mouse button (see <see cref="MouseButton"/>).</summary>
    Mouse,
}

/// <summary>
/// A single bindable physical input: either a keyboard <see cref="Key"/> or a
/// <see cref="MouseButton"/>. The action-binding layer of <see cref="Input"/> maps string action
/// names (for example "forward") to one or more of these, so a project can query actions instead of
/// hard-coded keys and rebind them at runtime.
/// </summary>
/// <param name="Device">Which device the <paramref name="Code"/> belongs to.</param>
/// <param name="Code">The device-specific code: a <see cref="Key"/> or <see cref="MouseButton"/> value.</param>
public readonly record struct InputBinding(InputDeviceKind Device, int Code)
{
    // Human-typed aliases and short forms that don't fall out of the enum names directly (digits,
    // mouse buttons, common abbreviations). Canonical tokens (enum names, digits, "mouseN") are still
    // handled without an entry here, so ToString stays the single source of the canonical spelling.
    private static readonly Dictionary<string, InputBinding> Aliases = BuildAliases();

    /// <summary>Creates a binding for the given keyboard key.</summary>
    /// <param name="key">The key to bind.</param>
    /// <returns>The binding.</returns>
    public static InputBinding Key(SKey key) => new(InputDeviceKind.Keyboard, (int)key);

    /// <summary>Creates a binding for the given mouse button.</summary>
    /// <param name="button">The button to bind.</param>
    /// <returns>The binding.</returns>
    public static InputBinding Mouse(SMouse button) => new(InputDeviceKind.Mouse, (int)button);

    /// <summary>
    /// Parses a console/config token (e.g. "w", "space", "up", "0", "f1", "mouse0", "lmb") into a
    /// binding. Case-insensitive.
    /// </summary>
    /// <param name="token">The token to parse.</param>
    /// <param name="binding">The parsed binding when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if the token names a known key or mouse button.</returns>
    public static bool TryParse(string? token, out InputBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string t = token.Trim().ToLowerInvariant();

        if (Aliases.TryGetValue(t, out binding))
        {
            return true;
        }

        // The bulk of keys match their enum name directly ("w", "space", "up", "f1", "leftshift").
        // Reject numeric input (digits are handled as aliases above) and undefined values so a token
        // like "999" or "unknown" doesn't parse.
        if (!char.IsDigit(t[0]) && Enum.TryParse(t, ignoreCase: true, out SKey key)
            && key != SKey.Unknown && Enum.IsDefined(key))
        {
            binding = Key(key);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the canonical token for this binding, round-trippable through <see cref="TryParse"/>.
    /// </summary>
    /// <returns>The canonical token (e.g. "w", "space", "0", "mouse0").</returns>
    public override string ToString()
    {
        if (Device == InputDeviceKind.Mouse)
        {
            return "mouse" + Code.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var key = (SKey)Code;
        if (key >= SKey.Alpha0 && key <= SKey.Alpha9)
        {
            return ((char)('0' + (Code - (int)SKey.Alpha0))).ToString();
        }

        return key.ToString().ToLowerInvariant();
    }

    private static Dictionary<string, InputBinding> BuildAliases()
    {
        var a = new Dictionary<string, InputBinding>(StringComparer.OrdinalIgnoreCase);

        void AddKey(SKey key, params string[] names)
        {
            var b = Key(key);
            foreach (string n in names)
            {
                a[n] = b;
            }
        }

        void AddMouse(SMouse button, params string[] names)
        {
            var b = Mouse(button);
            foreach (string n in names)
            {
                a[n] = b;
            }
        }

        for (int d = 0; d <= 9; d++)
        {
            a[((char)('0' + d)).ToString()] = new InputBinding(InputDeviceKind.Keyboard, (int)SKey.Alpha0 + d);
        }

        AddMouse(SMouse.Left, "mouse0", "mouseleft", "lmb");
        AddMouse(SMouse.Right, "mouse1", "mouseright", "rmb");
        AddMouse(SMouse.Middle, "mouse2", "mousemiddle", "mmb");
        AddMouse(SMouse.Button4, "mouse3");
        AddMouse(SMouse.Button5, "mouse4");

        AddKey(SKey.Escape, "esc");
        AddKey(SKey.Enter, "return");
        AddKey(SKey.Delete, "del");
        AddKey(SKey.LeftControl, "ctrl", "control", "lctrl");
        AddKey(SKey.RightControl, "rctrl");
        AddKey(SKey.LeftShift, "shift", "lshift");
        AddKey(SKey.RightShift, "rshift");
        AddKey(SKey.LeftAlt, "alt", "lalt");
        AddKey(SKey.RightAlt, "ralt");
        AddKey(SKey.Comma, ",");
        AddKey(SKey.Period, ".");
        AddKey(SKey.Slash, "/");
        AddKey(SKey.Minus, "-");
        AddKey(SKey.Apostrophe, "'");

        return a;
    }
}

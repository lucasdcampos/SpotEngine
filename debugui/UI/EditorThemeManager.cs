namespace Spot.DebugUI.UI;

/// <summary>
/// Owns the editor's active theme. This is the seam for future theme customization: a preferences
/// panel can edit <see cref="Current"/>'s palette/metrics and call <see cref="SetTheme"/> (or apply
/// a freshly loaded theme) to update the whole editor at once.
/// </summary>
public static class EditorThemeManager
{
    private static EditorTheme _current = EditorThemes.SpotDark;

    /// <summary>Gets the theme currently applied to the editor.</summary>
    public static EditorTheme Current => _current;

    /// <summary>
    /// Makes <paramref name="theme"/> the active theme and applies it to the ImGui context. Requires
    /// an active ImGui context (call after the ImGui controller exists).
    /// </summary>
    public static void SetTheme(EditorTheme theme)
    {
        _current = theme;
        theme.Apply();
    }

    /// <summary>Re-applies the current theme, e.g. after editing its palette or metrics in place.</summary>
    public static void Reapply() => _current.Apply();
}

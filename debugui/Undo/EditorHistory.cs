namespace Spot.DebugUI.Undo;

/// <summary>
/// Holds the editor's single undo history, so the panels spread across <c>Spot.DebugUI</c> and
/// <c>Spot.Editor</c> can reach it without every one of them taking it as a constructor argument.
/// </summary>
/// <remarks>
/// One history for the whole editor is a deliberate product decision: <c>Ctrl</c>+<c>Z</c> undoes the
/// last thing the user did, whichever panel they did it in, rather than depending on which tab happens
/// to be focused. <see cref="UndoHistory"/> itself is an ordinary instantiable class — tests build
/// their own and never touch this holder.
/// </remarks>
public static class EditorHistory
{
    /// <summary>The active history. Replaceable so a host can substitute its own instance.</summary>
    public static UndoHistory Current { get; set; } = new();

    /// <summary>
    /// Maps a scene to the host's document object for it (an open editor tab), so an action recorded by
    /// a panel marks the right document as having unsaved changes. Panels live in this assembly and
    /// know nothing about the editor's tab bookkeeping, so the host installs this.
    /// </summary>
    public static Func<Spot.Scenes.Scene, object?>? SceneDocumentResolver { get; set; }

    /// <summary>
    /// The document owning a scene, or <see langword="null"/> when the host has not registered a
    /// resolver or does not recognise the scene (a scratch scene, for instance).
    /// </summary>
    public static object? DocumentFor(Spot.Scenes.Scene? scene) =>
        scene != null ? SceneDocumentResolver?.Invoke(scene) : null;
}

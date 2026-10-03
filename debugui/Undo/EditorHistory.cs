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
    public static Func<Spot.Engine.Scenes.Scene, object?>? SceneDocumentResolver { get; set; }

    /// <summary>
    /// The document owning a scene, or <see langword="null"/> when the host has not registered a
    /// resolver or does not recognise the scene (a scratch scene, for instance).
    /// </summary>
    public static object? DocumentFor(Spot.Engine.Scenes.Scene? scene) =>
        scene != null ? SceneDocumentResolver?.Invoke(scene) : null;

    /// <summary>
    /// Records a structural edit a panel has just made to a scene (entities added, say) as one named
    /// entry that restores the whole scene. Panels cannot snapshot or restore a scene tab themselves, so
    /// the host installs this; when it has not, the edit is left to the host's periodic catch-all.
    /// </summary>
    public static Action<Spot.Engine.Scenes.Scene, string>? SceneEditRecorder { get; set; }

    /// <summary>
    /// Records a structural edit of <paramref name="scene"/> through <see cref="SceneEditRecorder"/>. Never
    /// throws: a recorder that fails is logged and the edit stays in the scene.
    /// </summary>
    /// <param name="scene">The edited scene.</param>
    /// <param name="label">What the edit did, e.g. "Add Sprite 'hero'".</param>
    public static void RecordSceneEdit(Spot.Engine.Scenes.Scene? scene, string label)
    {
        if (scene == null || SceneEditRecorder == null)
        {
            return;
        }

        try
        {
            SceneEditRecorder(scene, label);
        }
        catch (Exception ex)
        {
            Spot.Framework.Log.CoreWarn("Could not record '{0}' in the undo history: {1}", label, ex.Message);
        }
    }
}

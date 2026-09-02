using System.Collections.Generic;
using Spot.UI;

namespace Spot.Scenes;

/// <summary>
/// References a <c>.sptui</c> UI document authored in the editor and instantiates it into the scene's
/// screen-space UI (<see cref="Scene.UI"/>) when play mode starts. It is the data-driven bridge between an
/// editor-authored UI and a running scene: drop a document reference here and its widgets appear at runtime,
/// with no code required. Scripts can still reach the instantiated widgets by name via
/// <c>Scene.UI.Find&lt;Button&gt;("Play")</c> to wire up behaviour.
/// </summary>
/// <remarks>
/// Instantiation happens once, before scripts run (see <c>UICanvasSystem</c>), so a script's <c>OnStart</c>
/// can already find the widgets. Only the document reference is serialized; the instantiated widgets are
/// runtime state.
/// </remarks>
[ComponentMenu("UI Canvas", Order = 20)]
[SceneComponent("UICanvas")]
public sealed class UICanvasComponent : Component
{
    /// <summary>
    /// Gets or sets the reference (a <c>guid:</c> reference or a source path) to the <c>.sptui</c> document
    /// instantiated at play time. Drawn as an asset slot by the editor rather than a raw string.
    /// </summary>
    [HideInInspector]
    [SerializeHidden]
    public string? DocumentRef { get; set; }

    // Runtime state: whether the document has been instantiated this play session, and the top-level widgets
    // it added to Scene.UI. Neither is serialized (fields on a component are ignored by the scene serializer).
    internal bool Instantiated;
    internal readonly List<Widget> Instances = new();
}

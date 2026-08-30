using Spot.Scenes;
using Spot.UI;

namespace Spot.DebugUI;

/// <summary>What the shared Hierarchy panel currently shows: the scene's entities, or the open UI document's widgets.</summary>
public enum HierarchyTarget
{
    /// <summary>The scene's entity tree.</summary>
    Scene,

    /// <summary>The open UI document's widget tree.</summary>
    UI,
}

public interface ISelectionContext
{
    Scene? ActiveScene { get; }
    Entity? Selection { get; set; }
    string? SelectedAssetPath { get; set; }

    /// <summary>
    /// Which tree the single Hierarchy panel shows. The editor flips it to <see cref="HierarchyTarget.UI"/> when
    /// the UI Canvas is focused and back to <see cref="HierarchyTarget.Scene"/> when a scene viewport is focused.
    /// </summary>
    HierarchyTarget HierarchyTarget { get; set; }

    /// <summary>
    /// The UI document (a <c>.sptui</c> loaded as a widget tree) currently open for editing, or
    /// <see langword="null"/> when no UI document is open. When set, the UI Hierarchy, UI Canvas and Inspector
    /// author this tree instead of the scene.
    /// </summary>
    UIRoot? EditingDocument { get; set; }

    /// <summary>The source path of the open UI document, used to save it. <see langword="null"/> when none is open.</summary>
    string? EditingDocumentPath { get; set; }

    /// <summary>The selected widget within <see cref="EditingDocument"/>, shown in the Inspector.</summary>
    Widget? SelectedWidget { get; set; }
}

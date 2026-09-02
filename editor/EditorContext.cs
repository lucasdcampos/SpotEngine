using System.Collections.Generic;
using Spot.Scenes;
using Spot.DebugUI;
using Spot.UI;

namespace Spot.Editor;

public class EditorContext : ISelectionContext
{
    public Scene? ActiveScene { get; set; }

    // Backing store for the (multi-)entity selection. The last element is the primary selection.
    private readonly List<Entity> _selectedEntities = new();

    /// <summary>
    /// The primary selected entity (the last one added), or <see langword="null"/> when nothing is selected.
    /// Selecting an entity collapses the multi-selection to just it and clears any selected asset and UI
    /// widget, so the Inspector shows one thing at a time.
    /// </summary>
    public Entity? Selection
    {
        get => _selectedEntities.Count > 0 ? _selectedEntities[^1] : null;
        set
        {
            _selectedEntities.Clear();
            if (value != null)
            {
                _selectedEntities.Add(value.Value);
                SelectedAssetPath = null;
                SelectedWidget = null;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Entity> SelectedEntities => _selectedEntities;

    /// <inheritdoc />
    public void SetSelectedEntities(IReadOnlyList<Entity> entities)
    {
        _selectedEntities.Clear();
        _selectedEntities.AddRange(entities);
        if (_selectedEntities.Count > 0)
        {
            SelectedAssetPath = null;
            SelectedWidget = null;
        }
    }

    /// <summary>
    /// The full path of the currently selected asset (for example a material), or <see langword="null"/>.
    /// Set this to inspect an asset; setting <see cref="Selection"/> to an entity clears it.
    /// </summary>
    public string? SelectedAssetPath { get; set; }

    /// <inheritdoc />
    public HierarchyTarget HierarchyTarget { get; set; } = HierarchyTarget.Scene;

    /// <inheritdoc />
    public UIRoot? EditingDocument { get; set; }

    /// <inheritdoc />
    public string? EditingDocumentPath { get; set; }

    private Widget? _selectedWidget;

    /// <inheritdoc />
    public Widget? SelectedWidget
    {
        get => _selectedWidget;
        set
        {
            _selectedWidget = value;
            if (value != null)
            {
                _selectedEntities.Clear();
                SelectedAssetPath = null;
            }
        }
    }
}

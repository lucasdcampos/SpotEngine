using Spot.Scenes;
using Spot.DebugUI;
using Spot.UI;

namespace Spot.Editor;

public class EditorContext : ISelectionContext
{
    public Scene? ActiveScene { get; set; }

    private Entity? _selection;

    /// <summary>
    /// The currently selected entity. Selecting an entity clears any selected asset and UI widget, so the
    /// Inspector shows one thing at a time.
    /// </summary>
    public Entity? Selection
    {
        get => _selection;
        set
        {
            _selection = value;
            if (value != null)
            {
                SelectedAssetPath = null;
                SelectedWidget = null;
            }
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
                _selection = null;
                SelectedAssetPath = null;
            }
        }
    }
}

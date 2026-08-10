using Spot.Scenes;
using Spot.Engine.Debug;

namespace Spot.Editor;

public class EditorContext : ISelectionContext
{
    public Scene? ActiveScene { get; set; }

    private Entity? _selection;

    /// <summary>
    /// The currently selected entity. Selecting an entity clears any selected asset, so the Inspector
    /// shows one or the other, never both.
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
            }
        }
    }

    /// <summary>
    /// The full path of the currently selected asset (for example a material), or <see langword="null"/>.
    /// Set this to inspect an asset; setting <see cref="Selection"/> to an entity clears it.
    /// </summary>
    public string? SelectedAssetPath { get; set; }
}

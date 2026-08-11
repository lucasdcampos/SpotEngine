using Spot.Scenes;

namespace Spot.DebugUI;

public interface ISelectionContext
{
    Scene? ActiveScene { get; }
    Entity? Selection { get; set; }
    string? SelectedAssetPath { get; set; }
}

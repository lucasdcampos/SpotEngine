using Spot.Scenes;

namespace Spot.Engine.Debug;

public interface ISelectionContext
{
    Scene? ActiveScene { get; }
    Entity? Selection { get; set; }
    string? SelectedAssetPath { get; set; }
}

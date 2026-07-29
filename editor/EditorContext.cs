using Spot.Scenes;

namespace Spot.Editor;

public class EditorContext
{
    public Scene? ActiveScene { get; set; }
    public Entity? Selection { get; set; }
}

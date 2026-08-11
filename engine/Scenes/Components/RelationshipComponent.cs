using System.Collections.Generic;

namespace Spot.Scenes;

public class RelationshipComponent : Component
{
    public Entity? Parent { get; internal set; }
    public List<Entity> Children { get; } = new();
}

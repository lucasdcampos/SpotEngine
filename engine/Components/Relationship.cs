using Spot.Engine.Scenes;
using System.Collections.Generic;

namespace Spot.Engine;

public sealed class Relationship : Component
{
    public Entity? Parent { get; internal set; }
    public List<Entity> Children { get; } = new();
}

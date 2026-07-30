using System.Numerics;
using Spot.Assets;

namespace Spot.Scenes;

/// <summary>
/// A component that marks an entity as a drawable 3D model. Like <see cref="Sprite2D"/> it is
/// data-only: it holds what to draw (a <see cref="Assets.Model"/> and a color) and a render system
/// draws it together with the entity's <see cref="Rendering.Transform"/>.
/// </summary>
public sealed class MeshRenderer
{
    /// <summary>
    /// Gets or sets the model to draw. When <see langword="null"/>, nothing is drawn.
    /// </summary>
    public Model? Model { get; set; }

    /// <summary>
    /// Gets or sets the path to the model file, used for serialization.
    /// </summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Gets or sets the color multiplied into the shaded model. Defaults to opaque white.
    /// </summary>
    public Vector4 Color { get; set; } = Vector4.One;
}

using System.Numerics;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// A component that marks an entity as a drawable 2D sprite. It carries what to draw (an optional
/// texture and a color); a render system reads it and, together with the entity's <see cref="Transform"/>,
/// issues the actual draw call.
/// </summary>
/// <remarks>
/// This is the data-only counterpart to Unity's SpriteRenderer. It deliberately holds no drawing
/// logic — the engine's renderer does the drawing.
/// </remarks>
public sealed class Sprite2D : Component
{
    /// <summary>
    /// Gets or sets the texture to draw. When <see langword="null"/>, the sprite is a solid
    /// <see cref="Color"/> quad.
    /// </summary>
    public Texture2D? Texture { get; set; }

    /// <summary>
    /// Gets or sets the path to the texture file, used for serialization.
    /// </summary>
    public string? TexturePath { get; set; }

    /// <summary>
    /// Gets or sets the color. For a textured sprite this multiplies the sampled texture (a tint);
    /// for an untextured sprite it is the fill color. Defaults to opaque white.
    /// </summary>
    public Vector4 Color { get; set; } = Vector4.One;
}

using Silk.NET.OpenGL;

namespace Spot.Rendering;

/// <summary>
/// The type of a single vertex attribute, used to describe a vertex buffer layout
/// without exposing the underlying graphics API.
/// </summary>
public enum ShaderDataType
{
    /// <summary>An unspecified type.</summary>
    None = 0,

    /// <summary>A single 32-bit float.</summary>
    Float,

    /// <summary>A 2-component vector of 32-bit floats.</summary>
    Float2,

    /// <summary>A 3-component vector of 32-bit floats.</summary>
    Float3,

    /// <summary>A 4-component vector of 32-bit floats.</summary>
    Float4,

    /// <summary>A single 32-bit integer.</summary>
    Int,

    /// <summary>A 2-component vector of 32-bit integers.</summary>
    Int2,

    /// <summary>A 3-component vector of 32-bit integers.</summary>
    Int3,

    /// <summary>A 4-component vector of 32-bit integers.</summary>
    Int4,

    /// <summary>A single boolean stored as one byte.</summary>
    Bool,
}

/// <summary>
/// Helpers that map a <see cref="ShaderDataType"/> to its size, component count, and graphics API type.
/// </summary>
internal static class ShaderDataTypeExtensions
{
    /// <summary>
    /// Gets the total size of the attribute, in bytes.
    /// </summary>
    /// <param name="type">The attribute type.</param>
    /// <returns>The size in bytes.</returns>
    public static uint Size(this ShaderDataType type) => type switch
    {
        ShaderDataType.Float => 4,
        ShaderDataType.Float2 => 4 * 2,
        ShaderDataType.Float3 => 4 * 3,
        ShaderDataType.Float4 => 4 * 4,
        ShaderDataType.Int => 4,
        ShaderDataType.Int2 => 4 * 2,
        ShaderDataType.Int3 => 4 * 3,
        ShaderDataType.Int4 => 4 * 4,
        ShaderDataType.Bool => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown shader data type."),
    };

    /// <summary>
    /// Gets the number of components in the attribute (for example 3 for <see cref="ShaderDataType.Float3"/>).
    /// </summary>
    /// <param name="type">The attribute type.</param>
    /// <returns>The component count.</returns>
    public static int ComponentCount(this ShaderDataType type) => type switch
    {
        ShaderDataType.Float => 1,
        ShaderDataType.Float2 => 2,
        ShaderDataType.Float3 => 3,
        ShaderDataType.Float4 => 4,
        ShaderDataType.Int => 1,
        ShaderDataType.Int2 => 2,
        ShaderDataType.Int3 => 3,
        ShaderDataType.Int4 => 4,
        ShaderDataType.Bool => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown shader data type."),
    };

    /// <summary>
    /// Gets the graphics API base type used for the attribute's components.
    /// </summary>
    /// <param name="type">The attribute type.</param>
    /// <returns>The corresponding <see cref="VertexAttribPointerType"/>.</returns>
    public static VertexAttribPointerType ToVertexAttribPointerType(this ShaderDataType type) => type switch
    {
        ShaderDataType.Float or ShaderDataType.Float2 or ShaderDataType.Float3 or ShaderDataType.Float4
            => VertexAttribPointerType.Float,
        ShaderDataType.Int or ShaderDataType.Int2 or ShaderDataType.Int3 or ShaderDataType.Int4
            => VertexAttribPointerType.Int,
        ShaderDataType.Bool => VertexAttribPointerType.UnsignedByte,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown shader data type."),
    };
}

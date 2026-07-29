using System.Numerics;

namespace Spot.Rendering;

/// <summary>
/// A position, rotation, and scale in 3D space that produces a model matrix.
/// </summary>
/// <remarks>
/// The engine is 3D-first, so a transform is fully three-dimensional. For 2D (Unity-style)
/// usage, keep <see cref="Position"/>.Z at zero, rotate only around Z via <see cref="Rotation"/>.Z,
/// and leave <see cref="Scale"/>.Z at one.
/// </remarks>
public sealed class Transform
{
    private const float DegreesToRadians = MathF.PI / 180.0f;

    /// <summary>
    /// Gets or sets the position, in world units.
    /// </summary>
    public Vector3 Position { get; set; } = Vector3.Zero;

    /// <summary>
    /// Gets or sets the rotation as Euler angles in degrees (X = pitch, Y = yaw, Z = roll).
    /// </summary>
    public Vector3 Rotation { get; set; } = Vector3.Zero;

    /// <summary>
    /// Gets or sets the scale along each axis.
    /// </summary>
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>
    /// Gets the model matrix that maps local space to world space.
    /// </summary>
    public Matrix4x4 Matrix
    {
        get
        {
            Vector3 radians = Rotation * DegreesToRadians;
            return Matrix4x4.CreateScale(Scale)
                * Matrix4x4.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z)
                * Matrix4x4.CreateTranslation(Position);
        }
    }
}

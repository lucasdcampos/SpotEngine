using System.Numerics;
using Spot.Scenes;

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

    public Entity? Entity { get; internal set; }

    /// <summary>
    /// Gets or sets the position, in world units.
    /// </summary>
    public Vector3 Position { get; set; } = Vector3.Zero;

    /// <summary>
    /// Gets the world position.
    /// </summary>
    public Vector3 WorldPosition => Matrix.Translation;

    /// <summary>
    /// Gets or sets the rotation as Euler angles in degrees (X = pitch, Y = yaw, Z = roll).
    /// </summary>
    public Vector3 Rotation { get; set; } = Vector3.Zero;

    /// <summary>
    /// Gets the world rotation as Euler angles in degrees.
    /// </summary>
    public Vector3 WorldRotation
    {
        get
        {
            Vector3 worldRot = Rotation;
            Entity? parentEntity = Entity != null && Entity.Value.TryGetComponent(out RelationshipComponent? rel) ? rel.Parent : null;
            if (parentEntity != null && parentEntity.Value.TryGetComponent(out Transform? parentTransform))
            {
                worldRot += parentTransform.WorldRotation;
            }
            return worldRot;
        }
    }

    /// <summary>
    /// Gets or sets the scale along each axis.
    /// </summary>
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>
    /// Gets the local model matrix.
    /// </summary>
    public Matrix4x4 LocalMatrix
    {
        get
        {
            Vector3 radians = Rotation * DegreesToRadians;
            return Matrix4x4.CreateScale(Scale)
                * Matrix4x4.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z)
                * Matrix4x4.CreateTranslation(Position);
        }
    }

    /// <summary>
    /// Gets the model matrix that maps local space to world space.
    /// </summary>
    public Matrix4x4 Matrix
    {
        get
        {
            Matrix4x4 local = LocalMatrix;

            if (Entity != null && Entity.Value.TryGetComponent(out RelationshipComponent? rel) && rel.Parent != null)
            {
                if (rel.Parent.Value.TryGetComponent(out Transform? parentTransform))
                {
                    return local * parentTransform.Matrix;
                }
            }

            return local;
        }
    }
}

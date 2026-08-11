using System.Numerics;

namespace Spot.Scenes;

/// <summary>
/// A position, rotation, and scale in 3D space that produces a model matrix.
/// </summary>
/// <remarks>
/// The engine is 3D-first, so a transform is fully three-dimensional. For 2D (Unity-style)
/// usage, keep <see cref="Position"/>.Z at zero, rotate only around Z via <see cref="Rotation"/>.Z,
/// and leave <see cref="Scale"/>.Z at one.
/// </remarks>
[ComponentMenu("Transform", Addable = false, Removable = false, Order = 0)]
[SceneComponent("Transform")]
public sealed class TransformComponent : Component
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
    [HideInInspector]
    public Vector3 WorldPosition => Matrix.Translation;

    /// <summary>
    /// Gets or sets the rotation as Euler angles in degrees (X = pitch, Y = yaw, Z = roll).
    /// </summary>
    public Vector3 Rotation { get; set; } = Vector3.Zero;

    /// <summary>
    /// Gets the world rotation as Euler angles in degrees.
    /// </summary>
    [HideInInspector]
    public Vector3 WorldRotation
    {
        get
        {
            Vector3 worldRot = Rotation;
            Entity? parentEntity = Entity != null && Entity.Value.TryGetComponent(out RelationshipComponent? rel) ? rel.Parent : null;
            if (parentEntity != null && parentEntity.Value.TryGetComponent(out TransformComponent? parentTransform))
            {
                worldRot += parentTransform.WorldRotation;
            }
            return worldRot;
        }
    }

    /// <summary>
    /// Gets or sets the scale along each axis.
    /// </summary>
    [InspectorReset(1.0f)]
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>
    /// Gets the world scale.
    /// </summary>
    [HideInInspector]
    public Vector3 WorldScale
    {
        get
        {
            if (Matrix4x4.Decompose(Matrix, out Vector3 scale, out _, out _))
                return scale;
            return Scale;
        }
    }

    /// <summary>
    /// Gets the local model matrix.
    /// </summary>
    [HideInInspector]
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
    [HideInInspector]
    public Matrix4x4 Matrix
    {
        get
        {
            Matrix4x4 local = LocalMatrix;

            if (Entity != null && Entity.Value.TryGetComponent(out RelationshipComponent? rel) && rel.Parent != null)
            {
                if (rel.Parent.Value.TryGetComponent(out TransformComponent? parentTransform))
                {
                    return local * parentTransform.Matrix;
                }
            }

            return local;
        }
    }
}

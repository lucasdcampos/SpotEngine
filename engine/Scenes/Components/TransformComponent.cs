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

    private Vector3 _position = Vector3.Zero;
    private Vector3 _rotation = Vector3.Zero;
    private Vector3 _scale = Vector3.One;

    private Matrix4x4 _localMatrix = Matrix4x4.Identity;
    private Matrix4x4 _worldMatrix = Matrix4x4.Identity;
    
    private bool _localDirty = true;
    private bool _worldDirty = true;
    
    public int Version { get; private set; } = 1;
    private int _parentVersion = 0;
    private Entity? _lastParent = null;

    /// <summary>
    /// Gets or sets the position, in world units.
    /// </summary>
    public Vector3 Position 
    { 
        get => _position; 
        set { if (_position != value) { _position = value; SetDirty(); } }
    }

    /// <summary>
    /// Gets the world position.
    /// </summary>
    [HideInInspector]
    public Vector3 WorldPosition => Matrix.Translation;

    /// <summary>
    /// Gets or sets the rotation as Euler angles in degrees (X = pitch, Y = yaw, Z = roll).
    /// </summary>
    public Vector3 Rotation 
    { 
        get => _rotation; 
        set { if (_rotation != value) { _rotation = value; SetDirty(); } }
    }

    /// <summary>
    /// Gets the world rotation as Euler angles in degrees.
    /// </summary>
    [HideInInspector]
    public Vector3 WorldRotation
    {
        get
        {
            Vector3 worldRot = Rotation;
            TransformComponent? parentTransform = GetParentTransform();
            if (parentTransform != null)
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
    public Vector3 Scale 
    { 
        get => _scale; 
        set { if (_scale != value) { _scale = value; SetDirty(); } }
    }

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

    private void SetDirty()
    {
        _localDirty = true;
        _worldDirty = true;
        Version++;
    }

    private TransformComponent? GetParentTransform()
    {
        if (Entity != null && Entity.Value.TryGetComponent(out RelationshipComponent? rel) && rel.Parent != null)
        {
            if (rel.Parent.Value.TryGetComponent(out TransformComponent? parentTransform))
            {
                return parentTransform;
            }
        }
        return null;
    }

    /// <summary>
    /// Gets the local model matrix.
    /// </summary>
    [HideInInspector]
    public Matrix4x4 LocalMatrix
    {
        get
        {
            if (_localDirty)
            {
                Vector3 radians = _rotation * DegreesToRadians;
                _localMatrix = Matrix4x4.CreateScale(_scale)
                    * Matrix4x4.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z)
                    * Matrix4x4.CreateTranslation(_position);
                _localDirty = false;
            }
            return _localMatrix;
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
            TransformComponent? parentTransform = GetParentTransform();
            Entity? currentParent = parentTransform?.Entity;

            // If reparented, force update
            if (_lastParent != currentParent)
            {
                _lastParent = currentParent;
                _worldDirty = true;
            }

            if (parentTransform != null)
            {
                int pVer = parentTransform.Version;
                // Read parent.Matrix BEFORE checking if we need to update, as it updates its own version if dirty
                Matrix4x4 pMat = parentTransform.Matrix;
                
                // pVer might have changed if parent recomputed its Matrix
                pVer = parentTransform.Version;

                if (_worldDirty || _parentVersion != pVer || _localDirty)
                {
                    _worldMatrix = LocalMatrix * pMat;
                    _parentVersion = pVer;
                    _worldDirty = false;
                    Version++;
                }
            }
            else
            {
                if (_worldDirty || _parentVersion != 0 || _localDirty)
                {
                    _worldMatrix = LocalMatrix;
                    _parentVersion = 0;
                    _worldDirty = false;
                    Version++;
                }
            }

            return _worldMatrix;
        }
    }
}

using System.Numerics;

namespace Spot.Framework.Graphics;

/// <summary>
/// A perspective camera for code-driven 3D: a position looking at a target, with the view and projection
/// matrices it implies. Hand <see cref="ViewProjection"/> to a renderer (for example
/// <see cref="BasicRenderer3D.BeginScene(Camera3D, Vector3?, Vector3?, float)"/>).
/// </summary>
public sealed class Camera3D
{
    private float _fieldOfView = 60.0f;
    private float _near = 0.1f;
    private float _far = 1000.0f;
    private float _aspectRatio = 16.0f / 9.0f;

    /// <summary>Gets or sets the camera position.</summary>
    public Vector3 Position { get; set; } = new(0.0f, 0.0f, 5.0f);

    /// <summary>Gets or sets the point the camera looks at.</summary>
    public Vector3 Target { get; set; } = Vector3.Zero;

    /// <summary>Gets or sets the camera's up direction.</summary>
    public Vector3 Up { get; set; } = Vector3.UnitY;

    /// <summary>Gets or sets the vertical field of view in degrees, clamped to (0, 180).</summary>
    public float FieldOfView
    {
        get => _fieldOfView;
        set => _fieldOfView = Math.Clamp(value, 0.01f, 179.0f);
    }

    /// <summary>Gets or sets the near clip distance (kept positive and in front of the far plane).</summary>
    public float NearPlane
    {
        get => _near;
        set => _near = Math.Max(0.0001f, value);
    }

    /// <summary>Gets or sets the far clip distance (kept beyond the near plane).</summary>
    public float FarPlane
    {
        get => _far;
        set => _far = value;
    }

    /// <summary>Gets or sets the width / height aspect ratio. See <see cref="SetViewport"/>.</summary>
    public float AspectRatio
    {
        get => _aspectRatio;
        set => _aspectRatio = value > 0.0f ? value : _aspectRatio;
    }

    /// <summary>Gets the unit direction the camera looks along (forward when it sits on its target).</summary>
    public Vector3 Forward
    {
        get
        {
            Vector3 direction = Target - Position;
            return direction.LengthSquared() > 1e-12f ? Vector3.Normalize(direction) : -Vector3.UnitZ;
        }
    }

    /// <summary>Gets the view matrix.</summary>
    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, Position + Forward, Up);

    /// <summary>Gets the projection matrix.</summary>
    public Matrix4x4 Projection => Matrix4x4.CreatePerspectiveFieldOfView(
        _fieldOfView * (MathF.PI / 180.0f), _aspectRatio, _near, Math.Max(_far, _near + 0.001f));

    /// <summary>Gets the combined view-projection matrix.</summary>
    public Matrix4x4 ViewProjection => View * Projection;

    /// <summary>Points the camera at a target.</summary>
    /// <param name="target">The point to look at.</param>
    public void LookAt(Vector3 target) => Target = target;

    /// <summary>Matches the aspect ratio to a viewport (ignored when either side is zero).</summary>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    public void SetViewport(float width, float height)
    {
        if (width > 0.0f && height > 0.0f)
        {
            _aspectRatio = width / height;
        }
    }
}

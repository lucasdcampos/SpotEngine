using System.Numerics;

namespace Spot.Rendering;

/// <summary>
/// A 2D orthographic camera, positioned and rotated in the XY plane. This is the Unity-style
/// "2D" camera: geometry keeps its size regardless of distance, and the view is defined by a
/// rectangular region of world space.
/// </summary>
public sealed class OrthographicCamera
{
    private const float DegreesToRadians = MathF.PI / 180.0f;

    private Matrix4x4 _projection;
    private Matrix4x4 _view = Matrix4x4.Identity;
    private Vector3 _position = Vector3.Zero;
    private float _rotation;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrthographicCamera"/> class.
    /// </summary>
    /// <param name="left">The left edge of the view, in world units.</param>
    /// <param name="right">The right edge of the view, in world units.</param>
    /// <param name="bottom">The bottom edge of the view, in world units.</param>
    /// <param name="top">The top edge of the view, in world units.</param>
    public OrthographicCamera(float left, float right, float bottom, float top)
    {
        SetProjection(left, right, bottom, top);
    }

    /// <summary>
    /// Gets or sets the camera position, in world units.
    /// </summary>
    public Vector3 Position
    {
        get => _position;
        set
        {
            _position = value;
            RecalculateView();
        }
    }

    /// <summary>
    /// Gets or sets the camera rotation around the Z axis, in degrees.
    /// </summary>
    public float Rotation
    {
        get => _rotation;
        set
        {
            _rotation = value;
            RecalculateView();
        }
    }

    /// <summary>
    /// Gets the projection matrix.
    /// </summary>
    public Matrix4x4 Projection => _projection;

    /// <summary>
    /// Gets the view matrix.
    /// </summary>
    public Matrix4x4 View => _view;

    /// <summary>
    /// Gets the combined view-projection matrix to upload to shaders.
    /// </summary>
    public Matrix4x4 ViewProjection { get; private set; }

    /// <summary>
    /// Sets the orthographic view region, in world units.
    /// </summary>
    /// <param name="left">The left edge of the view.</param>
    /// <param name="right">The right edge of the view.</param>
    /// <param name="bottom">The bottom edge of the view.</param>
    /// <param name="top">The top edge of the view.</param>
    public void SetProjection(float left, float right, float bottom, float top)
    {
        _projection = Matrix4x4.CreateOrthographicOffCenter(left, right, bottom, top, -1.0f, 1.0f);
        RecalculateViewProjection();
    }

    private void RecalculateView()
    {
        Matrix4x4 transform =
            Matrix4x4.CreateRotationZ(_rotation * DegreesToRadians)
            * Matrix4x4.CreateTranslation(_position);

        Matrix4x4.Invert(transform, out _view);
        RecalculateViewProjection();
    }

    private void RecalculateViewProjection() => ViewProjection = _view * _projection;
}

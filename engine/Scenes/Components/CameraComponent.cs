using Spot.Rendering;
using System.Numerics;

namespace Spot.Scenes;

/// <summary>
/// Defines the projection type of a Scene Camera.
/// </summary>
public enum SceneCameraProjection
{
    Orthographic = 0,
    Perspective = 1
}

/// <summary>
/// A component that acts as a camera for the scene.
/// </summary>
[ComponentMenu("Camera", Order = 30)]
[SceneComponent("Camera")]
public class CameraComponent : Component
{
    public bool Primary { get; set; } = true;
    public bool FixedAspectRatio { get; set; } = false;

    [InspectorColor]
    [InspectorLabel("Background")]
    public Vector4 BackgroundColor { get; set; } = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

    private SceneCameraProjection _projectionType = SceneCameraProjection.Orthographic;
    public SceneCameraProjection ProjectionType
    {
        get => _projectionType;
        set
        {
            _projectionType = value;
            RecalculateProjection();
        }
    }

    private float _zoomLevel = 5.0f;
    [InspectorRange(0.1f, 100.0f, 0.1f)]
    [ShowIf(nameof(ProjectionType), SceneCameraProjection.Orthographic)]
    public float ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            _zoomLevel = value;
            RecalculateProjection();
        }
    }

    private float _fieldOfView = 45.0f;
    [InspectorRange(1.0f, 180.0f, 1.0f)]
    [InspectorLabel("Field Of View")]
    [ShowIf(nameof(ProjectionType), SceneCameraProjection.Perspective)]
    public float FieldOfView
    {
        get => _fieldOfView;
        set
        {
            _fieldOfView = value;
            RecalculateProjection();
        }
    }

    private float _nearClip = 0.1f;
    [InspectorRange(0.01f, 100.0f, 0.01f)]
    [InspectorLabel("Near Clip")]
    [ShowIf(nameof(ProjectionType), SceneCameraProjection.Perspective)]
    public float NearClip
    {
        get => _nearClip;
        set
        {
            _nearClip = value;
            RecalculateProjection();
        }
    }

    private float _farClip = 1000.0f;
    [InspectorRange(1.0f, 10000.0f, 1.0f)]
    [InspectorLabel("Far Clip")]
    public float FarClip
    {
        get => _farClip;
        set
        {
            _farClip = value;
            RecalculateProjection();
        }
    }

    private float _aspectRatio = 1.0f;
    [HideInInspector]
    public Matrix4x4 Projection { get; private set; }

    public CameraComponent()
    {
        RecalculateProjection();
    }

    public void SetViewportSize(float width, float height)
    {
        if (FixedAspectRatio || width == 0 || height == 0)
        {
            return;
        }

        _aspectRatio = width / height;
        RecalculateProjection();
    }

    private void RecalculateProjection()
    {
        if (ProjectionType == SceneCameraProjection.Perspective)
        {
            // Clamp defensively: CreatePerspectiveFieldOfView throws for near <= 0 or far <= near, and
            // near/far may arrive from a hand-edited scene file. Bad input must never take the app down.
            float near = MathF.Max(_nearClip, 0.001f);
            float far = MathF.Max(_farClip, near + 0.001f);
            Projection = Matrix4x4.CreatePerspectiveFieldOfView(_fieldOfView * (MathF.PI / 180.0f), _aspectRatio, near, far);
        }
        else
        {
            // Ortho depth runs symmetrically along the view axis (-far..far) so 3D orthographic scenes
            // aren't clipped by the old fixed -1..1 range; 2D is unaffected (its depth sits at 0).
            float far = MathF.Max(_farClip, 0.001f);
            Projection = Matrix4x4.CreateOrthographicOffCenter(-_aspectRatio * _zoomLevel, _aspectRatio * _zoomLevel, -_zoomLevel, _zoomLevel, -far, far);
        }
    }

    public Matrix4x4 GetViewProjection(TransformComponent transform)
    {
        Matrix4x4 view;
        if (ProjectionType == SceneCameraProjection.Perspective)
        {
            // The view matrix is the inverse of the transform's matrix (ignoring scale)
            Vector3 position = transform.WorldPosition;
            Vector3 rotation = transform.WorldRotation * (MathF.PI / 180.0f);
            
            Matrix4x4 t = Matrix4x4.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z) * Matrix4x4.CreateTranslation(position);
            Matrix4x4.Invert(t, out view);
        }
        else
        {
            Vector3 position = transform.WorldPosition;
            Matrix4x4 t = Matrix4x4.CreateRotationZ(transform.WorldRotation.Z * (MathF.PI / 180.0f)) * Matrix4x4.CreateTranslation(position);
            Matrix4x4.Invert(t, out view);
        }
        return view * Projection;
    }
}

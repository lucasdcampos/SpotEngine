using Spot.Rendering;
using System.Numerics;

namespace Spot.Scenes;

/// <summary>
/// A component that acts as a camera for the scene.
/// </summary>
public class CameraComponent
{
    public OrthographicCamera Camera { get; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the primary camera.
    /// </summary>
    public bool Primary { get; set; } = true;

    /// <summary>
    /// Gets or sets the fixed aspect ratio. If false, the camera's aspect ratio will automatically resize with the viewport.
    /// </summary>
    public bool FixedAspectRatio { get; set; } = false;

    /// <summary>
    /// Gets or sets the background clear color for this camera.
    /// </summary>
    public Vector4 BackgroundColor { get; set; } = new Vector4(0.1f, 0.1f, 0.1f, 1.0f);

    private float _zoomLevel = 5.0f;
    private float _aspectRatio = 1.0f;

    public float ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            _zoomLevel = value;
            RecalculateProjection();
        }
    }

    public CameraComponent()
    {
        Camera = new OrthographicCamera(-_aspectRatio * _zoomLevel, _aspectRatio * _zoomLevel, -_zoomLevel, _zoomLevel);
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
        Camera.SetProjection(-_aspectRatio * _zoomLevel, _aspectRatio * _zoomLevel, -_zoomLevel, _zoomLevel);
    }
}

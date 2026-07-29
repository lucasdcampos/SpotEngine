using System.Numerics;
using Spot.Rendering;

namespace Spot.Editor;

public class EditorCamera
{
    private OrthographicCamera _camera;
    private float _aspectRatio = 1.0f;
    private float _zoomLevel = 2.0f;
    private float _viewportHeight = 720.0f;

    public EditorCamera()
    {
        _camera = new OrthographicCamera(-_aspectRatio * _zoomLevel, _aspectRatio * _zoomLevel, -_zoomLevel, _zoomLevel);
    }

    public OrthographicCamera Camera => _camera;

    public void SetViewportSize(float width, float height)
    {
        _aspectRatio = width / height;
        _viewportHeight = height;
        _camera.SetProjection(-_aspectRatio * _zoomLevel, _aspectRatio * _zoomLevel, -_zoomLevel, _zoomLevel);
    }

    public void OnMouseScroll(float delta)
    {
        _zoomLevel -= delta * 0.25f;
        _zoomLevel = System.Math.Max(_zoomLevel, 0.25f);
        _camera.SetProjection(-_aspectRatio * _zoomLevel, _aspectRatio * _zoomLevel, -_zoomLevel, _zoomLevel);
    }

    public void OnMouseDrag(Vector2 delta)
    {
        float unitsPerPixel = GetUnitsPerPixel();
        var position = _camera.Position;
        position.X -= delta.X * unitsPerPixel;
        position.Y += delta.Y * unitsPerPixel;
        _camera.Position = position;
    }

    public float GetUnitsPerPixel()
    {
        return 2.0f * _zoomLevel / _viewportHeight;
    }
}

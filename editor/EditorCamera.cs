using Spot.Rendering;

namespace Spot.Editor;

public class EditorCamera
{
    private OrthographicCamera _camera;
    private float _aspectRatio = 1.0f;
    private float _zoomLevel = 2.0f;

    public EditorCamera()
    {
        _camera = new OrthographicCamera(-_aspectRatio * _zoomLevel, _aspectRatio * _zoomLevel, -_zoomLevel, _zoomLevel);
    }

    public OrthographicCamera Camera => _camera;

    public void SetViewportSize(float width, float height)
    {
        _aspectRatio = width / height;
        _camera.SetProjection(-_aspectRatio * _zoomLevel, _aspectRatio * _zoomLevel, -_zoomLevel, _zoomLevel);
    }
}

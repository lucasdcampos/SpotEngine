using Spot.Rendering;
using Spot.Assets;

namespace Spot.Core.Services;

/// <summary>
/// Initializes rendering systems and asset importers.
/// </summary>
public class GraphicsService : IEngineService
{
    public void Init(Application app)
    {
        var gl = Silk.NET.OpenGL.GL.GetApi(app.Window.NativeWindow);
        Renderer.Init(gl);
        Renderer2D.Init();
        Renderer3D.Init();
        PostProcessingRenderer.Init();
        ModelImporter.Register(new AssimpModelImporter());
        Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);
    }

    public void Shutdown()
    {
        Renderer2D.Shutdown();
    }
}

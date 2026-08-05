using Spot.Rendering;

namespace Spot.Core.Services;

/// <summary>
/// Initializes rendering systems.
/// </summary>
/// <remarks>
/// This runs in both the editor and a shipped game, so it deliberately does not register the Assimp source
/// importer: a shipped game loads cooked <c>.spmesh</c> meshes and must never pull in Assimp. Authoring hosts
/// (the editor) register the source importer themselves.
/// </remarks>
public class GraphicsService : IEngineService
{
    public void Init(Application app)
    {
        var gl = Silk.NET.OpenGL.GL.GetApi(app.Window.NativeWindow);
        Renderer.Init(gl);
        Renderer2D.Init();
        Renderer3D.Init();
        PostProcessingRenderer.Init();
        Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);
    }

    public void Shutdown()
    {
        Renderer2D.Shutdown();
    }
}

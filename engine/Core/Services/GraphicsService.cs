using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Core.Services;

/// <summary>
/// Initializes rendering systems.
/// </summary>
/// <remarks>
/// This runs in both the editor and a shipped game, so it deliberately does not register the Assimp source
/// importer: a shipped game loads cooked <c>.sptmesh</c> meshes and must never pull in Assimp. Authoring hosts
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
        BloomRenderer.Init();
        ParticleRenderer.Init();
        UIRenderer.Init();
        Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);

        // Route Scene.OnRender through the full 3D-first pipeline. The browser host installs its own slim
        // 2D scene renderer instead.
        SceneRenderer.Callback = static (scene, viewProjection, cameraPosition) =>
            RenderSystem.Render(scene, viewProjection, cameraPosition);
    }

    public void Shutdown()
    {
        UIRenderer.Shutdown();
        ParticleRenderer.Shutdown();
        Renderer2D.Shutdown();
    }
}

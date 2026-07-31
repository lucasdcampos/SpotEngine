using System.IO;
using ImGuiNET;
using Silk.NET.OpenGL.Extensions.ImGui;

namespace Spot.Core.Services;

/// <summary>
/// Manages the ImGui context and rendering loop.
/// </summary>
public class ImGuiService : IEngineService
{
    private readonly ApplicationSpec _spec;
    private ImGuiController? _controller;

    public ImGuiService(ApplicationSpec spec)
    {
        _spec = spec;
    }

    public void Init(Application app)
    {
        ImGuiFontConfig? fontConfig = null;
        if (!string.IsNullOrEmpty(_spec.FontPath) && File.Exists(_spec.FontPath))
        {
            fontConfig = new ImGuiFontConfig(_spec.FontPath, _spec.FontSize);
        }
        else if (!string.IsNullOrEmpty(_spec.FontPath))
        {
            Log.CoreWarn("UI font not found at '{0}', using the default font.", _spec.FontPath);
        }

        _controller = new ImGuiController(Spot.Rendering.Renderer.Api, app.Window.NativeWindow, app.Window.Input, fontConfig);
        ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        ImGui.StyleColorsDark();
    }

    public void Update(float deltaTime)
    {
        _controller?.Update(deltaTime);
    }

    public void ImGuiRender()
    {
        // This is called inside the try-catch block by Application.
    }

    /// <summary>
    /// Called by Application.Run in a finally block to ensure the frame is balanced.
    /// </summary>
    public void RenderFrame()
    {
        _controller?.Render();
    }

    public void Shutdown()
    {
        _controller?.Dispose();
        _controller = null;
    }
}

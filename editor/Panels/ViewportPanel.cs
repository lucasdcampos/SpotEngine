using System;
using System.Numerics;
using ImGuiNET;
using Spot.Rendering;

namespace Spot.Editor.Panels;

public class ViewportPanel
{
    private readonly EditorContext _context;
    private Framebuffer? _framebuffer;
    private EditorCamera? _camera;

    public ViewportPanel(EditorContext context)
    {
        _context = context;
    }

    public void SetFramebuffer(Framebuffer framebuffer)
    {
        _framebuffer = framebuffer;
    }

    public void SetCamera(EditorCamera camera)
    {
        _camera = camera;
    }

    public void OnImGuiRender()
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));
        ImGui.Begin("Viewport", flags);
        ImGui.PopStyleVar();

        var viewportSize = ImGui.GetContentRegionAvail();

        if (_framebuffer != null && viewportSize.X > 0 && viewportSize.Y > 0)
        {
            _framebuffer.Resize((uint)viewportSize.X, (uint)viewportSize.Y);
            _camera?.SetViewportSize(viewportSize.X, viewportSize.Y);
            
            ImGui.Image((IntPtr)_framebuffer.ColorAttachment, viewportSize, new Vector2(0, 1), new Vector2(1, 0));
        }
        else
        {
            ImGui.Text("Viewport Placeholder");
        }

        ImGui.End();
    }
}

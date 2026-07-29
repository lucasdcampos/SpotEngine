using ImGuiNET;

namespace Spot.Editor.Panels;

public class ViewportPanel
{
    private readonly EditorContext _context;

    public ViewportPanel(EditorContext context)
    {
        _context = context;
    }

    public void OnImGuiRender()
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        ImGui.Begin("Viewport", flags);
        ImGui.Text("Viewport Placeholder");
        ImGui.End();
    }
}

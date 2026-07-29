using ImGuiNET;

namespace Spot.Editor.Panels;

public class HierarchyPanel
{
    private readonly EditorContext _context;

    public HierarchyPanel(EditorContext context)
    {
        _context = context;
    }

    public void OnImGuiRender()
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        ImGui.Begin("Hierarchy", flags);
        ImGui.Text("Hierarchy Placeholder");
        ImGui.End();
    }
}

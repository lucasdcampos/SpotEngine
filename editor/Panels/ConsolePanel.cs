using ImGuiNET;

namespace Spot.Editor.Panels;

public class ConsolePanel
{
    private readonly EditorContext _context;

    public ConsolePanel(EditorContext context)
    {
        _context = context;
    }

    public void OnImGuiRender()
    {
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
        ImGui.Begin("Console", flags);
        ImGui.Text("Console Placeholder");
        ImGui.End();
    }
}

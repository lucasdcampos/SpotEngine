using ImGuiNET;
using Spot.Engine.Console;

namespace Spot.Editor.Panels;

public class ConsolePanel
{
    private readonly EditorContext _context;

    public ConsolePanel(EditorContext context)
    {
        _context = context;
    }

    public void OnImGuiRender(bool asWindow = true)
    {
        if (asWindow)
        {
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
            ImGui.Begin("Console", flags);
        }
        
        Spot.Engine.Application.Instance.Console.DrawContents(ConsolePresentation.Editor);
        
        if (asWindow)
        {
            ImGui.End();
        }
    }
}

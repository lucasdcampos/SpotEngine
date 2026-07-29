using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Scenes;
using Spot.Editor.Panels;

namespace Spot.Editor;

public class EditorScene : Scene
{
    private readonly EditorContext _context = new();
    
    private readonly HierarchyPanel _hierarchyPanel;
    private readonly InspectorPanel _inspectorPanel;
    private readonly ViewportPanel _viewportPanel;
    private readonly ConsolePanel _consolePanel;

    public EditorScene()
    {
        _hierarchyPanel = new HierarchyPanel(_context);
        _inspectorPanel = new InspectorPanel(_context);
        _viewportPanel = new ViewportPanel(_context);
        _consolePanel = new ConsolePanel(_context);
    }

    public override void OnEnter()
    {
    }

    public override void OnUpdate(float deltaTime)
    {
    }

    public override void OnRender()
    {
    }

    public override void OnImGuiRender()
    {
        DrawMenuBar();

        var viewport = ImGui.GetMainViewport();
        var workPos = viewport.WorkPos;
        var workSize = viewport.WorkSize;

        float hierarchyWidth = 300;
        float inspectorWidth = 300;
        float consoleHeight = 200;

        float middleWidth = workSize.X - hierarchyWidth - inspectorWidth;
        float middleHeight = workSize.Y - consoleHeight;

        ImGui.SetNextWindowPos(new Vector2(workPos.X, workPos.Y));
        ImGui.SetNextWindowSize(new Vector2(hierarchyWidth, middleHeight));
        _hierarchyPanel.OnImGuiRender();

        ImGui.SetNextWindowPos(new Vector2(workPos.X + hierarchyWidth, workPos.Y));
        ImGui.SetNextWindowSize(new Vector2(middleWidth, middleHeight));
        _viewportPanel.OnImGuiRender();

        ImGui.SetNextWindowPos(new Vector2(workPos.X + hierarchyWidth + middleWidth, workPos.Y));
        ImGui.SetNextWindowSize(new Vector2(inspectorWidth, middleHeight));
        _inspectorPanel.OnImGuiRender();

        ImGui.SetNextWindowPos(new Vector2(workPos.X, workPos.Y + middleHeight));
        ImGui.SetNextWindowSize(new Vector2(workSize.X, consoleHeight));
        _consolePanel.OnImGuiRender();
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Exit"))
                {
                    Application.Instance.Quit();
                }
                ImGui.EndMenu();
            }
            ImGui.EndMainMenuBar();
        }
    }
}

using ImGuiNET;
using Spot.Core;
using Spot.Core.Services;
using Spot.Engine.Debug.Panels;
using Spot.Scenes;

namespace Spot.Engine.Debug;

public class RuntimeDebuggerService : IEngineService, ISelectionContext
{
    public Scene? ActiveScene => SceneManager.Current;
    public Entity? Selection { get; set; }
    public string? SelectedAssetPath { get; set; }

    public bool IsOpen => _showHierarchy || _showInspector || _showTime;

    private bool _showHierarchy;
    private bool _showInspector;
    private bool _showTime;
    private readonly HierarchyPanel _hierarchyPanel;
    private readonly InspectorPanel _inspectorPanel;

    public RuntimeDebuggerService()
    {
        _hierarchyPanel = new HierarchyPanel(this);
        _inspectorPanel = new InspectorPanel(this);
    }

    public void Init(Application app) { }
    public void Shutdown() { }
    
    public void Update(float deltaTime)
    {
        // Input capturing is now handled centrally by Application.PollEvents 
        // which checks Application.Instance.Debugger.IsOpen
    }

    public void ImGuiRender()
    {
        if (Application.Instance.Console.IsOpen || IsOpen)
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(viewport.WorkPos.X + viewport.WorkSize.X - 10, viewport.WorkPos.Y + 10), ImGuiCond.Always, new System.Numerics.Vector2(1.0f, 0.0f));
            ImGui.SetNextWindowBgAlpha(0.7f);
            
            if (ImGui.Begin("Debugger Menu", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove))
            {
                if (ImGui.Button("Hierarchy")) _showHierarchy = !_showHierarchy;
                ImGui.SameLine();
                if (ImGui.Button("Inspector")) _showInspector = !_showInspector;
                ImGui.SameLine();
                if (ImGui.Button("Time Controls")) _showTime = !_showTime;
                
                ImGui.End();
            }
        }

        if (_showTime)
        {
            ImGui.Begin("Time Controls", ref _showTime, ImGuiWindowFlags.AlwaysAutoResize);
            
            bool isPaused = Time.TimeScale == 0.0f;
            if (ImGui.Button(isPaused ? "Resume" : "Pause"))
            {
                Time.TimeScale = isPaused ? 1.0f : 0.0f;
            }

            ImGui.SameLine();
            ImGui.BeginDisabled(!isPaused);
            if (ImGui.Button("Step Frame"))
            {
                Time.StepNextFrame = true;
            }
            ImGui.EndDisabled();

            ImGui.End();
        }

        if (_showHierarchy) _hierarchyPanel.OnImGuiRender(ref _showHierarchy);
        if (_showInspector) _inspectorPanel.OnImGuiRender(ref _showInspector);
    }
}

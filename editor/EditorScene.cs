using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Editor.Panels;
using Spot.Editor.Scenes;

namespace Spot.Editor;

public class EditorScene : Scene
{
    private readonly EditorContext _context = new();
    
    private readonly HierarchyPanel _hierarchyPanel;
    private readonly InspectorPanel _inspectorPanel;
    private readonly ViewportPanel _viewportPanel;
    private readonly ConsolePanel _consolePanel;

    private Framebuffer? _framebuffer;
    private readonly EditorCamera _editorCamera = new();

    public EditorScene()
    {
        _hierarchyPanel = new HierarchyPanel(_context);
        _inspectorPanel = new InspectorPanel(_context);
        _viewportPanel = new ViewportPanel(_context);
        _consolePanel = new ConsolePanel(_context);
    }

    public override void OnEnter()
    {
        _framebuffer = new Framebuffer(1280, 720);
        
        var demoScene = new DemoScene();
        demoScene.OnEnter();
        _context.ActiveScene = demoScene;
        
        _viewportPanel.SetFramebuffer(_framebuffer);
        _viewportPanel.SetCamera(_editorCamera);
    }

    public override void OnUpdate(float deltaTime)
    {
        _context.ActiveScene?.OnUpdate(deltaTime);
    }

    public override void OnRender()
    {
        if (_framebuffer == null || _context.ActiveScene == null)
            return;
            
        _framebuffer.Bind();
        Renderer.SetClearColor(0.1f, 0.1f, 0.1f, 1.0f);
        Renderer.Clear();
        
        RenderSystem.Render(_context.ActiveScene, _editorCamera.Camera);
        
        _framebuffer.Unbind();
        
        var window = Application.Instance.Window;
        Renderer.SetViewport(0, 0, (uint)window.Width, (uint)window.Height);
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
    
    public override void OnExit()
    {
        _framebuffer?.Dispose();
        _context.ActiveScene?.OnExit();
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

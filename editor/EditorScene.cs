using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Editor.Panels;
using Spot.Editor.Scenes;
using Spot.Editor.UI;

namespace Spot.Editor;

public enum EditorState
{
    Edit,
    Play
}

public class EditorScene : Scene
{
    private EditorState _state = EditorState.Edit;
    private string? _sceneSnapshot;

    private bool _isCreatingProject = false;
    private bool _showAbout = false;
    private string _newProjectName = "MyProject";
    private string _newProjectLocation = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

    private readonly EditorContext _context = new();
    
    private readonly HierarchyPanel _hierarchyPanel;
    private readonly InspectorPanel _inspectorPanel;
    private readonly ViewportPanel _viewportPanel;
    private readonly ViewportPanel _gamePanel;
    private readonly ConsolePanel _consolePanel;
    private readonly AssetBrowserPanel _assetBrowserPanel;

    private Framebuffer? _framebuffer;
    private Framebuffer? _gameFramebuffer;
    private Framebuffer? _cameraPreviewFramebuffer;
    private readonly EditorCamera _editorCamera = new();

    private string? _currentScenePath;

    // Unsaved-changes tracking. Rather than hooking every mutation site, the current scene is
    // serialized and compared (throttled) against the snapshot captured at the last save/open.
    private string? _savedSnapshot;
    private bool _isSceneDirty = true;
    private int _dirtyCheckCounter;
    private string? _lastWindowTitle;

    public EditorScene()
    {
        _hierarchyPanel = new HierarchyPanel(_context);
        _inspectorPanel = new InspectorPanel(_context);
        _viewportPanel = new ViewportPanel(_context);
        _gamePanel = new ViewportPanel(_context);
        _consolePanel = new ConsolePanel(_context);
        _assetBrowserPanel = new AssetBrowserPanel(_context);

        _hierarchyPanel.OnEntityDoubleClicked += entity =>
        {
            if (entity.HasComponent<Transform>())
            {
                _editorCamera.Focus(entity.GetComponent<Transform>().WorldPosition);
            }
        };
    }

    public override void OnEnter()
    {
        EditorThemeManager.SetTheme(EditorThemes.SpotDark);

        _framebuffer = new Framebuffer(1280, 720);
        _gameFramebuffer = new Framebuffer(1280, 720);
        _cameraPreviewFramebuffer = new Framebuffer(320, 180);

        _viewportPanel.SetFramebuffer(_framebuffer);
        _viewportPanel.SetCameraPreviewFramebuffer(_cameraPreviewFramebuffer);
        _viewportPanel.SetCamera(_editorCamera);

        _gamePanel.SetFramebuffer(_gameFramebuffer);

        LoadStartScene();
    }

    // Loads the active project's start scene (falling back to an empty standalone scene when there
    // is none), so the editor opens on whatever the launcher selected.
    private void LoadStartScene()
    {
        if (Project.Active == null)
        {
            Project.New();
            _context.ActiveScene = new Scene();
            _context.Selection = null;
            _currentScenePath = null;
            return;
        }

        string startAbs = System.IO.Path.Combine(Project.Active.GetAssetDirectory(), Project.Active.Config.StartScene);
        var scene = new Scene();
        if (System.IO.File.Exists(startAbs) && new SceneSerializer(scene).Deserialize(startAbs))
        {
            _context.ActiveScene = scene;
            _currentScenePath = startAbs;
            MarkSceneClean();
        }
        else
        {
            _context.ActiveScene = new Scene();
            _currentScenePath = null;
        }
        _context.Selection = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_context.ActiveScene != null)
        {
            if (_state == EditorState.Edit)
            {
                _context.ActiveScene.OnUpdate(deltaTime);
                _context.ActiveScene.FlushDestroyed();
            }
            else if (_state == EditorState.Play)
            {
                _context.ActiveScene.UpdateRuntime(deltaTime);
            }
        }
    }

    public override void OnRender()
    {
        if (_framebuffer == null || _gameFramebuffer == null || _context.ActiveScene == null)
            return;
            
        // Render Scene View
        _framebuffer.Bind();
        Renderer.SetClearColor(0.1f, 0.1f, 0.1f, 1.0f);
        Renderer.Clear();
        
        if (_editorCamera.Is3D)
            Renderer.SetDepthTest(true);
            
        RenderSystem.Render(_context.ActiveScene, _editorCamera.ViewProjection);
        
        // Draw Axes
        var palette = EditorThemeManager.Current.Palette;
        Renderer2D.BeginScene(_editorCamera.ViewProjection);
        
        float axisThickness;
        if (_editorCamera.Is3D)
        {
            float dist = _editorCamera.Position.Length();
            // Scale thickness based on distance from origin. 
            // A multiplier of 0.005f keeps it visually around 1-2 pixels thick depending on FOV.
            axisThickness = Math.Max(0.01f, dist * 0.005f);
        }
        else
        {
            // In 2D, scale thickness based on zoom level.
            axisThickness = Math.Max(0.01f, _editorCamera.ZoomLevel * 0.005f);
        }
        
        Renderer2D.DrawLine(new Vector3(-1000, 0, 0), new Vector3(1000, 0, 0), palette.AxisX, axisThickness); // X
        Renderer2D.DrawLine(new Vector3(0, -1000, 0), new Vector3(0, 1000, 0), palette.AxisY, axisThickness); // Y
        if (_editorCamera.Is3D)
        {
            Renderer2D.DrawLine(new Vector3(0, 0, -1000), new Vector3(0, 0, 1000), palette.AxisZ, axisThickness); // Z
            
            // Draw 3D Grid
            int gridSize = 100;
            float gridThickness = axisThickness * 0.2f;
            Vector4 gridColor = new Vector4(0.3f, 0.3f, 0.3f, 1.0f);
            
            float camX = MathF.Round(_editorCamera.Position.X);
            float camZ = MathF.Round(_editorCamera.Position.Z);
            
            for (int i = -gridSize; i <= gridSize; i++)
            {
                float z = camZ + i;
                float x = camX + i;
                
                // Lines parallel to X axis
                if (MathF.Abs(z) > 0.01f)
                    Renderer2D.DrawLine(new Vector3(camX - gridSize, 0, z), new Vector3(camX + gridSize, 0, z), gridColor, gridThickness);
                    
                // Lines parallel to Z axis
                if (MathF.Abs(x) > 0.01f)
                    Renderer2D.DrawLine(new Vector3(x, 0, camZ - gridSize), new Vector3(x, 0, camZ + gridSize), gridColor, gridThickness);
            }
        }
        Renderer2D.EndScene();

        if (_editorCamera.Is3D)
            Renderer.SetDepthTest(false);
        
        // Debug Physics Rendering
        if (_context.Selection.HasValue)
        {
            var selectedEntity = _context.Selection.Value;
            if (selectedEntity.HasComponent<Spot.Physics.BoxCollider2DComponent>() && selectedEntity.HasComponent<Transform>())
            {
                Renderer2D.BeginScene(_editorCamera.ViewProjection);
                
                var transform = selectedEntity.GetComponent<Transform>();
                var collider = selectedEntity.GetComponent<Spot.Physics.BoxCollider2DComponent>();
                var bounds = collider.GetWorldBounds(new Vector2(transform.Position.X, transform.Position.Y));
                
                // Draw green hollow box
                Renderer2D.DrawRect(bounds.Center, bounds.HalfExtents * 2.0f, new Vector4(0.0f, 1.0f, 0.0f, 1.0f), 0.02f);
                
                Renderer2D.EndScene();
            }
        }
        
        _framebuffer.Unbind();
        
        // Render Game View
        _gameFramebuffer.Bind();
        
        System.Numerics.Matrix4x4? viewProjection = null;
        Vector4 clearColor = new Vector4(0.1f, 0.1f, 0.1f, 1.0f);
        bool is3D = false;
        
        foreach (var entity in _context.ActiveScene.View<CameraComponent>())
        {
            var cc = entity.GetComponent<CameraComponent>();
            if (cc.Primary)
            {
                if (entity.HasComponent<Transform>())
                {
                    var transform = entity.GetComponent<Transform>();
                    viewProjection = cc.GetViewProjection(transform);
                    is3D = cc.ProjectionType == SceneCameraProjection.Perspective;
                }
                clearColor = cc.BackgroundColor;
                break;
            }
        }
        
        Renderer.SetClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
        Renderer.Clear();

        if (viewProjection.HasValue)
        {
            if (is3D)
                Renderer.SetDepthTest(true);
                
            RenderSystem.Render(_context.ActiveScene, viewProjection.Value);
            
            if (is3D)
                Renderer.SetDepthTest(false);
        }
        
        _gameFramebuffer.Unbind();

        // Render Camera Preview
        if (_cameraPreviewFramebuffer != null && _context.Selection.HasValue && _context.Selection.Value.HasComponent<CameraComponent>())
        {
            _cameraPreviewFramebuffer.Bind();
            var entity = _context.Selection.Value;
            var cc = entity.GetComponent<CameraComponent>();
            if (entity.HasComponent<Transform>())
            {
                var transform = entity.GetComponent<Transform>();
                var viewProj = cc.GetViewProjection(transform);
                var is3DPrev = cc.ProjectionType == SceneCameraProjection.Perspective;
                
                Renderer.SetClearColor(cc.BackgroundColor.X, cc.BackgroundColor.Y, cc.BackgroundColor.Z, cc.BackgroundColor.W);
                Renderer.Clear();
                
                if (is3DPrev)
                    Renderer.SetDepthTest(true);
                    
                RenderSystem.Render(_context.ActiveScene, viewProj);
                
                if (is3DPrev)
                    Renderer.SetDepthTest(false);
            }
            _cameraPreviewFramebuffer.Unbind();
        }

        var window = Spot.Core.Application.Instance.Window;
        Renderer.SetViewport(0, 0, (uint)window.Width, (uint)window.Height);
        Renderer.SetClearColor(0.1f, 0.1f, 0.1f, 1.0f);
    }

    public override void OnImGuiRender()
    {
        HandleShortcuts();

        DrawMenuBar();

        var viewport = ImGui.GetMainViewport();
        var workPos = viewport.WorkPos;
        var workSize = viewport.WorkSize;

        // The play/stop control now lives in the main menu bar (see DrawMenuBar), so the panels
        // start right below it with no separate toolbar strip.
        var mainPos = workPos;
        float hierarchyWidth = 300;
        float inspectorWidth = 300;
        float consoleHeight = 200;

        float middleWidth = workSize.X - hierarchyWidth - inspectorWidth;
        float middleHeight = workSize.Y - consoleHeight;

        ImGui.SetNextWindowPos(new Vector2(mainPos.X, mainPos.Y));
        ImGui.SetNextWindowSize(new Vector2(hierarchyWidth, middleHeight));
        _hierarchyPanel.OnImGuiRender();

        ImGui.SetNextWindowPos(new Vector2(mainPos.X + hierarchyWidth, mainPos.Y));
        ImGui.SetNextWindowSize(new Vector2(middleWidth, middleHeight));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));
        ImGui.Begin("Viewports", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar);
        ImGui.PopStyleVar();

        if (ImGui.BeginTabBar("ViewportTabs"))
        {
            if (ImGui.BeginTabItem("Scene"))
            {
                _viewportPanel.OnImGuiRender(handleInput: true);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Game"))
            {
                var size = ImGui.GetContentRegionAvail();
                if (size.X > 0 && size.Y > 0 && _context.ActiveScene != null)
                {
                    foreach (var entity in _context.ActiveScene.View<CameraComponent>())
                    {
                        var cc = entity.GetComponent<CameraComponent>();
                        if (cc.Primary)
                        {
                            cc.SetViewportSize(size.X, size.Y);
                            break;
                        }
                    }
                }
                
                _gamePanel.OnImGuiRender(handleInput: false);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.End();

        ImGui.SetNextWindowPos(new Vector2(mainPos.X + hierarchyWidth + middleWidth, mainPos.Y));
        ImGui.SetNextWindowSize(new Vector2(inspectorWidth, middleHeight));
        _inspectorPanel.OnImGuiRender();

        ImGui.SetNextWindowPos(new Vector2(mainPos.X, mainPos.Y + middleHeight));
        ImGui.SetNextWindowSize(new Vector2(workSize.X, consoleHeight));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));
        ImGui.Begin("BottomPanels", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar);
        ImGui.PopStyleVar();

        if (ImGui.BeginTabBar("BottomTabs"))
        {
            if (ImGui.BeginTabItem("Console"))
            {
                _consolePanel.OnImGuiRender(asWindow: false);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Asset Browser"))
            {
                _assetBrowserPanel.OnImGuiRender(asWindow: false);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.End();

        if (_isCreatingProject)
        {
            ImGui.OpenPopup("Create New Project");
        }

        bool modalOpen = true;
        if (ImGui.BeginPopupModal("Create New Project", ref modalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("Project Name", ref _newProjectName, 128);
            
            ImGui.InputText("Location", ref _newProjectLocation, 256);
            ImGui.SameLine();
            if (ImGui.Button("...##Location"))
            {
                string? folder = Spot.Editor.Utils.FileDialogs.SelectFolder();
                if (folder != null)
                {
                    _newProjectLocation = folder;
                }
            }
            
            if (ImGui.Button("Create", new Vector2(120, 0)))
            {
                CreateProject(_newProjectName, _newProjectLocation);
                _isCreatingProject = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                _isCreatingProject = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        else if (!modalOpen)
        {
            _isCreatingProject = false;
        }

        if (_showAbout)
        {
            ImGui.OpenPopup("About Spot Editor");
        }

        bool aboutOpen = true;
        if (ImGui.BeginPopupModal("About Spot Editor", ref aboutOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Spot Editor");
            ImGui.TextDisabled("A lightweight 2D/3D game engine.");
            ImGui.Separator();
            if (ImGui.Button("Close", new Vector2(120, 0)))
            {
                _showAbout = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        else if (!aboutOpen)
        {
            _showAbout = false;
        }

        UpdateSceneStatus();
    }

    private void CreateProject(string name, string location)
    {
        string sptprojPath = ProjectFactory.Create(name, location);
        Spot.Editor.Utils.RecentProjects.Add(sptprojPath);

        _context.ActiveScene = new Scene();
        _context.Selection = null;
        _currentScenePath = null;
    }

    public override void OnExit()
    {
        _framebuffer?.Dispose();
        _gameFramebuffer?.Dispose();
        _context.ActiveScene?.OnExit();
    }

    private void OnPlay()
    {
        if (_state == EditorState.Edit && _context.ActiveScene != null)
        {
            _sceneSnapshot = new SceneSerializer(_context.ActiveScene).SerializeToString();
            var playScene = new Scene();
            new SceneSerializer(playScene).DeserializeFromString(_sceneSnapshot);
            _context.ActiveScene = playScene;
            _state = EditorState.Play;
        }
    }

    private void OnStop()
    {
        if (_state == EditorState.Play && _sceneSnapshot != null)
        {
            var editScene = new Scene();
            new SceneSerializer(editScene).DeserializeFromString(_sceneSnapshot);
            _context.ActiveScene = editScene;
            _state = EditorState.Edit;
            _sceneSnapshot = null;
        }
    }

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
        {
            return;
        }

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Open Scene...")) OpenScene();
            if (ImGui.MenuItem("Save Scene", "Ctrl+S")) SaveScene();
            ImGui.Separator();
            if (ImGui.MenuItem("Exit")) Spot.Core.Application.Instance.Quit();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Project"))
        {
            if (ImGui.MenuItem("New Project...")) _isCreatingProject = true;
            if (ImGui.MenuItem("Open Project...")) OpenProject();
            if (Project.Active != null)
            {
                ImGui.Separator();
                if (ImGui.BeginMenu("Regenerate Project Files"))
                {
                    if (ImGui.MenuItem("Update Build Files (.csproj, DLLs)")) Project.GenerateCSProject(overwriteProgram: false);
                    if (ImGui.MenuItem("Full Reset (Includes Program.cs)")) Project.GenerateCSProject(overwriteProgram: true);
                    ImGui.EndMenu();
                }
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Entity"))
        {
            bool hasScene = _context.ActiveScene != null;
            if (ImGui.MenuItem("Create Empty", "", false, hasScene)) _hierarchyPanel.CreateEmpty();
            if (ImGui.MenuItem("Create Camera", "", false, hasScene)) _hierarchyPanel.CreateCamera();
            if (ImGui.MenuItem("Create Sprite", "", false, hasScene)) _hierarchyPanel.CreateSprite();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            if (ImGui.BeginMenu("Theme"))
            {
                foreach (var theme in EditorThemes.All)
                {
                    bool selected = ReferenceEquals(EditorThemeManager.Current, theme);
                    if (ImGui.MenuItem(theme.Name, "", selected)) EditorThemeManager.SetTheme(theme);
                }
                ImGui.EndMenu();
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"))
        {
            if (ImGui.MenuItem("About")) _showAbout = true;
            ImGui.EndMenu();
        }

        DrawPlayControl();

        ImGui.EndMainMenuBar();
    }

    // Draws the centered play/stop icon button inside the main menu bar.
    private void DrawPlayControl()
    {
        var palette = EditorThemeManager.Current.Palette;
        float size = ImGui.GetFrameHeight();

        // Center the control horizontally in the menu bar (unless the menus already reach past it).
        float centerX = (ImGui.GetWindowWidth() - size) * 0.5f;
        if (centerX > ImGui.GetCursorPosX())
        {
            ImGui.SetCursorPosX(centerX);
        }

        var drawList = ImGui.GetWindowDrawList();
        Vector2 p0 = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##playstop", new Vector2(size, size));
        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

        if (hovered)
        {
            drawList.AddRectFilled(p0, p0 + new Vector2(size, size), ImGui.GetColorU32(palette.FrameBgHovered), 4.0f);
        }

        float pad = size * 0.30f;
        if (_state == EditorState.Edit)
        {
            // Play: right-pointing triangle.
            uint color = ImGui.GetColorU32(palette.Accent);
            Vector2 a = p0 + new Vector2(pad, pad);
            Vector2 b = p0 + new Vector2(pad, size - pad);
            Vector2 c = p0 + new Vector2(size - pad, size * 0.5f);
            drawList.AddTriangleFilled(a, b, c, color);
            if (clicked) OnPlay();
        }
        else
        {
            // Stop: filled square.
            uint color = ImGui.GetColorU32(palette.LogError);
            drawList.AddRectFilled(p0 + new Vector2(pad, pad), p0 + new Vector2(size - pad, size - pad), color, 2.0f);
            if (clicked) OnStop();
        }

        if (hovered)
        {
            ImGui.SetTooltip(_state == EditorState.Edit ? "Play" : "Stop");
        }
    }

    // Keyboard shortcuts handled once per frame (editor/edit mode only).
    private void HandleShortcuts()
    {
        var io = ImGui.GetIO();
        if (_state == EditorState.Edit && io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S, repeat: false))
        {
            SaveScene();
        }
    }

    // Records the current scene as the clean baseline (called after a successful save/open).
    private void MarkSceneClean()
    {
        _savedSnapshot = _context.ActiveScene != null
            ? new SceneSerializer(_context.ActiveScene).SerializeToString()
            : null;
        _isSceneDirty = false;
        _dirtyCheckCounter = 0;
    }

    // Refreshes the unsaved-changes flag and reflects it (with a '*') in the window title.
    private void UpdateSceneStatus()
    {
        if (_state == EditorState.Edit)
        {
            if (_context.ActiveScene == null)
            {
                _isSceneDirty = false;
            }
            else if (_currentScenePath == null)
            {
                _isSceneDirty = true; // never saved to a file yet
            }
            else if (++_dirtyCheckCounter >= 15)
            {
                _dirtyCheckCounter = 0;
                string current = new SceneSerializer(_context.ActiveScene).SerializeToString();
                _isSceneDirty = _savedSnapshot == null || current != _savedSnapshot;
            }
        }

        string sceneName = _currentScenePath != null
            ? System.IO.Path.GetFileNameWithoutExtension(_currentScenePath)
            : "Untitled";
        string title = _state == EditorState.Play
            ? $"{sceneName} (Playing) - Spot.Editor"
            : $"{sceneName}{(_isSceneDirty ? "*" : "")} - Spot.Editor";

        if (title != _lastWindowTitle)
        {
            _lastWindowTitle = title;
            Spot.Core.Application.Instance.Window.NativeWindow.Title = title;
        }
    }

    private void OpenScene()
    {
        string initialDir = Project.Active != null ? Project.Active.GetAssetDirectory() : "";
        string? filepath = Spot.Editor.Utils.FileDialogs.OpenFile("Spot Scene (*.sptscene)|*.sptscene", initialDir);
        if (filepath != null)
        {
            var newScene = new Scene();
            var serializer = new SceneSerializer(newScene);
            if (serializer.Deserialize(filepath))
            {
                _context.ActiveScene = newScene;
                _context.Selection = null;
                _currentScenePath = filepath;
                MarkSceneClean();
            }
        }
    }

    private void SaveScene()
    {
        if (_context.ActiveScene == null) return;

        // No file backing this scene yet: prompt the user to create one.
        if (_currentScenePath == null)
        {
            string initialDir = Project.Active != null ? Project.Active.GetAssetDirectory() : "";
            _currentScenePath = Spot.Editor.Utils.FileDialogs.SaveFile("Spot Scene (*.sptscene)|*.sptscene", "sptscene", initialDir);
            if (_currentScenePath == null) return; // user cancelled the dialog
        }

        new SceneSerializer(_context.ActiveScene).Serialize(_currentScenePath);
        EnsureStartScene(_currentScenePath);
        MarkSceneClean();
    }

    // Promotes the just-saved scene to the project's start scene when none is defined yet or the
    // configured one is missing. The project config is persisted automatically (there is no manual
    // "Save Project" action).
    private void EnsureStartScene(string sceneAbsolutePath)
    {
        var project = Project.Active;
        if (project == null || string.IsNullOrEmpty(project.ProjectDirectory)) return;

        string assetDir = project.GetAssetDirectory();
        string configuredAbs = System.IO.Path.Combine(assetDir, project.Config.StartScene);
        bool needsStartScene = string.IsNullOrEmpty(project.Config.StartScene) || !System.IO.File.Exists(configuredAbs);
        if (!needsStartScene) return;

        project.Config.StartScene = System.IO.Path.GetRelativePath(assetDir, sceneAbsolutePath).Replace('\\', '/');
        Project.SaveActive(System.IO.Path.Combine(project.ProjectDirectory, project.Config.Name + ".sptproj"));
        Spot.Core.Log.Info("Start scene set to '{0}'", project.Config.StartScene);
    }

    private void OpenProject()
    {
        string? filepath = Spot.Editor.Utils.FileDialogs.OpenFile("Spot Project (*.sptproj)|*.sptproj");
        if (filepath == null || Project.Load(filepath) == null || Project.Active == null)
        {
            return;
        }

        Spot.Editor.Utils.RecentProjects.Add(filepath);

        string startSceneAbs = System.IO.Path.Combine(Project.Active.GetAssetDirectory(), Project.Active.Config.StartScene);
        var newScene = new Scene();
        if (System.IO.File.Exists(startSceneAbs) && new SceneSerializer(newScene).Deserialize(startSceneAbs))
        {
            _context.ActiveScene = newScene;
            _currentScenePath = startSceneAbs;
        }
        else
        {
            _context.ActiveScene = new Scene();
            _currentScenePath = null;
        }
        _context.Selection = null;
    }

}

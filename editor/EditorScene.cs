using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Editor.Panels;
using Spot.Editor.Scenes;
using Spot.Editor.UI;

namespace Spot.Editor;


public class OpenSceneData
{
    public Scene Scene = new();
    public string? FilePath;
    public string? SavedSnapshot;
    public bool IsDirty = true;
    public int DirtyCheckCounter = 0;
    public ViewportPanel ViewportPanel;
    public Framebuffer Framebuffer;
    public Framebuffer CameraPreviewFramebuffer;
    public EditorCamera EditorCamera = new();
    public bool IsOpen = true;
    public bool FocusNextFrame = false;
    public bool FirstFrame = true;

    public OpenSceneData(EditorContext context)
    {
        ViewportPanel = new ViewportPanel(context);
        Framebuffer = new Framebuffer(1280, 720);
        CameraPreviewFramebuffer = new Framebuffer(320, 180);
        ViewportPanel.SetFramebuffer(Framebuffer);
        ViewportPanel.SetCameraPreviewFramebuffer(CameraPreviewFramebuffer);
        ViewportPanel.SetCamera(EditorCamera);
    }

    public void Dispose()
    {
        Framebuffer.Dispose();
        CameraPreviewFramebuffer.Dispose();
    }
}

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
    private readonly ViewportPanel _gamePanel;
    private readonly ConsolePanel _consolePanel;
    private readonly AssetBrowserPanel _assetBrowserPanel;

    private Framebuffer? _gameFramebuffer;

    private List<OpenSceneData> _openScenes = new();
    private OpenSceneData? _activeSceneData = null;
    private OpenSceneData? _lastEditedSceneData = null;

    private string? _lastWindowTitle;

    // Per-panel visibility, toggled from View > Panels and by each window's close button.
    private bool _showGame = true;
    private bool _showHierarchy = true;
    private bool _showInspector = true;
    private bool _showConsole = true;
    private bool _showAssetBrowser = true;

    // When set, the default docked layout is rebuilt on the next frame (first launch / Reset Layout).
    private bool _rebuildDefaultLayout = !System.IO.File.Exists("imgui.ini");

    public EditorScene()
    {
        _hierarchyPanel = new HierarchyPanel(_context);
        _inspectorPanel = new InspectorPanel(_context);
        
        _gamePanel = new ViewportPanel(_context);
        _consolePanel = new ConsolePanel(_context);
        _assetBrowserPanel = new AssetBrowserPanel(_context);
        _assetBrowserPanel.OnAssetOpened += OpenSceneAsset;

        _hierarchyPanel.OnEntityDoubleClicked += entity =>
        {
            if (entity.HasComponent<Transform>() && _activeSceneData != null)
            {
                _activeSceneData.EditorCamera.Focus(entity.GetComponent<Transform>().WorldPosition);
            }
        };
    }

    public override void OnEnter()
    {
        EditorThemeManager.SetTheme(EditorThemes.SpotDark);
        Spot.Editor.Utils.EditorSettings.LoadAndApply(Spot.Core.Application.Instance.Window.NativeWindow);
        ImGui.LoadIniSettingsFromDisk("imgui.ini");

        _gameFramebuffer = new Framebuffer(1280, 720);
        _gamePanel.SetFramebuffer(_gameFramebuffer);

        LoadStartScene();
    }

    // Loads the active project's start scene (falling back to an empty standalone scene when there
    // is none), so the editor opens on whatever the launcher selected.
    private void OpenSceneAsset(string filepath)
    {
        string normPath = System.IO.Path.GetFullPath(filepath).ToLowerInvariant();
        var existing = _openScenes.FirstOrDefault(s => s.FilePath != null && System.IO.Path.GetFullPath(s.FilePath).ToLowerInvariant() == normPath);
        if (existing != null)
        {
            existing.FocusNextFrame = true;
            _activeSceneData = existing;
            _lastEditedSceneData = existing;
            _context.ActiveScene = existing.Scene;
            return;
        }

        var newSceneData = new OpenSceneData(_context);
        var serializer = new SceneSerializer(newSceneData.Scene);
        if (serializer.Deserialize(filepath))
        {
            newSceneData.FilePath = filepath;
            newSceneData.SavedSnapshot = new SceneSerializer(newSceneData.Scene).SerializeToString();
            newSceneData.IsDirty = false;
            _openScenes.Add(newSceneData);
            _activeSceneData = newSceneData;
            _lastEditedSceneData = newSceneData;
            _context.ActiveScene = newSceneData.Scene;
            _context.Selection = null;
        }
    }

    private void LoadStartScene()
    {
        _openScenes.Clear();
        _activeSceneData = null;
        _lastEditedSceneData = null;
        _context.ActiveScene = null;
        _context.Selection = null;

        if (Project.Active == null)
        {
            Project.New();
            var newSceneData = new OpenSceneData(_context);
            _openScenes.Add(newSceneData);
            _activeSceneData = newSceneData;
            _lastEditedSceneData = newSceneData;
            _context.ActiveScene = newSceneData.Scene;
            return;
        }

        string startAbs = System.IO.Path.Combine(Project.Active.GetAssetDirectory(), Project.Active.Config.StartScene);
        if (System.IO.File.Exists(startAbs))
        {
            OpenSceneAsset(startAbs);
        }
        else
        {
            var newSceneData = new OpenSceneData(_context);
            _openScenes.Add(newSceneData);
            _activeSceneData = newSceneData;
            _lastEditedSceneData = newSceneData;
            _context.ActiveScene = newSceneData.Scene;
        }
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_state == EditorState.Play && _context.ActiveScene != null)
        {
            _context.ActiveScene.UpdateRuntime(deltaTime);
        }
        else if (_state == EditorState.Edit)
        {
            foreach (var sceneData in _openScenes)
            {
                sceneData.Scene.OnUpdate(deltaTime);
                sceneData.Scene.FlushDestroyed();
            }
        }
    }

    public override void OnRender()
    {
        if (_gameFramebuffer == null)
            return;
            
        // Render Scene Views
        foreach (var sceneData in _openScenes)
        {
            if (!sceneData.IsOpen) continue;
            
            sceneData.Framebuffer.Bind();
            Renderer.SetClearColor(0.1f, 0.1f, 0.1f, 1.0f);
            Renderer.Clear();
            
            if (sceneData.EditorCamera.Is3D)
                Renderer.SetDepthTest(true);
                
            RenderSystem.Render(sceneData.Scene, sceneData.EditorCamera.ViewProjection);
            
            // Draw Axes
            var palette = EditorThemeManager.Current.Palette;
            Renderer2D.BeginScene(sceneData.EditorCamera.ViewProjection);
            
            float axisThickness;
            if (sceneData.EditorCamera.Is3D)
            {
                float dist = sceneData.EditorCamera.Position.Length();
                axisThickness = Math.Max(0.01f, dist * 0.005f);
            }
            else
            {
                axisThickness = Math.Max(0.01f, sceneData.EditorCamera.ZoomLevel * 0.005f);
            }
            
            Renderer2D.DrawLine(new Vector3(-1000, 0, 0), new Vector3(1000, 0, 0), palette.AxisX, axisThickness);
            Renderer2D.DrawLine(new Vector3(0, -1000, 0), new Vector3(0, 1000, 0), palette.AxisY, axisThickness);
            if (sceneData.EditorCamera.Is3D)
            {
                Renderer2D.DrawLine(new Vector3(0, 0, -1000), new Vector3(0, 0, 1000), palette.AxisZ, axisThickness);
                
                int gridSize = 100;
                float gridThickness = axisThickness * 0.2f;
                Vector4 gridColor = new Vector4(0.3f, 0.3f, 0.3f, 1.0f);
                
                float camX = MathF.Round(sceneData.EditorCamera.Position.X);
                float camZ = MathF.Round(sceneData.EditorCamera.Position.Z);
                
                for (int i = -gridSize; i <= gridSize; i++)
                {
                    float z = camZ + i;
                    float x = camX + i;
                    if (MathF.Abs(z) > 0.01f) Renderer2D.DrawLine(new Vector3(camX - gridSize, 0, z), new Vector3(camX + gridSize, 0, z), gridColor, gridThickness);
                    if (MathF.Abs(x) > 0.01f) Renderer2D.DrawLine(new Vector3(x, 0, camZ - gridSize), new Vector3(x, 0, camZ + gridSize), gridColor, gridThickness);
                }
            }
            Renderer2D.EndScene();

            if (sceneData.EditorCamera.Is3D)
                Renderer.SetDepthTest(false);
            
            // Debug Physics Rendering
            if (_context.Selection.HasValue && sceneData == _activeSceneData)
            {
                var selectedEntity = _context.Selection.Value;
                if (selectedEntity.HasComponent<Spot.Physics.BoxCollider2DComponent>() && selectedEntity.HasComponent<Transform>())
                {
                    Renderer2D.BeginScene(sceneData.EditorCamera.ViewProjection);
                    var transform = selectedEntity.GetComponent<Transform>();
                    var collider = selectedEntity.GetComponent<Spot.Physics.BoxCollider2DComponent>();
                    var bounds = collider.GetWorldBounds(new Vector2(transform.Position.X, transform.Position.Y));
                    Renderer2D.DrawRect(bounds.Center, bounds.HalfExtents * 2.0f, new Vector4(0.0f, 1.0f, 0.0f, 1.0f), 0.02f);
                    Renderer2D.EndScene();
                }
            }
            
            sceneData.Framebuffer.Unbind();
            
            // Render Camera Preview
            if (_context.Selection.HasValue && _context.Selection.Value.HasComponent<CameraComponent>() && sceneData == _activeSceneData)
            {
                sceneData.CameraPreviewFramebuffer.Bind();
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
                        
                    RenderSystem.Render(sceneData.Scene, viewProj);
                    
                    if (is3DPrev)
                        Renderer.SetDepthTest(false);
                }
                sceneData.CameraPreviewFramebuffer.Unbind();
            }
        }
        
        // Render Game View
        _gameFramebuffer.Bind();
        Renderer.SetClearColor(0.1f, 0.1f, 0.1f, 1.0f);
        Renderer.Clear();
        
        var gameScene = _state == EditorState.Play ? _context.ActiveScene : _lastEditedSceneData?.Scene;
        
        if (gameScene != null)
        {
            System.Numerics.Matrix4x4? viewProjection = null;
            Vector4 clearColor = new Vector4(0.1f, 0.1f, 0.1f, 1.0f);
            bool is3D = false;
            
            foreach (var entity in gameScene.View<CameraComponent>())
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
                    
                RenderSystem.Render(gameScene, viewProjection.Value);
                
                if (is3D)
                    Renderer.SetDepthTest(false);
            }
        }
        
        _gameFramebuffer.Unbind();

        var window = Spot.Core.Application.Instance.Window;
        Renderer.SetViewport(0, 0, (uint)window.Width, (uint)window.Height);
        Renderer.SetClearColor(0.1f, 0.1f, 0.1f, 1.0f);
    }
    public override void OnImGuiRender()
    {
        HandleShortcuts();

        DrawMenuBar();

        // Full-viewport dockspace so every editor panel can be docked, resized and rearranged.
        // The resulting layout is persisted across runs via imgui.ini.
        uint dockspaceId = ImGui.DockSpaceOverViewport();

        // First launch (no imgui.ini) or an explicit Reset Layout: arrange the panels into the
        // default docked layout. DockBuilder must run after the dockspace is submitted this frame.
        if (_rebuildDefaultLayout)
        {
            _rebuildDefaultLayout = false;
            BuildDefaultLayout(dockspaceId, ImGui.GetMainViewport().WorkSize);
        }

        // Each panel is now an independent dockable window: closable via its title-bar 'x' and
        // reopenable from View > Panels. The 'ref' visibility flag also drives the close button.
        if (_showHierarchy)
        {
            _hierarchyPanel.OnImGuiRender(ref _showHierarchy);
        }

        for (int i = 0; i < _openScenes.Count; i++)
        {
            var sceneData = _openScenes[i];
            if (!sceneData.IsOpen) continue;

            string sceneName = sceneData.FilePath != null
                ? System.IO.Path.GetFileNameWithoutExtension(sceneData.FilePath)
                : "Untitled";
            
            // Generate unique title but nice display name
            string stableId = sceneData.FilePath != null ? sceneData.FilePath : $"Untitled_{i}";
            string title = $"{sceneName}{(sceneData.IsDirty ? "*" : "")}###Scene_{stableId}";

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));
            
            if (sceneData.FocusNextFrame)
            {
                ImGui.SetNextWindowFocus();
                sceneData.FocusNextFrame = false;
            }
            if (sceneData.FirstFrame)
            {
                ImGui.SetNextWindowDockID(dockspaceId, ImGuiCond.FirstUseEver);
                sceneData.FirstFrame = false;
            }

            bool open = ImGui.Begin(title, ref sceneData.IsOpen, ImGuiWindowFlags.NoCollapse);
            ImGui.PopStyleVar();
            
            bool isFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows | ImGuiFocusedFlags.RootWindow);
            bool isHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.RootWindow);
            
            if (isHovered && (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsMouseClicked(ImGuiMouseButton.Middle)))
            {
                ImGui.SetWindowFocus();
                isFocused = true;
            }

            if (isFocused || _activeSceneData == null)
            {
                _activeSceneData = sceneData;
                _context.ActiveScene = sceneData.Scene;
                _lastEditedSceneData = sceneData;
            }

            if (open)
            {
                sceneData.ViewportPanel.OnImGuiRender(handleInput: isFocused || isHovered);
            }
            ImGui.End();
        }
        
        // Remove closed scenes
        _openScenes.RemoveAll(s => !s.IsOpen);
        if (!_openScenes.Contains(_activeSceneData))
        {
            _activeSceneData = _openScenes.Count > 0 ? _openScenes[0] : null;
            _context.ActiveScene = _activeSceneData?.Scene;
            if (_activeSceneData != null) _lastEditedSceneData = _activeSceneData;
        }

        if (_showGame)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));
            bool open = ImGui.Begin("Game", ref _showGame, ImGuiWindowFlags.NoCollapse);
            ImGui.PopStyleVar();
            if (open)
            {
                var size = ImGui.GetContentRegionAvail();
                var gameScene = _state == EditorState.Play ? _context.ActiveScene : _lastEditedSceneData?.Scene;
                if (size.X > 0 && size.Y > 0 && gameScene != null)
                {
                    foreach (var entity in gameScene.View<CameraComponent>())
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
            }
            ImGui.End();
        }

        if (_showInspector)
        {
            _inspectorPanel.OnImGuiRender(ref _showInspector);
        }

        if (_showConsole)
        {
            bool open = ImGui.Begin("Console", ref _showConsole, ImGuiWindowFlags.NoCollapse);
            if (open)
            {
                _consolePanel.OnImGuiRender(asWindow: false);
            }
            ImGui.End();
        }

        if (_showAssetBrowser)
        {
            bool open = ImGui.Begin("Asset Browser", ref _showAssetBrowser, ImGuiWindowFlags.NoCollapse);
            if (open)
            {
                _assetBrowserPanel.OnImGuiRender(asWindow: false);
            }
            ImGui.End();
        }

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

        var lastLine = Spot.Core.Application.Instance.Console.LastLine;
        if (lastLine != null)
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(viewport.WorkPos.X + 10, viewport.WorkPos.Y + viewport.WorkSize.Y - 30));
            ImGui.Begin("StatusOverlay", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoInputs);
            ImGui.TextColored(lastLine.Value.Color, lastLine.Value.Text);
            ImGui.End();
        }
    }

    private void CreateProject(string name, string location)
    {
        string sptprojPath = ProjectFactory.Create(name, location);
        Spot.Editor.Utils.RecentProjects.Add(sptprojPath);
        
        _openScenes.Clear();
        var newSceneData = new OpenSceneData(_context);
        _openScenes.Add(newSceneData);
        _activeSceneData = newSceneData;
        _lastEditedSceneData = newSceneData;
        _context.ActiveScene = newSceneData.Scene;
        _context.Selection = null;
    }

    private void BuildProject()
    {
        if (Project.Active == null || string.IsNullOrEmpty(Project.Active.ProjectDirectory)) return;
        
        Spot.Core.Log.Info("Starting build process...");
        Project.GenerateCSProject();
        string buildDir = System.IO.Path.Combine(Project.Active.ProjectDirectory, "Build");
        
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                string csprojFile = $"{Project.Active.Config.Name}.csproj";
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"publish \"{csprojFile}\" -c Release -o \"{buildDir}\"",
                    WorkingDirectory = Project.Active.ProjectDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                processInfo.RedirectStandardOutput = true;
                processInfo.RedirectStandardError = true;

                var process = new System.Diagnostics.Process { StartInfo = processInfo };
                process.OutputDataReceived += (sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Spot.Core.Log.Info(e.Data); };
                process.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Spot.Core.Log.Error(e.Data); };

                if (process.Start())
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        Spot.Core.Log.Info("Build completed successfully!");
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{buildDir}\"");
                    }
                    else
                    {
                        Spot.Core.Log.Error($"Build failed with exit code {process.ExitCode}. See above for details.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Spot.Core.Log.Error($"Failed to build project: {ex.Message}");
            }
        });
    }

    public override void OnExit()
    {
        Spot.Editor.Utils.EditorSettings.Save(Spot.Core.Application.Instance.Window.NativeWindow);

        foreach (var sceneData in _openScenes) sceneData.Dispose();
        _gameFramebuffer?.Dispose();
        _context.ActiveScene?.OnExit();
    }

    private void OnPlay()
    {
        if (_state == EditorState.Edit && _activeSceneData != null)
        {
            _sceneSnapshot = new SceneSerializer(_activeSceneData.Scene).SerializeToString();
            var playScene = new Scene();
            new SceneSerializer(playScene).DeserializeFromString(_sceneSnapshot);
            _context.ActiveScene = playScene;
            _state = EditorState.Play;
        }
    }

    private void OnStop()
    {
        if (_state == EditorState.Play && _sceneSnapshot != null && _activeSceneData != null)
        {
            var editScene = new Scene();
            new SceneSerializer(editScene).DeserializeFromString(_sceneSnapshot);
            _activeSceneData.Scene = editScene;
            _context.ActiveScene = editScene;
            _state = EditorState.Edit;
            _sceneSnapshot = null;
        }
    }

    // Rebuilds the default docked arrangement: Hierarchy on the left, Inspector on the right,
    // Console/Asset Browser tabbed along the bottom, and the Scene/Game viewports in the center.
    private void BuildDefaultLayout(uint dockspaceId, Vector2 size)
    {
        ImGuiDock.igDockBuilderRemoveNode(dockspaceId);
        ImGuiDock.igDockBuilderAddNode(dockspaceId, ImGuiDock.DockNodeFlagsDockSpace);
        ImGuiDock.igDockBuilderSetNodeSize(dockspaceId, size);

        uint center = dockspaceId;
        ImGuiDock.igDockBuilderSplitNode(center, ImGuiDir.Left, 0.20f, out uint left, out center);
        ImGuiDock.igDockBuilderSplitNode(left, ImGuiDir.Down, 0.40f, out uint leftBottom, out uint leftTop);
        ImGuiDock.igDockBuilderSplitNode(center, ImGuiDir.Right, 0.25f, out uint right, out center);
        ImGuiDock.igDockBuilderSplitNode(center, ImGuiDir.Down, 0.25f, out uint bottom, out center);

        ImGuiDock.igDockBuilderDockWindow("Hierarchy", leftTop);
        ImGuiDock.igDockBuilderDockWindow("Asset Browser", leftBottom);
        ImGuiDock.igDockBuilderDockWindow("Inspector", right);
        ImGuiDock.igDockBuilderDockWindow("Console", bottom);
        
        foreach (var sceneData in _openScenes)
        {
            string sceneName = sceneData.FilePath != null
                ? System.IO.Path.GetFileNameWithoutExtension(sceneData.FilePath)
                : "Untitled";
            string title = $"{sceneName}{(sceneData.IsDirty ? "*" : "")}###Scene_{sceneData.GetHashCode()}";
            ImGuiDock.igDockBuilderDockWindow(title, center);
        }

        ImGuiDock.igDockBuilderDockWindow("Game", center);

        ImGuiDock.igDockBuilderFinish(dockspaceId);
    }

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
        {
            return;
        }

        if (ImGui.BeginMenu("Scene"))
        {
            if (ImGui.MenuItem("Open Scene...")) OpenScene();
            if (ImGui.MenuItem("Save Scene", "Ctrl+S")) SaveScene();
            if (ImGui.MenuItem("Save All Scenes", "Ctrl+Shift+S")) SaveAllScenes();
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
                if (ImGui.MenuItem("Build Game (Release)")) BuildProject();
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
            if (ImGui.BeginMenu("Panels"))
            {
                ImGui.MenuItem("Game", "", ref _showGame);
                ImGui.MenuItem("Hierarchy", "", ref _showHierarchy);
                ImGui.MenuItem("Inspector", "", ref _showInspector);
                ImGui.MenuItem("Console", "", ref _showConsole);
                ImGui.MenuItem("Asset Browser", "", ref _showAssetBrowser);
                ImGui.EndMenu();
            }

            if (ImGui.MenuItem("Reset Layout"))
            {
                // Bring every panel back and rebuild the default docked arrangement next frame.
                _showGame = _showHierarchy = true;
                _showInspector = _showConsole = _showAssetBrowser = true;
                _rebuildDefaultLayout = true;
            }

            ImGui.Separator();

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
        bool ctrl = Spot.Core.Input.GetKey(Spot.Core.Key.LeftControl) || Spot.Core.Input.GetKey(Spot.Core.Key.RightControl);
        if (_state == EditorState.Edit && ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.S))
        {
            SaveScene();
        }
    }

    // Records the current scene as the clean baseline (called after a successful save/open).
    private void UpdateSceneStatus()
    {
        if (_state == EditorState.Edit)
        {
            foreach (var sceneData in _openScenes)
            {
                if (sceneData.FilePath == null)
                {
                    sceneData.IsDirty = true;
                }
                else if (++sceneData.DirtyCheckCounter >= 15)
                {
                    sceneData.DirtyCheckCounter = 0;
                    string current = new SceneSerializer(sceneData.Scene).SerializeToString();
                    sceneData.IsDirty = sceneData.SavedSnapshot == null || current != sceneData.SavedSnapshot;
                }
            }
        }

        string title = "Spot.Editor";
        if (_state == EditorState.Play)
        {
            string sceneName = _activeSceneData?.FilePath != null ? System.IO.Path.GetFileNameWithoutExtension(_activeSceneData.FilePath) : "Untitled";
            title = $"{sceneName} (Playing) - Spot.Editor";
        }
        
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
            OpenSceneAsset(filepath);
        }
    }

    private void SaveScene()
    {
        if (_activeSceneData == null) return;

        if (_activeSceneData.FilePath == null)
        {
            string initialDir = Project.Active != null ? Project.Active.GetAssetDirectory() : "";
            _activeSceneData.FilePath = Spot.Editor.Utils.FileDialogs.SaveFile("Spot Scene (*.sptscene)|*.sptscene", "sptscene", initialDir);
            if (_activeSceneData.FilePath == null) return;
        }

        new SceneSerializer(_activeSceneData.Scene).Serialize(_activeSceneData.FilePath);
        EnsureStartScene(_activeSceneData.FilePath);
        _activeSceneData.SavedSnapshot = new SceneSerializer(_activeSceneData.Scene).SerializeToString();
        _activeSceneData.IsDirty = false;
        _activeSceneData.DirtyCheckCounter = 0;
    }

    private void SaveAllScenes()
    {
        foreach (var sceneData in _openScenes)
        {
            if (sceneData.FilePath != null && sceneData.IsDirty)
            {
                new SceneSerializer(sceneData.Scene).Serialize(sceneData.FilePath);
                sceneData.SavedSnapshot = new SceneSerializer(sceneData.Scene).SerializeToString();
                sceneData.IsDirty = false;
                sceneData.DirtyCheckCounter = 0;
            }
        }
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
        _openScenes.Clear();
        if (System.IO.File.Exists(startSceneAbs))
        {
            OpenSceneAsset(startSceneAbs);
        }
        else
        {
            var newSceneData = new OpenSceneData(_context);
            _openScenes.Add(newSceneData);
            _activeSceneData = newSceneData;
            _lastEditedSceneData = newSceneData;
            _context.ActiveScene = newSceneData.Scene;
        }
        _context.Selection = null;
    }

}

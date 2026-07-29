using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Editor.Panels;
using Spot.Editor.Scenes;

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
    private readonly EditorCamera _editorCamera = new();

    private string? _currentScenePath;

    public EditorScene()
    {
        _hierarchyPanel = new HierarchyPanel(_context);
        _inspectorPanel = new InspectorPanel(_context);
        _viewportPanel = new ViewportPanel(_context);
        _gamePanel = new ViewportPanel(_context);
        _consolePanel = new ConsolePanel(_context);
        _assetBrowserPanel = new AssetBrowserPanel(_context);
    }

    public override void OnEnter()
    {
        _framebuffer = new Framebuffer(1280, 720);
        _gameFramebuffer = new Framebuffer(1280, 720);
        
        Project.New();
        
        _context.ActiveScene = new Scene();
        
        _viewportPanel.SetFramebuffer(_framebuffer);
        _viewportPanel.SetCamera(_editorCamera);

        _gamePanel.SetFramebuffer(_gameFramebuffer);
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
        
        RenderSystem.Render(_context.ActiveScene, _editorCamera.Camera);
        
        // Debug Physics Rendering
        if (_context.Selection.HasValue)
        {
            var selectedEntity = _context.Selection.Value;
            if (selectedEntity.HasComponent<Spot.Physics.BoxCollider2DComponent>() && selectedEntity.HasComponent<Transform>())
            {
                Renderer2D.BeginScene(_editorCamera.Camera);
                
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
        
        OrthographicCamera? gameCamera = null;
        Vector4 clearColor = new Vector4(0.1f, 0.1f, 0.1f, 1.0f);
        
        foreach (var entity in _context.ActiveScene.View<CameraComponent>())
        {
            var cc = entity.GetComponent<CameraComponent>();
            if (cc.Primary)
            {
                if (entity.HasComponent<Transform>())
                {
                    var transform = entity.GetComponent<Transform>();
                    cc.Camera.Position = transform.WorldPosition;
                    cc.Camera.Rotation = transform.WorldRotation.Z;
                }
                gameCamera = cc.Camera;
                clearColor = cc.BackgroundColor;
                break;
            }
        }
        
        Renderer.SetClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
        Renderer.Clear();

        if (gameCamera != null)
        {
            RenderSystem.Render(_context.ActiveScene, gameCamera);
        }
        
        _gameFramebuffer.Unbind();

        var window = Spot.Core.Application.Instance.Window;
        Renderer.SetViewport(0, 0, (uint)window.Width, (uint)window.Height);
        Renderer.SetClearColor(0.1f, 0.1f, 0.1f, 1.0f);
    }

    public override void OnImGuiRender()
    {
        DrawMenuBar();

        float toolbarHeight = 40;
        var viewport = ImGui.GetMainViewport();
        var workPos = viewport.WorkPos;
        var workSize = viewport.WorkSize;

        ImGui.SetNextWindowPos(new Vector2(workPos.X, workPos.Y));
        ImGui.SetNextWindowSize(new Vector2(workSize.X, toolbarHeight));
        ImGui.Begin("##Toolbar", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoScrollbar);
        
        if (_state == EditorState.Edit)
        {
            if (ImGui.Button("Play")) { OnPlay(); }
        }
        else
        {
            if (ImGui.Button("Stop")) { OnStop(); }
        }
        ImGui.End();

        var mainPos = new Vector2(workPos.X, workPos.Y + toolbarHeight);
        float hierarchyWidth = 300;
        float inspectorWidth = 300;
        float consoleHeight = 200;

        float middleWidth = workSize.X - hierarchyWidth - inspectorWidth;
        float middleHeight = workSize.Y - consoleHeight - toolbarHeight;

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
    }
    
    private void CreateProject(string name, string location)
    {
        string projDir = System.IO.Path.Combine(location, name);
        if (!System.IO.Directory.Exists(projDir)) System.IO.Directory.CreateDirectory(projDir);
        
        string assetsDir = System.IO.Path.Combine(projDir, "Assets");
        if (!System.IO.Directory.Exists(assetsDir)) System.IO.Directory.CreateDirectory(assetsDir);
        
        string sptprojPath = System.IO.Path.Combine(projDir, name + ".sptproj");
        
        Project.New();
        Project.Active!.Config.Name = name;
        Project.Active.Config.StartScene = "Scenes/Main.sptscene";
        Project.Active.ProjectDirectory = projDir;
        
        Project.SaveActive(sptprojPath); // Saves project config and generates .csproj
        
        string slnContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
Project(""{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}"") = ""{name}"", ""{name}.csproj"", ""{{{System.Guid.NewGuid().ToString().ToUpper()}}}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{{{System.Guid.NewGuid().ToString().ToUpper()}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{{{System.Guid.NewGuid().ToString().ToUpper()}}}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{{{System.Guid.NewGuid().ToString().ToUpper()}}}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{{{System.Guid.NewGuid().ToString().ToUpper()}}}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
";
        System.IO.File.WriteAllText(System.IO.Path.Combine(projDir, name + ".sln"), slnContent);
        
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
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New Project"))
                {
                    _isCreatingProject = true;
                }
                
                if (ImGui.MenuItem("Open Project..."))
                {
                    string? filepath = Spot.Editor.Utils.FileDialogs.OpenFile("Spot Project (*.sptproj)|*.sptproj");
                    if (filepath != null)
                    {
                        if (Project.Load(filepath) != null && Project.Active != null)
                        {
                            string startSceneAbs = System.IO.Path.Combine(Project.Active.ProjectDirectory, Project.Active.Config.StartScene);
                            var newScene = new Scene();
                            
                            if (System.IO.File.Exists(startSceneAbs))
                            {
                                var serializer = new SceneSerializer(newScene);
                                if (serializer.Deserialize(startSceneAbs))
                                {
                                    _context.ActiveScene = newScene;
                                    _context.Selection = null;
                                    _currentScenePath = startSceneAbs;
                                }
                                else
                                {
                                    _context.ActiveScene = new Scene();
                                    _context.Selection = null;
                                    _currentScenePath = null;
                                }
                            }
                            else
                            {
                                _context.ActiveScene = new Scene();
                                _context.Selection = null;
                                _currentScenePath = null;
                            }
                        }
                    }
                }
                
                if (ImGui.MenuItem("Save Project"))
                {
                    if (Project.Active != null && string.IsNullOrEmpty(Project.Active.ProjectDirectory))
                    {
                        string? filepath = Spot.Editor.Utils.FileDialogs.SaveFile("Spot Project (*.sptproj)|*.sptproj", "sptproj");
                        if (filepath != null)
                        {
                            Project.SaveActive(filepath);
                        }
                    }
                    else if (Project.Active != null)
                    {
                        Project.SaveActive(System.IO.Path.Combine(Project.Active.ProjectDirectory, Project.Active.Config.Name + ".sptproj"));
                    }
                }
                
                ImGui.Separator();

                if (ImGui.MenuItem("New Scene"))
                {
                    _context.ActiveScene = new Scene();
                    _context.Selection = null;
                    _currentScenePath = null;
                }
                
                if (ImGui.MenuItem("Open Scene..."))
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
                        }
                    }
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Save Scene"))
                {
                    if (_context.ActiveScene != null)
                    {
                        if (_currentScenePath == null)
                        {
                            string initialDir = Project.Active != null ? Project.Active.GetAssetDirectory() : "";
                            _currentScenePath = Spot.Editor.Utils.FileDialogs.SaveFile("Spot Scene (*.sptscene)|*.sptscene", "sptscene", initialDir);
                        }
                        
                        if (_currentScenePath != null)
                        {
                            var serializer = new SceneSerializer(_context.ActiveScene);
                            serializer.Serialize(_currentScenePath);
                        }
                    }
                }
                
                if (ImGui.MenuItem("Save Scene As..."))
                {
                    if (_context.ActiveScene != null)
                    {
                        string initialDir = Project.Active != null ? Project.Active.GetAssetDirectory() : "";
                        string? filepath = Spot.Editor.Utils.FileDialogs.SaveFile("Spot Scene (*.sptscene)|*.sptscene", "sptscene", initialDir);
                        if (filepath != null)
                        {
                            _currentScenePath = filepath;
                            var serializer = new SceneSerializer(_context.ActiveScene);
                            serializer.Serialize(_currentScenePath);
                        }
                    }
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Exit"))
                {
                    Spot.Core.Application.Instance.Quit();
                }
                ImGui.EndMenu();
            }
            ImGui.EndMainMenuBar();
        }
    }
}

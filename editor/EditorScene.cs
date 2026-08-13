using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Build;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Editor.Panels;
using Spot.DebugUI.Panels;
using Spot.Editor.Scenes;
using Spot.DebugUI.UI;
using Spot.Editor.UI;
using Spot.Events;

namespace Spot.Editor;


public class OpenSceneData
{
    public Scene Scene = new();
    public string? FilePath;
    public string? SavedSnapshot;
    public bool IsDirty = true;
    public int DirtyCheckCounter = 0;

    // Undo/redo history for this scene tab. Snapshots are full-scene JSON (the same serialization the
    // dirty-check uses). UndoBaseline is the last "settled" state; edits push the previous baseline onto
    // UndoStack. Lists (not Stack) so the oldest entry can be dropped once MaxUndo is exceeded.
    public string? UndoBaseline;
    public readonly List<string> UndoStack = new();
    public readonly List<string> RedoStack = new();
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
    private Entity? _lastSelectedParticleEntity = null;

    private bool _isCreatingProject = false;
    private bool _showAbout = false;

    // Environment details shown on the About dialog's "System" tab. Queried once, the first time the
    // dialog needs them (the GL strings require a current context, which only exists inside a frame),
    // then cached so the popup doesn't re-probe the runtime and driver every frame it is open.
    private string? _sysRuntime;
    private string? _sysOs;
    private string? _sysArch;
    private string? _sysGpu;
    private string? _sysGl;
    private string _newProjectName = "MyProject";
    private string _newProjectLocation = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

    private readonly EditorContext _context = new();
    
    private readonly HierarchyPanel _hierarchyPanel;
    private readonly InspectorPanel _inspectorPanel;
    private readonly ViewportPanel _gamePanel;
    private readonly ConsolePanel _consolePanel;
    private readonly AssetBrowserPanel _assetBrowserPanel;
    private readonly ProjectSettingsPanel _projectSettingsPanel;

    private Framebuffer? _gameFramebuffer;

    private List<OpenSceneData> _openScenes = new();
    private OpenSceneData? _activeSceneData = null;
    private OpenSceneData? _lastEditedSceneData = null;

    // Unsaved-changes confirmation state: a scene panel pending close, and the app-quit prompt.
    private OpenSceneData? _pendingCloseScene;
    private bool _showQuitConfirm;

    private string? _lastWindowTitle;

    // Per-panel visibility, toggled from View > Panels and by each window's close button.
    private bool _showGame = true;
    private bool _showHierarchy = true;
    private bool _showInspector = true;
    private uint _lastGameDockId;
    private bool _showConsole = true;
    private bool _showAssetBrowser = true;
    private bool _showProjectSettings = false;

    // When set, the default docked layout is rebuilt on the next frame (first launch / Reset Layout).
    private bool _rebuildDefaultLayout = !System.IO.File.Exists("imgui.ini");

    // Number of initial frames to keep the loading cover up, hiding the dock layout and framebuffers
    // as they settle (and the heavy first render) so the editor never flashes a half-built UI. Seeded
    // in OnEnter and counted down in OnImGuiRender.
    private int _warmupFrames;

    public EditorScene()
    {
        _hierarchyPanel = new HierarchyPanel(_context);
        _inspectorPanel = new InspectorPanel(_context);
        
        _gamePanel = new ViewportPanel(_context);
        _consolePanel = new ConsolePanel(_context);
        _assetBrowserPanel = new AssetBrowserPanel(_context);
        _projectSettingsPanel = new ProjectSettingsPanel();
        _assetBrowserPanel.OnAssetOpened += OpenSceneAsset;

        _hierarchyPanel.OnEntityDoubleClicked += entity =>
        {
            if (entity.HasComponent<TransformComponent>() && _activeSceneData != null)
            {
                _activeSceneData.EditorCamera.Focus(entity.GetComponent<TransformComponent>().WorldPosition);
            }
        };
    }

    public override void OnEnter()
    {
        _warmupFrames = 3;
        EditorThemeManager.SetTheme(EditorThemes.SpotDark);
        Spot.Editor.Utils.EditorSettings.LoadAndApply(Spot.Core.Application.Instance.Window.NativeWindow);
        ImGui.LoadIniSettingsFromDisk("imgui.ini");

        // Intercept window-close requests so we can confirm unsaved changes first.
        Spot.Core.Application.Instance.CanClose = CanCloseApp;

        _gameFramebuffer = new Framebuffer(1280, 720);
        _gamePanel.SetFramebuffer(_gameFramebuffer);

        LoadStartScene();
    }

    // Loads the active project's start scene (falling back to an empty standalone scene when there
    // is none), so the editor opens on whatever the launcher selected.
    private void OpenSceneAsset(string filepath)
    {
        if (!filepath.EndsWith(".sptscene", System.StringComparison.OrdinalIgnoreCase))
        {
            Log.CoreWarn("Cannot open '{0}' as a scene: not a .sptscene file.", System.IO.Path.GetFileName(filepath));
            return;
        }

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
        else
        {
            Log.CoreError("Failed to open scene '{0}'. See the console for details.", System.IO.Path.GetFileName(filepath));
        }
    }

    // Scans the active project's assets and installs the Library-backed content resolver, so guid: references
    // in scenes/materials resolve to cooked artifacts — the editor renders exactly what a build would ship.
    private static void ActivateProjectPipeline()
    {
        var project = Project.Active;
        if (project == null)
        {
            return;
        }

        Spot.Assets.AssetDatabase.Refresh(project.GetAssetDirectory());
        Spot.Assets.AssetDatabase.InstallLibraryResolver(System.IO.Path.Combine(project.ProjectDirectory, Spot.Core.ProjectStructure.LibraryFolder));

        LoadProjectAssembly(project);
    }

    private static void LoadProjectAssembly(Project project)
    {
        string binDir = System.IO.Path.Combine(project.ProjectDirectory, "bin");
        if (System.IO.Directory.Exists(binDir))
        {
            var dlls = System.IO.Directory.GetFiles(binDir, project.Config.Name + ".dll", System.IO.SearchOption.AllDirectories);
            var latest = System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OrderByDescending(dlls, f => System.IO.File.GetLastWriteTimeUtc(f)));
            if (latest != null)
            {
                try
                {
                    byte[] assemblyBytes = System.IO.File.ReadAllBytes(latest);
                    
                    // Also try to load the PDB if it exists next to the DLL, so stack traces and debugging work.
                    string pdbPath = System.IO.Path.ChangeExtension(latest, ".pdb");
                    if (System.IO.File.Exists(pdbPath))
                    {
                        byte[] pdbBytes = System.IO.File.ReadAllBytes(pdbPath);
                        System.Reflection.Assembly.Load(assemblyBytes, pdbBytes);
                    }
                    else
                    {
                        System.Reflection.Assembly.Load(assemblyBytes);
                    }
                    
                    Spot.Core.Log.CoreInfo("Loaded project assembly: {0}", latest);
                }
                catch (System.Exception ex)
                {
                    Spot.Core.Log.CoreWarn("Failed to load project assembly '{0}': {1}", latest, ex.Message);
                }
            }
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

        ActivateProjectPipeline();

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
        // The game now runs in an external process, so the editor's copy of the scene
        // should never process physics or scripts (UpdateRuntime). It just stays in edit mode.
        foreach (var sceneData in _openScenes)
        {
            sceneData.Scene.OnUpdate(deltaTime);
            sceneData.Scene.FlushDestroyed();
        }

        if (_state == EditorState.Edit)
        {
            Entity? currentSelected = _context.Selection;
            
            if (_lastSelectedParticleEntity.HasValue && currentSelected != _lastSelectedParticleEntity)
            {
                // Only clear if the entity is still alive in the scene
                if (_activeSceneData != null && _activeSceneData.Scene.IsAlive(_lastSelectedParticleEntity.Value))
                {
                    if (_lastSelectedParticleEntity.Value.TryGetComponent(out ParticleSystemComponent? oldParticles))
                    {
                        oldParticles.Clear();
                        oldParticles.Stop();
                    }
                }
            }

            if (currentSelected.HasValue && currentSelected.Value.IsActiveInHierarchy() && currentSelected.Value.TryGetComponent(out ParticleSystemComponent? particles))
            {
                if (!particles.IsPlaying)
                {
                    particles.Play();
                }
                Spot.Scenes.ParticleSystem.UpdateEntity(currentSelected.Value, deltaTime);
                _lastSelectedParticleEntity = currentSelected;
            }
            else
            {
                _lastSelectedParticleEntity = null;
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
            Renderer.SetClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            Renderer.Clear();
            
            if (sceneData.EditorCamera.Is3D)
            {
                Renderer.SetDepthTest(true);
                Renderer.SetFaceCulling(true);
            }
                
            RenderSystem.Render(sceneData.Scene, sceneData.EditorCamera.ViewProjection, sceneData.EditorCamera.Position);

            // The editor grid and world axes are screen-aligned / crossed-quad overlays with no single
            // front-face winding, so culling must be off while drawing them. Otherwise the back-face
            // culling enabled above for the scene meshes discards them and the grid/axes vanish.
            if (sceneData.EditorCamera.Is3D)
                Renderer.SetFaceCulling(false);

            // Draw Axes. Drawn before the grid so that, at the ground plane, the axis lines win the
            // equal-depth test against the grid's own centre lines and read as crisp coloured lines.
            var palette = EditorThemeManager.Current.Palette;
            Renderer2D.BeginScene(sceneData.EditorCamera.ViewProjection);

            if (sceneData.EditorCamera.Is3D)
            {
                // Full X/Y/Z origin axes. Thin lines whose thickness scales with camera distance so
                // they hold a steady, understated on-screen weight as the camera dollies in and out.
                float axisThickness = Math.Max(0.004f, sceneData.EditorCamera.Position.Length() * 0.0018f);
                Renderer2D.DrawLine(new Vector3(-1000, 0, 0), new Vector3(1000, 0, 0), palette.AxisX, axisThickness);
                Renderer2D.DrawLine(new Vector3(0, -1000, 0), new Vector3(0, 1000, 0), palette.AxisY, axisThickness);
                Renderer2D.DrawLine(new Vector3(0, 0, -1000), new Vector3(0, 0, 1000), palette.AxisZ, axisThickness);
            }
            else
            {
                float axisThickness = Math.Max(0.006f, sceneData.EditorCamera.ZoomLevel * 0.003f);
                Renderer2D.DrawEditorGrid(sceneData.EditorCamera.ZoomLevel);
                Renderer2D.DrawLine(new Vector3(-1000, 0, 0), new Vector3(1000, 0, 0), palette.AxisX, axisThickness);
                Renderer2D.DrawLine(new Vector3(0, -1000, 0), new Vector3(0, 1000, 0), palette.AxisY, axisThickness);
            }
            Renderer2D.EndScene();

            if (sceneData.EditorCamera.Is3D)
            {
                Renderer3D.BeginScene(sceneData.EditorCamera.ViewProjection);
                Renderer3D.DrawEditorGrid(sceneData.EditorCamera.Position);
                Renderer3D.EndScene();
            }

            if (sceneData.EditorCamera.Is3D)
            {
                Renderer.SetDepthTest(false);
                Renderer.SetFaceCulling(false);
            }
            
            // Debug Physics Rendering
            if (_context.Selection.HasValue && sceneData == _activeSceneData)
            {
                Renderer2D.BeginScene(sceneData.EditorCamera.ViewProjection);
                
                void DrawColliders(Entity entity)
                {
                    if (entity.HasComponent<Spot.Physics.BoxCollider2DComponent>() && entity.HasComponent<TransformComponent>())
                    {
                        var transform = entity.GetComponent<TransformComponent>();
                        var collider = entity.GetComponent<Spot.Physics.BoxCollider2DComponent>();
                        var bounds = collider.GetWorldBounds(new Vector2(transform.WorldPosition.X, transform.WorldPosition.Y), new Vector2(transform.WorldScale.X, transform.WorldScale.Y));
                        Renderer2D.DrawRect(bounds.Center, bounds.HalfExtents * 2.0f, new Vector4(0.0f, 1.0f, 0.0f, 1.0f), 0.02f);
                    }
                    
                    if (entity.HasComponent<Spot.Physics.BoxCollider3DComponent>() && entity.HasComponent<TransformComponent>())
                    {
                        var transform = entity.GetComponent<TransformComponent>();
                        var collider = entity.GetComponent<Spot.Physics.BoxCollider3DComponent>();
                        var bounds = collider.GetWorldBounds(transform.WorldPosition, transform.WorldScale);
                        
                        Vector3 min = bounds.Min;
                        Vector3 max = bounds.Max;
                        Vector4 color = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
                        float t = 0.02f;

                        // Bottom rect
                        Renderer2D.DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z), color, t);
                        Renderer2D.DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, min.Y, max.Z), color, t);
                        Renderer2D.DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(min.X, min.Y, max.Z), color, t);
                        Renderer2D.DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, min.Y, min.Z), color, t);

                        // Top rect
                        Renderer2D.DrawLine(new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color, t);
                        Renderer2D.DrawLine(new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, max.Y, max.Z), color, t);
                        Renderer2D.DrawLine(new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color, t);
                        Renderer2D.DrawLine(new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, max.Y, min.Z), color, t);

                        // Connecting lines
                        Renderer2D.DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, max.Y, min.Z), color, t);
                        Renderer2D.DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color, t);
                        Renderer2D.DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z), color, t);
                        Renderer2D.DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color, t);
                    }

                    if (entity.TryGetComponent(out Spot.Scenes.RelationshipComponent? rel))
                    {
                        foreach (var child in rel.Children)
                        {
                            DrawColliders(child);
                        }
                    }
                }
                
                DrawColliders(_context.Selection.Value);
                Renderer2D.EndScene();
            }

            // Camera frustum gizmo: draw where the selected camera is looking.
            if (_context.Selection.HasValue && sceneData == _activeSceneData
                && _context.Selection.Value.HasComponent<CameraComponent>()
                && _context.Selection.Value.HasComponent<TransformComponent>())
            {
                var camEntity = _context.Selection.Value;
                var camComp = camEntity.GetComponent<CameraComponent>();
                var camTransform = camEntity.GetComponent<TransformComponent>();

                // Invert the camera's real view-projection so the drawing matches exactly what it
                // renders (direction, aspect, FOV) regardless of projection type or forward convention.
                System.Numerics.Matrix4x4 camVP = camComp.GetViewProjection(camTransform);
                if (System.Numerics.Matrix4x4.Invert(camVP, out System.Numerics.Matrix4x4 invVP))
                {
                    // NDC corner (x,y in [-1,1]; z in [0,1] for System.Numerics projections) -> world.
                    Vector3 ToWorld(float x, float y, float z)
                    {
                        Vector4 p = Vector4.Transform(new Vector4(x, y, z, 1.0f), invVP);
                        return new Vector3(p.X, p.Y, p.Z) / p.W;
                    }

                    // Near corners sit at the true near plane; far corners are pulled in to a capped
                    // gizmo length along each frustum edge, so the drawing shows the camera's aim and
                    // field of view without spanning the whole scene when FarClip is large (the 1000
                    // default would otherwise draw lines a kilometre long).
                    float gizmoDepth = MathF.Min(camComp.FarClip - camComp.NearClip, 8.0f);
                    Vector3 CapFar(Vector3 near, Vector3 far)
                    {
                        Vector3 dir = far - near;
                        float len = dir.Length();
                        if (len <= 1e-6f) return near;
                        return near + (dir / len) * gizmoDepth;
                    }

                    Vector3 nbl = ToWorld(-1, -1, 0), nbr = ToWorld(1, -1, 0), ntr = ToWorld(1, 1, 0), ntl = ToWorld(-1, 1, 0);
                    Vector3 fbl = CapFar(nbl, ToWorld(-1, -1, 1));
                    Vector3 fbr = CapFar(nbr, ToWorld(1, -1, 1));
                    Vector3 ftr = CapFar(ntr, ToWorld(1, 1, 1));
                    Vector3 ftl = CapFar(ntl, ToWorld(-1, 1, 1));

                    Vector4 frustumColor = new Vector4(0.35f, 0.75f, 1.0f, 1.0f);
                    float t = Math.Max(0.004f, sceneData.EditorCamera.Position.Length() * 0.0016f);

                    Renderer2D.BeginScene(sceneData.EditorCamera.ViewProjection);

                    // Near rectangle
                    Renderer2D.DrawLine(nbl, nbr, frustumColor, t);
                    Renderer2D.DrawLine(nbr, ntr, frustumColor, t);
                    Renderer2D.DrawLine(ntr, ntl, frustumColor, t);
                    Renderer2D.DrawLine(ntl, nbl, frustumColor, t);
                    // Far rectangle
                    Renderer2D.DrawLine(fbl, fbr, frustumColor, t);
                    Renderer2D.DrawLine(fbr, ftr, frustumColor, t);
                    Renderer2D.DrawLine(ftr, ftl, frustumColor, t);
                    Renderer2D.DrawLine(ftl, fbl, frustumColor, t);
                    // Connecting edges (near -> far)
                    Renderer2D.DrawLine(nbl, fbl, frustumColor, t);
                    Renderer2D.DrawLine(nbr, fbr, frustumColor, t);
                    Renderer2D.DrawLine(ntr, ftr, frustumColor, t);
                    Renderer2D.DrawLine(ntl, ftl, frustumColor, t);

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
                if (entity.HasComponent<TransformComponent>())
                {
                    var transform = entity.GetComponent<TransformComponent>();
                    var viewProj = cc.GetViewProjection(transform);
                    var is3DPrev = cc.ProjectionType == SceneCameraProjection.Perspective;

                    Renderer.SetClearColor(cc.BackgroundColor.X, cc.BackgroundColor.Y, cc.BackgroundColor.Z, cc.BackgroundColor.W);
                    Renderer.Clear();

                    if (is3DPrev)
                    {
                        Renderer.SetDepthTest(true);
                        Renderer.SetFaceCulling(true);
                    }

                    RenderSystem.Render(sceneData.Scene, viewProj, transform.WorldPosition);
                    
                    if (is3DPrev)
                    {
                        Renderer.SetDepthTest(false);
                        Renderer.SetFaceCulling(false);
                    }
                }
                sceneData.CameraPreviewFramebuffer.Unbind();
            }
        }
        
        // Render Game View
        _gameFramebuffer.Bind();
        Renderer.SetClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        Renderer.Clear();
        
        var gameScene = _state == EditorState.Play ? _context.ActiveScene : _lastEditedSceneData?.Scene;
        
        if (gameScene != null)
        {
            System.Numerics.Matrix4x4? viewProjection = null;
            Vector3 cameraPosition = Vector3.Zero;
            Vector4 clearColor = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            bool is3D = false;

            foreach (var entity in gameScene.View<CameraComponent>())
            {
                var cc = entity.GetComponent<CameraComponent>();
                if (cc.Primary)
                {
                    if (entity.HasComponent<TransformComponent>())
                    {
                        var transform = entity.GetComponent<TransformComponent>();
                        viewProjection = cc.GetViewProjection(transform);
                        cameraPosition = transform.WorldPosition;
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
                {
                    Renderer.SetDepthTest(true);
                    Renderer.SetFaceCulling(true);
                }
                    
                RenderSystem.Render(gameScene, viewProjection.Value, cameraPosition);
                
                if (is3D)
                {
                    Renderer.SetDepthTest(false);
                    Renderer.SetFaceCulling(false);
                }
            }
        }
        
        _gameFramebuffer.Unbind();

        var window = Spot.Core.Application.Instance.Window;
        Renderer.SetViewport(0, 0, (uint)window.Width, (uint)window.Height);
        Renderer.SetClearColor(0.0f, 0.0f, 0.0f, 1.0f);
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
                uint targetDock = _lastGameDockId != 0 ? _lastGameDockId : dockspaceId;
                ImGui.SetNextWindowDockID(targetDock, ImGuiCond.FirstUseEver);
                sceneData.FirstFrame = false;
            }

            bool wasOpen = sceneData.IsOpen;
            bool open = ImGui.Begin(title, ref sceneData.IsOpen, ImGuiWindowFlags.NoCollapse);
            ImGui.PopStyleVar();

            // Closing a scene with unsaved changes: keep it open and confirm first.
            if (wasOpen && !sceneData.IsOpen && sceneData.IsDirty)
            {
                sceneData.IsOpen = true;
                _pendingCloseScene = sceneData;
            }

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
                // While the game runs in an external process the scene viewport is frozen: disable all
                // camera/gizmo/selection input and paint a centered "running" notice over it.
                bool playing = _state == EditorState.Play;
                var viewportMin = ImGui.GetCursorScreenPos();
                var viewportSize = ImGui.GetContentRegionAvail();
                sceneData.ViewportPanel.OnImGuiRender(handleInput: !playing && (isFocused || isHovered));
                if (playing)
                    DrawPlayingOverlay(viewportMin, viewportSize);
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
            _lastGameDockId = ImGui.GetWindowDockID();
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

                var imageTopLeft = ImGui.GetCursorScreenPos();
                _gamePanel.OnImGuiRender(handleInput: false);

                // Without an active primary camera nothing renders, leaving a blank Game view. Explain it
                // instead of showing an unexplained black panel — a common first-time snag.
                if (size.X > 0 && size.Y > 0 && gameScene != null && !gameScene.HasActivePrimaryCamera())
                {
                    const string msg = "No camera in scene";
                    var textSize = ImGui.CalcTextSize(msg);
                    var textPos = new Vector2(
                        imageTopLeft.X + (size.X - textSize.X) * 0.5f,
                        imageTopLeft.Y + (size.Y - textSize.Y) * 0.5f);
                    var pad = new Vector2(10.0f, 6.0f);
                    var drawList = ImGui.GetWindowDrawList();
                    drawList.AddRectFilled(textPos - pad, textPos + textSize + pad,
                        ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.55f)), 4.0f);
                    drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.9f)), msg);
                }
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

        _projectSettingsPanel.OnImGuiRender(ref _showProjectSettings);

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

        DrawAboutPopup();

        DrawUnsavedChangesModals();

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

        // Cover the first few frames so the settling dock layout / framebuffers are never seen. Drawn
        // on the foreground draw list (above every panel and the menu bar), matching the launcher's
        // loading screen so the hand-off from launcher to editor looks like one continuous load.
        if (_warmupFrames > 0)
        {
            _warmupFrames--;
            string title = Project.Active?.Config.Name ?? "Loading";
            LoadingScreen.Present(EditorThemeManager.Current.Palette, title, "Loading project...");
        }
    }

    // ----- About dialog --------------------------------------------------------------------------

    // The open-source projects Spot is built on, shown on the About dialog's "Credits" tab. Kept in
    // sync with THIRDPARTY.md (versions live there; only the name + license are surfaced here).
    private static readonly (string Name, string License)[] AboutLibraries =
    {
        ("Silk.NET",       "MIT"),
        ("Dear ImGui",     "MIT"),
        ("BepuPhysics",    "Apache-2.0"),
        ("OpenAL Soft",    "LGPL-2.1"),
        ("Assimp",         "BSD-3-Clause"),
        ("Serilog",        "Apache-2.0"),
        ("StbImageSharp",  "Public Domain"),
        ("StbVorbisSharp", "Public Domain"),
    };

    private const string RepoUrl = "https://github.com/lucasdcampos/spotengine";

    // A short feature line on the "About" tab: an accent-colored icon glyph followed by a description.
    private static readonly (string Glyph, string Text)[] AboutHighlights =
    {
        (EditorIcons.Cube, "Real-time 2D & 3D rendering — HDR, bloom, ACES tonemapping & FXAA"),
        (EditorIcons.Sun,  "Dynamic lighting, shadows, skyboxes & GPU particles"),
        (EditorIcons.Move, "3D physics & character controllers powered by BepuPhysics"),
        (EditorIcons.Music, "3D positional audio via OpenAL"),
        (EditorIcons.Code, "C# scripting with coroutines, tweening & input actions"),
        (EditorIcons.Gear, "Project tooling, asset cooking & self-contained platform builds"),
    };

    /// <summary>
    /// Draws the Help &gt; About modal: a centered header (icon / name / version) over a tabbed body
    /// (overview + highlights, host system details, and third-party credits). Opened by setting
    /// <see cref="_showAbout"/>; from there ImGui owns the popup's lifetime.
    /// </summary>
    private void DrawAboutPopup()
    {
        if (_showAbout)
        {
            ImGui.OpenPopup("About Spot Engine");
            _showAbout = false; // Consume the request; the modal stays open on its own until dismissed.
        }

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(500, 560), ImGuiCond.Appearing);

        bool open = true;
        if (!ImGui.BeginPopupModal("About Spot Engine", ref open,
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse))
        {
            return;
        }

        var palette = EditorThemeManager.Current.Palette;
        float windowWidth = ImGui.GetWindowSize().X;
        float padX = ImGui.GetStyle().WindowPadding.X;

        void Center(float itemWidth) =>
            ImGui.SetCursorPosX(MathF.Max((windowWidth - itemWidth) * 0.5f, padX));

        // ---- Header ---------------------------------------------------------------------------
        ImGui.Spacing();
        ImGui.PushFont(EditorFonts.Icons);
        Center(ImGui.CalcTextSize(EditorIcons.Cubes).X);
        ImGui.TextColored(palette.Accent, EditorIcons.Cubes);
        ImGui.PopFont();

        ImGui.Spacing();

        EditorFonts.PushTitle();
        Center(ImGui.CalcTextSize("Spot Engine").X);
        ImGui.TextUnformatted("Spot Engine");
        EditorFonts.Pop();

        const string tagline = "A lightweight 2D/3D game engine for .NET";
        Center(ImGui.CalcTextSize(tagline).X);
        ImGui.TextDisabled(tagline);

        string version = $"Version {SpotEngine.GetVersion()}";
        Center(ImGui.CalcTextSize(version).X);
        ImGui.TextColored(palette.Accent, version);

        ImGui.Spacing();
        ImGui.Separator();

        // ---- Body (tabs) ----------------------------------------------------------------------
        // Reserve room at the bottom for the footer (separator + Close button) so the tab content
        // gets its own scroll region and the button never drifts as tabs change height.
        float footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y * 2.0f;
        var bodySize = new Vector2(0.0f, -footer);
        // Inset each tab's content from the child's edges so text never sits flush against the border.
        // AlwaysUseWindowPadding makes the borderless children honor this padding on all sides.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14.0f, 10.0f));
        const ImGuiChildFlags bodyFlags = ImGuiChildFlags.AlwaysUseWindowPadding;
        if (ImGui.BeginTabBar("AboutTabs"))
        {
            if (ImGui.BeginTabItem("About"))
            {
                ImGui.BeginChild("AboutOverview", bodySize, bodyFlags);
                DrawAboutOverviewTab(palette);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("System"))
            {
                ImGui.BeginChild("AboutSystem", bodySize, bodyFlags);
                DrawAboutSystemTab();
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Credits"))
            {
                ImGui.BeginChild("AboutCredits", bodySize, bodyFlags);
                DrawAboutCreditsTab(palette);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.PopStyleVar();

        // ---- Footer ---------------------------------------------------------------------------
        ImGui.Separator();
        const float closeWidth = 120.0f;
        ImGui.SetCursorPosX((windowWidth - closeWidth) * 0.5f);
        if (ImGui.Button("Close", new Vector2(closeWidth, 0.0f)))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private static void DrawAboutOverviewTab(EditorPalette palette)
    {
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0.0f);
        ImGui.TextUnformatted(
            "Spot is a data-driven game engine built on .NET 10, Silk.NET and Dear ImGui. It pairs a " +
            "docking editor with an entity/component runtime spanning rendering, physics, audio and " +
            "scripting, plus a command-line pipeline for creating, cooking and shipping standalone games.");
        ImGui.PopTextWrapPos();

        AboutHeading(palette, "Highlights");
        foreach (var (glyph, text) in AboutHighlights)
        {
            ImGui.TextColored(palette.Accent, glyph);
            ImGui.SameLine(0.0f, 10.0f);
            ImGui.TextUnformatted(text);
        }

        AboutHeading(palette, "Links");
        if (ImGui.Button("GitHub")) OpenUrl(RepoUrl);
        ImGui.SameLine();
        if (ImGui.Button("Documentation")) OpenUrl($"{RepoUrl}#readme");
        ImGui.SameLine();
        if (ImGui.Button("Report an Issue")) OpenUrl($"{RepoUrl}/issues");
    }

    private void DrawAboutSystemTab()
    {
        _sysRuntime ??= System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        _sysOs ??= System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        _sysArch ??= System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
        _sysGpu ??= QueryGlString(Silk.NET.OpenGL.StringName.Renderer);
        _sysGl ??= QueryGlString(Silk.NET.OpenGL.StringName.Version);

        ImGui.Spacing();
        AboutInfoRow("Engine", $"Spot {SpotEngine.GetVersion()}");
        AboutInfoRow("Runtime", _sysRuntime);
        AboutInfoRow("Operating System", _sysOs);
        AboutInfoRow("Architecture", _sysArch);
        AboutInfoRow("Graphics", _sysGpu);
        AboutInfoRow("OpenGL", _sysGl);

        ImGui.Spacing();
        ImGui.Spacing();
        if (ImGui.Button("Copy to clipboard"))
        {
            ImGui.SetClipboardText(
                $"Spot Engine {SpotEngine.GetVersion()}\n" +
                $"Runtime: {_sysRuntime}\n" +
                $"OS: {_sysOs} ({_sysArch})\n" +
                $"Graphics: {_sysGpu}\n" +
                $"OpenGL: {_sysGl}");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy these details for a bug report");
    }

    private static void DrawAboutCreditsTab(EditorPalette palette)
    {
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0.0f);
        ImGui.TextUnformatted("Spot is free, open-source software, made possible by these projects:");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        foreach (var (name, license) in AboutLibraries)
        {
            ImGui.Bullet();
            ImGui.SameLine();
            ImGui.TextUnformatted(name);
            ImGui.SameLine(230.0f);
            ImGui.TextDisabled(license);
        }

        AboutHeading(palette, string.Empty);
        Center("© 2026 Lucas Maciel de Campos");
        ImGui.TextUnformatted("© 2026 Lucas Maciel de Campos");
        Center("Released under the MIT License.");
        ImGui.TextDisabled("Released under the MIT License.");

        // Centers the next single-line widget within the current content region.
        static void Center(string text) => ImGui.SetCursorPosX(MathF.Max(
            (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) * 0.5f, 0.0f));
    }

    // A small section divider used inside the About tabs: an optional title in the heavier face over a
    // separator, with a little breathing room above so sections read as distinct blocks.
    private static void AboutHeading(EditorPalette palette, string title)
    {
        ImGui.Spacing();
        ImGui.Spacing();
        if (!string.IsNullOrEmpty(title))
        {
            EditorFonts.PushTitle();
            ImGui.TextColored(palette.Accent, title);
            EditorFonts.Pop();
        }
        ImGui.Separator();
        ImGui.Spacing();
    }

    // A two-column "key: value" line for the System tab. The value wraps to the window edge so long
    // driver strings stay readable inside the fixed-width dialog.
    private static void AboutInfoRow(string key, string value)
    {
        ImGui.TextDisabled(key);
        ImGui.SameLine(170.0f);
        ImGui.PushTextWrapPos(0.0f);
        ImGui.TextUnformatted(value);
        ImGui.PopTextWrapPos();
    }

    // Reads a string from the active OpenGL context (GPU name, driver version). Best-effort: if the
    // renderer isn't initialized yet it must not take the editor down, so failures return a placeholder.
    private static string QueryGlString(Silk.NET.OpenGL.StringName name)
    {
        try { return Renderer.Api.GetStringS(name) ?? "Unknown"; }
        catch { return "Unavailable"; }
    }

    // Opens a URL in the user's default browser. Best-effort — a missing handler must never throw.
    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch { /* no browser / blocked shell handler: nothing useful to do */ }
    }

    private void CreateProject(string name, string location)
    {
        string sptprojPath = Spot.Build.ProjectScaffolder.Create(name, location);
        Spot.Editor.Utils.RecentProjects.Add(sptprojPath);
        
        _openScenes.Clear();
        var newSceneData = new OpenSceneData(_context);
        _openScenes.Add(newSceneData);
        _activeSceneData = newSceneData;
        _lastEditedSceneData = newSceneData;
        _context.ActiveScene = newSceneData.Scene;
        _context.Selection = null;
    }

    private void BuildProject(Spot.Build.BuildPlatform platform)
    {
        var project = Project.Active;
        if (project == null || string.IsNullOrEmpty(project.ProjectDirectory)) return;

        Spot.Core.Log.Info($"Starting build process for {platform}...");

        System.Threading.Tasks.Task.Run(() =>
        {
            var result = Spot.Build.ProjectBuilder.Build(project, platform,
                onOutput: msg => Spot.Core.Log.Info(msg),
                onError: msg => Spot.Core.Log.Error(msg));

            if (result.Success)
            {
                Spot.Core.Log.Info("Build completed successfully!");
                if (System.OperatingSystem.IsWindows())
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", $"\"{result.OutputDir}\""); } catch { }
                }
            }
            else
            {
                Spot.Core.Log.Error($"Build failed with exit code {result.ExitCode}. See above for details.");
            }
        });
    }

    public override void OnEvent(Event e)
    {
        base.OnEvent(e);

        var dispatcher = new EventDispatcher(e);
        dispatcher.Dispatch<WindowDropEvent>(OnWindowDrop);
    }

    private bool OnWindowDrop(WindowDropEvent e)
    {
        string targetDir = _assetBrowserPanel.CurrentDirectory;
        if (!System.IO.Directory.Exists(targetDir))
        {
            return false;
        }

        foreach (string file in e.Paths)
        {
            try
            {
                if (System.IO.File.Exists(file))
                {
                    string destFile = System.IO.Path.Combine(targetDir, System.IO.Path.GetFileName(file));
                    System.IO.File.Copy(file, destFile, overwrite: true);
                    Spot.Core.Log.CoreInfo($"Copied '{file}' to '{destFile}'");
                }
                else if (System.IO.Directory.Exists(file))
                {
                    // Basic copy for directory could be recursive, but let's just log for now
                    Spot.Core.Log.CoreWarn($"Dropping directories is not fully supported yet: '{file}'");
                }
            }
            catch (System.Exception ex)
            {
                Spot.Core.Log.CoreError($"Failed to copy dropped file '{file}': {ex.Message}");
            }
        }
        return true;
    }

    public override void OnExit()
    {
        Spot.Core.Application.Instance.CanClose = null;
        Spot.Editor.Utils.EditorSettings.Save(Spot.Core.Application.Instance.Window.NativeWindow);

        foreach (var sceneData in _openScenes) sceneData.Dispose();
        _gameFramebuffer?.Dispose();
        _inspectorPanel.Dispose();
        _context.ActiveScene?.OnExit();
    }

    private System.Diagnostics.Process? _gameProcess;

    private void OnPlay()
    {
        if (_state == EditorState.Edit && Spot.Core.Project.Active != null)
        {
            if (!SaveAllDirtyWithPrompts()) return;

            _state = EditorState.Play;
            Spot.Core.Log.Info("Building project for Play...");

            System.Threading.Tasks.Task.Run(() => 
            {
                var result = Spot.Build.ProjectBuilder.Build(Spot.Core.Project.Active, Spot.Build.BuildPlatform.Windows,
                    onOutput: msg => Spot.Core.Log.Info($"[Build] {msg}"),
                    onError: msg => Spot.Core.Log.Error($"[Build] {msg}"),
                    fastDebug: true);

                if (result.Success)
                {
                    Spot.Core.Log.Info("Build succeeded. Launching game...");
                    var exePath = System.IO.Path.Combine(result.OutputDir, Spot.Core.Project.Active.Config.Name + ".exe");
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = Spot.Core.Project.Active.ProjectDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    
                    _gameProcess = new System.Diagnostics.Process { StartInfo = processInfo };
                    _gameProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Spot.Core.Log.Info($"[Game] {e.Data}"); };
                    _gameProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Spot.Core.Log.Error($"[Game] {e.Data}"); };
                    
                    _gameProcess.EnableRaisingEvents = true;
                    _gameProcess.Exited += (sender, e) => 
                    {
                        Spot.Core.Log.Info("Game process exited.");
                        _gameProcess = null;
                        _state = EditorState.Edit; 
                    };
                    
                    _gameProcess.Start();
                    _gameProcess.BeginOutputReadLine();
                    _gameProcess.BeginErrorReadLine();
                }
                else
                {
                    Spot.Core.Log.Error("Build failed. Cannot play.");
                    _state = EditorState.Edit;
                }
            });
        }
    }

    private void OnStop()
    {
        if (_state == EditorState.Play)
        {
            if (_gameProcess != null && !_gameProcess.HasExited)
            {
                Spot.Core.Log.Info("Stopping game process...");
                try { _gameProcess.Kill(); } catch { }
            }
            _state = EditorState.Edit;
        }
    }

    // Dims a scene viewport and centers a "game is running" notice over it while the game runs in an
    // external process, so it's obvious the frozen, non-interactive view is expected (not a hang).
    private static void DrawPlayingOverlay(Vector2 min, Vector2 size)
    {
        if (size.X <= 0.0f || size.Y <= 0.0f)
            return;

        var palette = EditorThemeManager.Current.Palette;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(min, min + size, ImGui.GetColorU32(new Vector4(0.04f, 0.04f, 0.06f, 0.72f)));

        Vector2 center = min + size * 0.5f;

        var font = ImGui.GetFont();
        const string title = "Game is running";
        const float titleSize = 30.0f;
        Vector2 titleDim = font.CalcTextSizeA(titleSize, float.MaxValue, 0.0f, title);
        drawList.AddText(font, titleSize, new Vector2(center.X - titleDim.X * 0.5f, center.Y - titleDim.Y - 2.0f),
            ImGui.GetColorU32(palette.Text), title);

        const string subtitle = "Press Stop to return to the editor";
        Vector2 subDim = ImGui.CalcTextSize(subtitle);
        drawList.AddText(new Vector2(center.X - subDim.X * 0.5f, center.Y + 6.0f),
            ImGui.GetColorU32(palette.TextDisabled), subtitle);
    }

    // Rebuilds the default docked arrangement: Hierarchy and Inspector on the right,
    // Asset Browser and Console side-by-side along the bottom, and the Scene/Game viewports in the center.
    private void BuildDefaultLayout(uint dockspaceId, Vector2 size)
    {
        ImGuiDock.igDockBuilderRemoveNode(dockspaceId);
        ImGuiDock.igDockBuilderAddNode(dockspaceId, ImGuiDock.DockNodeFlagsDockSpace);
        ImGuiDock.igDockBuilderSetNodeSize(dockspaceId, size);

        uint center = dockspaceId;
        ImGuiDock.igDockBuilderSplitNode(center, ImGuiDir.Right, 0.25f, out uint right, out center);
        ImGuiDock.igDockBuilderSplitNode(right, ImGuiDir.Down, 0.60f, out uint rightBottom, out uint rightTop);
        ImGuiDock.igDockBuilderSplitNode(center, ImGuiDir.Down, 0.30f, out uint bottom, out center);
        ImGuiDock.igDockBuilderSplitNode(bottom, ImGuiDir.Left, 0.50f, out uint bottomLeft, out uint bottomRight);

        ImGuiDock.igDockBuilderDockWindow("Scene", rightTop);
        ImGuiDock.igDockBuilderDockWindow("Properties", rightBottom);
        ImGuiDock.igDockBuilderDockWindow("Asset Browser", bottomLeft);
        ImGuiDock.igDockBuilderDockWindow("Console", bottomRight);
        
        for (int i = 0; i < _openScenes.Count; i++)
        {
            var sceneData = _openScenes[i];
            string sceneName = sceneData.FilePath != null
                ? System.IO.Path.GetFileNameWithoutExtension(sceneData.FilePath)
                : "Untitled";
            string stableId = sceneData.FilePath != null ? sceneData.FilePath : $"Untitled_{i}";
            string title = $"{sceneName}{(sceneData.IsDirty ? "*" : "")}###Scene_{stableId}";
            ImGuiDock.igDockBuilderDockWindow(title, center);
        }

        ImGuiDock.igDockBuilderDockWindow("Game", center);

        ImGuiDock.igDockBuilderFinish(dockspaceId);
    }

    private void DrawMenuBar()
    {
        var palette = EditorThemeManager.Current.Palette;

        // A slightly taller bar with more generous item spacing so the top strip reads as part of the
        // editor chrome rather than a stock menu. The vars stay pushed for the whole bar so the menu
        // items inherit the same rhythm.
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(14.0f, 10.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(14.0f, 8.0f));
        if (!ImGui.BeginMainMenuBar())
        {
            ImGui.PopStyleVar(2);
            return;
        }

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Project...")) _isCreatingProject = true;
            if (ImGui.MenuItem("Open Project...")) OpenProject();
            ImGui.Separator();
            
            if (ImGui.MenuItem("New Scene", "Ctrl+N")) NewScene();
            if (ImGui.MenuItem("Open Scene...")) OpenScene();
            if (ImGui.MenuItem("Save Scene", "Ctrl+S")) SaveScene();
            if (ImGui.MenuItem("Save All Scenes", "Ctrl+Shift+S")) SaveAllScenes();
            ImGui.Separator();
            
            if (Project.Active != null)
            {
                ImGui.MenuItem("Project Settings", "", ref _showProjectSettings);
                ImGui.Separator();
                if (ImGui.BeginMenu("Build Game (Release)"))
                {
                    if (ImGui.MenuItem("Windows")) BuildProject(Spot.Build.BuildPlatform.Windows);
                    if (ImGui.MenuItem("Linux")) BuildProject(Spot.Build.BuildPlatform.Linux);
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Regenerate Project Files"))
                {
                    if (ImGui.MenuItem("Update Build Files (.csproj, DLLs)")) Spot.Build.ProjectGenerator.Generate(Project.Active!, overwriteProgram: false);
                    if (ImGui.MenuItem("Full Reset (Includes Program.cs)")) Spot.Build.ProjectGenerator.Generate(Project.Active!, overwriteProgram: true);
                    ImGui.EndMenu();
                }
                ImGui.Separator();
            }
            
            if (ImGui.MenuItem("Exit")) RequestExit();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            bool canUndo = _state == EditorState.Edit && (_activeSceneData?.UndoStack.Count ?? 0) > 0;
            bool canRedo = _state == EditorState.Edit && (_activeSceneData?.RedoStack.Count ?? 0) > 0;
            if (ImGui.MenuItem("Undo", "Ctrl+Z", false, canUndo)) Undo();
            if (ImGui.MenuItem("Redo", "Ctrl+Y", false, canRedo)) Redo();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            if (ImGui.BeginMenu("Panels"))
            {
                ImGui.MenuItem("Game", "", ref _showGame);
                ImGui.MenuItem("Scene", "", ref _showHierarchy);
                ImGui.MenuItem("Properties", "", ref _showInspector);
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
        ImGui.PopStyleVar(2);
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

        float pad = size * 0.22f;
        if (_state == EditorState.Edit)
        {
            // Play: right-pointing triangle.
            uint color = ImGui.GetColorU32(palette.Text);
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
        if (_state == EditorState.Edit && ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.N))
        {
            NewScene();
        }
        if (_state == EditorState.Edit && ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.Z))
        {
            Undo();
        }
        if (_state == EditorState.Edit && ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.Y))
        {
            Redo();
        }
    }

    // The most entries kept per scene's undo history.
    private const int MaxUndo = 100;

    // True while the user is mid-interaction (dragging the gizmo, or editing an ImGui field), used to
    // coalesce a continuous edit into a single history entry: snapshots are only committed once the
    // interaction settles.
    private static bool EditorIsInteracting() =>
        ImGui.GetIO().MouseDown[0] || ImGui.IsAnyItemActive();

    // Records a history entry when the scene has changed since the last settled baseline. Skipped while
    // the user is still interacting, so a continuous edit (a gizmo drag, a slider) collapses into one entry.
    private static void CaptureUndoState(OpenSceneData sceneData, string current)
    {
        if (sceneData.UndoBaseline == null)
        {
            sceneData.UndoBaseline = current;
            return;
        }

        if (current == sceneData.UndoBaseline || EditorIsInteracting())
        {
            return;
        }

        sceneData.UndoStack.Add(sceneData.UndoBaseline);
        if (sceneData.UndoStack.Count > MaxUndo)
        {
            sceneData.UndoStack.RemoveAt(0);
        }
        sceneData.UndoBaseline = current;
        sceneData.RedoStack.Clear();
    }

    private void Undo()
    {
        var sd = _activeSceneData;
        if (sd == null || sd.UndoStack.Count == 0 || sd.UndoBaseline == null)
        {
            return;
        }

        string target = sd.UndoStack[^1];
        sd.UndoStack.RemoveAt(sd.UndoStack.Count - 1);
        sd.RedoStack.Add(sd.UndoBaseline);
        RestoreSnapshot(sd, target);
        sd.UndoBaseline = target;
    }

    private void Redo()
    {
        var sd = _activeSceneData;
        if (sd == null || sd.RedoStack.Count == 0 || sd.UndoBaseline == null)
        {
            return;
        }

        string target = sd.RedoStack[^1];
        sd.RedoStack.RemoveAt(sd.RedoStack.Count - 1);
        sd.UndoStack.Add(sd.UndoBaseline);
        RestoreSnapshot(sd, target);
        sd.UndoBaseline = target;
    }

    // Re-hydrates a scene from a snapshot in place (same Scene instance, so framebuffer/viewport bindings
    // stay valid). The selection is remapped by id when the entity still exists, else cleared.
    private void RestoreSnapshot(OpenSceneData sd, string json)
    {
        int? selId = _context.Selection is { IsValid: true } sel ? sel.Id : null;

        sd.Scene.Clear();
        new SceneSerializer(sd.Scene).DeserializeFromString(json);

        if (_activeSceneData == sd)
        {
            _context.ActiveScene = sd.Scene;
            _context.Selection = sd.Scene.EntityById(selId);
        }

        // Force the dirty flag to be recomputed against the saved baseline on the next check.
        sd.DirtyCheckCounter = 15;
    }

    // Records the current scene as the clean baseline (called after a successful save/open).
    private void UpdateSceneStatus()
    {
        if (_state == EditorState.Edit)
        {
            foreach (var sceneData in _openScenes)
            {
                if (++sceneData.DirtyCheckCounter < 15)
                {
                    continue;
                }
                sceneData.DirtyCheckCounter = 0;

                // Serialize once and reuse for both dirty-tracking and the undo history. Unsaved scenes
                // (no file yet) are always considered dirty, but still get history captured.
                string current = new SceneSerializer(sceneData.Scene).SerializeToString();
                sceneData.IsDirty = sceneData.FilePath == null
                    || sceneData.SavedSnapshot == null
                    || current != sceneData.SavedSnapshot;

                CaptureUndoState(sceneData, current);
            }
        }

        string projectName = Project.Active?.Config.Name ?? "Untitled Project";
        string title = $"Spot {Spot.Core.Application.Instance.EngineVersion} - {projectName}";
        if (_state == EditorState.Play)
        {
            title += " (Playing)";
        }
        
        if (title != _lastWindowTitle)
        {
            _lastWindowTitle = title;
            Spot.Core.Application.Instance.Window.NativeWindow.Title = title;
        }
    }


    private void NewScene()
    {
        var newSceneData = new OpenSceneData(_context);
        newSceneData.FocusNextFrame = true;
        _openScenes.Add(newSceneData);
        _activeSceneData = newSceneData;
        _lastEditedSceneData = newSceneData;
        _context.ActiveScene = newSceneData.Scene;
        _context.Selection = null;
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
        if (_activeSceneData != null) SaveSceneData(_activeSceneData);
    }

    // Saves one scene, prompting for a path when it has never been saved. Returns false only if the
    // user cancelled the Save As dialog, so callers can abort a pending close/quit.
    private bool SaveSceneData(OpenSceneData sceneData)
    {
        if (sceneData.FilePath == null)
        {
            string initialDir = Project.Active != null ? Project.Active.GetAssetDirectory() : "";
            sceneData.FilePath = Spot.Editor.Utils.FileDialogs.SaveFile("Spot Scene (*.sptscene)|*.sptscene", "sptscene", initialDir);
            if (sceneData.FilePath == null) return false;
        }

        new SceneSerializer(sceneData.Scene).Serialize(sceneData.FilePath);
        EnsureStartScene(sceneData.FilePath);
        sceneData.SavedSnapshot = new SceneSerializer(sceneData.Scene).SerializeToString();
        sceneData.IsDirty = false;
        sceneData.DirtyCheckCounter = 0;
        return true;
    }

    // Invoked when the window's close button is pressed. Vetoes the close (returning false) when any
    // scene has unsaved changes, raising the quit-confirmation dialog instead.
    private bool CanCloseApp()
    {
        if (_openScenes.Any(s => s.IsDirty))
        {
            _showQuitConfirm = true;
            return false;
        }
        return true;
    }

    private void RequestExit()
    {
        if (_openScenes.Any(s => s.IsDirty)) _showQuitConfirm = true;
        else Spot.Core.Application.Instance.Quit();
    }

    // Saves every dirty scene, prompting for a path where needed. Returns false if the user cancelled.
    private bool SaveAllDirtyWithPrompts()
    {
        foreach (var sceneData in _openScenes)
        {
            if (sceneData.IsDirty && !SaveSceneData(sceneData))
            {
                return false;
            }
        }
        return true;
    }

    private void DrawUnsavedChangesModals()
    {
        // Closing a single scene panel with unsaved changes.
        if (_pendingCloseScene != null) ImGui.OpenPopup("Unsaved Changes##scene");

        bool sceneModalOpen = true;
        if (ImGui.BeginPopupModal("Unsaved Changes##scene", ref sceneModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var scene = _pendingCloseScene;
            string name = scene?.FilePath != null ? System.IO.Path.GetFileNameWithoutExtension(scene.FilePath) : "Untitled";
            ImGui.TextUnformatted($"Save changes to '{name}' before closing?");
            ImGui.Spacing();
            if (ImGui.Button("Save", new Vector2(110, 0)))
            {
                if (scene != null && SaveSceneData(scene)) scene.IsOpen = false;
                _pendingCloseScene = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Don't Save", new Vector2(110, 0)))
            {
                if (scene != null) scene.IsOpen = false;
                _pendingCloseScene = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110, 0)))
            {
                _pendingCloseScene = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        else if (!sceneModalOpen)
        {
            _pendingCloseScene = null;
        }

        // Closing the whole application with unsaved changes.
        if (_showQuitConfirm) ImGui.OpenPopup("Unsaved Changes##quit");

        bool quitModalOpen = true;
        if (ImGui.BeginPopupModal("Unsaved Changes##quit", ref quitModalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            int dirtyCount = _openScenes.Count(s => s.IsDirty);
            ImGui.TextUnformatted($"{dirtyCount} scene(s) have unsaved changes. Exit anyway?");
            ImGui.Spacing();
            if (ImGui.Button("Save All & Exit", new Vector2(140, 0)))
            {
                if (SaveAllDirtyWithPrompts())
                {
                    _showQuitConfirm = false;
                    ImGui.CloseCurrentPopup();
                    Spot.Core.Application.Instance.Quit();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Exit Without Saving", new Vector2(160, 0)))
            {
                _showQuitConfirm = false;
                ImGui.CloseCurrentPopup();
                Spot.Core.Application.Instance.Quit();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110, 0)))
            {
                _showQuitConfirm = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        else if (!quitModalOpen)
        {
            _showQuitConfirm = false;
        }
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
        ActivateProjectPipeline();

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

using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Build;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Editor.Panels;
using Spot.DebugUI;
using Spot.DebugUI.Panels;
using Spot.Editor.Scenes;
using Spot.DebugUI.UI;
using Spot.Editor.UI;
using Spot.Events;
using Spot.DebugUI.Undo;

namespace Spot.Editor;


public class OpenSceneData : IUndoDocument
{
    public Scene Scene = new();
    public string? FilePath;
    public string? SavedSnapshot;
    public bool IsDirty = true;
    public int DirtyCheckCounter = 0;

    /// <inheritdoc />
    public long CleanStamp { get; set; }

    // The scene as it stood after the last change the undo history knows about. The periodic check
    // compares the scene against this, so anything that moved without going through the history is
    // caught and recorded as a coarse entry rather than silently becoming un-undoable.
    public string? LastPushSnapshot;
    public ViewportPanel ViewportPanel;
    public Framebuffer Framebuffer;
    public Framebuffer CameraPreviewFramebuffer;
    public EditorCamera EditorCamera = new();
    public bool IsOpen = true;
    public bool FocusNextFrame = false;
    public bool FirstFrame = true;

    // Whether this scene's viewport was actually visible last ImGui frame (not tabbed behind another panel).
    // The render pass reads it to skip re-rendering a hidden viewport (and its camera preview overlay).
    public bool ViewportVisible = true;

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

// One open UI document (a .sptui asset), shown as its own dockable tab. Mirrors OpenSceneData: the panel owns
// the offscreen framebuffer it renders into, and the tab closes via its title-bar 'x'.
public sealed class UIDocumentData : IUndoDocument
{
    public required Spot.UI.UIRoot Document;
    public required string Path;
    public required Spot.Editor.Panels.UICanvasPanel Panel;
    public bool IsOpen = true;
    public bool FirstFrame = true;
    public bool FocusNextFrame = true;

    /// <inheritdoc />
    public long CleanStamp { get; set; }

    public void Dispose() => Panel.Dispose();
}

public enum EditorState
{
    Edit,
    Play,
    Paused
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

    // The editor's single undo history. One history for the whole editor (not one per scene tab) so
    // Ctrl+Z always takes back the last thing the user did, whichever panel they did it in.
    private readonly UndoHistory _history = EditorHistory.Current;

    private readonly HierarchyPanel _hierarchyPanel;
    private readonly InspectorPanel _inspectorPanel;
    private readonly ViewportPanel _gamePanel;
    private readonly ConsolePanel _consolePanel;
    private readonly AssetBrowserPanel _assetBrowserPanel;
    private readonly ProjectSettingsPanel _projectSettingsPanel;
    private readonly UIHierarchyPanel _uiHierarchyPanel;
    private readonly ProfilerPanel _profilerPanel = new();
    private readonly HistoryPanel _historyPanel;
    private readonly Spot.DebugUI.Panels.AudioMixerPanel _audioMixerPanel = new();

    // Open UI documents, each shown as its own dockable tab (like scenes). The active one drives the shared
    // Hierarchy/Inspector while it is focused.
    private readonly List<UIDocumentData> _openUIDocuments = new();
    private UIDocumentData? _activeUIDocument;

    private Framebuffer? _gameFramebuffer;

    private List<OpenSceneData> _openScenes = new();
    private OpenSceneData? _activeSceneData = null;
    private OpenSceneData? _lastEditedSceneData = null;

    // One dockable node-graph editor window per open .sptcontroller asset.
    private readonly List<AnimatorControllerPanel> _animatorEditors = new();

    // Unsaved-changes confirmation state: a scene panel pending close, and the app-quit prompt.
    private OpenSceneData? _pendingCloseScene;
    private bool _showQuitConfirm;

    private string? _lastWindowTitle;

    // Per-panel visibility, toggled from View > Panels and by each window's close button.
    private bool _showGame = true;
    // Whether the Game panel was actually visible last ImGui frame. The render pass reads it to skip the
    // full extra scene render into the game framebuffer when the panel is tabbed behind another or closed.
    private bool _gameViewVisible;
    private bool _showHierarchy = true;
    private bool _showInspector = true;
    private uint _lastGameDockId;
    private bool _showConsole = true;

    // Set when something asks for the console (the ' key, routed here because the editor owns the
    // console's window). Consumed by the next ImGui pass, which reveals the panel, raises its dock tab
    // and puts the caret in the prompt.
    private bool _focusConsoleRequested;
    private bool _showAssetBrowser = true;
    private bool _showProjectSettings = false;
    private bool _showProfiler = false;
    private bool _showAudioMixer = false;
    private bool _showHistory = false;

    // When on, a detected script edit triggers a rebuild+reload automatically once edits settle; otherwise the
    // user reloads from Project > Reload Scripts (Ctrl+R). The settle timestamp debounces bursts of file events.
    private bool _autoReloadScripts = true;
    private long _scriptsChangedAtTick;

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
        _uiHierarchyPanel = new UIHierarchyPanel(_context);
        _historyPanel = new HistoryPanel(_history);
        _assetBrowserPanel.OnAssetOpened += OpenAsset;

        // Undo wiring: the history restores the selection around each action (so undoing a delete also
        // re-selects what came back), and the tracker records field edits into this same history.
        _history.Selection = new ContextSelectionStore(_context);
        UndoTracker.History = _history;

        // Panels record actions without knowing about scene tabs, so tell the history how to map a
        // scene back to the tab that owns it — that is what puts the "*" on the right tab.
        EditorHistory.SceneDocumentResolver =
            scene => _openScenes.FirstOrDefault(s => ReferenceEquals(s.Scene, scene));

        // Any history movement leaves the catch-all's baselines out of date. Without this, a precise
        // action recorded by a panel would be followed moments later by the periodic check noticing the
        // same change and recording a second, coarse entry for it — costing the user two undos for one
        // edit. Flagged rather than recomputed here so a multi-step jump re-serializes once, not once
        // per step, and so it can reuse the serialization the check already performs.
        _history.Changed += () => _baselinesStale = true;

        // The mixer edits AudioMixer live; the bus layout is project data, so an edit writes it straight to the
        // .sptproj (there is no manual "Save Project" action, matching Project Settings).
        _audioMixerPanel.LayoutChanged = SaveAudioMixerLayout;
        Spot.DebugUI.UI.WidgetInspector.OpenDocumentRequested = OpenUIDocument;

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

        // The editor docks the console as a native panel, so take ownership of its window: the engine
        // then stops drawing its own floating "Console" (ImGui would merge the two by name and draw the
        // body — prompt included — twice) and stops capturing input from the console's open state, which
        // in the editor had no way to be dismissed and left the game's input dead. The ' key now reveals
        // and focuses the docked panel instead.
        Spot.Core.Application.Instance.Console.SetHost(FocusConsolePanel);

        _gameFramebuffer = new Framebuffer(1280, 720);
        _gamePanel.SetFramebuffer(_gameFramebuffer);

        LoadStartScene();
    }

    // Reveals the docked Console panel and hands it the keyboard, the editor's answer to the engine's
    // "open the console" request (the ' key). Runs during event handling, before OnUpdate, so dropping the
    // Game panel's input focus is picked up by the same frame's focus transition: the cursor is freed and
    // game input suppressed, exactly as Escape does. Without that, keys typed into the prompt would also
    // drive the game and the camera would stay on mouse-look.
    private void FocusConsolePanel()
    {
        _focusConsoleRequested = true;
        _gamePanelFocused = false;
    }

    // Routes a double-clicked asset to the right editor: scenes open as tabs, animator controllers open as
    // node-graph windows. Anything else is handled by the asset browser's own fallback.
    private void OpenAsset(string filepath)
    {
        if (filepath.EndsWith(".sptcontroller", System.StringComparison.OrdinalIgnoreCase))
        {
            OpenAnimatorController(filepath);
        }
        else if (filepath.EndsWith(".sptui", System.StringComparison.OrdinalIgnoreCase))
        {
            OpenUIDocument(filepath);
        }
        else
        {
            OpenSceneAsset(filepath);
        }
    }

    // Opens a .sptui document in its own tab (or focuses the tab if it is already open), like opening a scene.
    // UISerializer.Load never throws (it logs and returns an empty document on failure), so this is safe.
    private void OpenUIDocument(string filepath)
    {
        UIDocumentData? existing = _openUIDocuments.FirstOrDefault(
            d => string.Equals(d.Path, filepath, System.StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.IsOpen = true;
            existing.FocusNextFrame = true;
            SetActiveUIDocument(existing);
            _showHierarchy = true;
            return;
        }

        Spot.UI.UIRoot document = Spot.UI.Serialization.UISerializer.Load(filepath);
        var data = new UIDocumentData
        {
            Document = document,
            Path = filepath,
            Panel = new UICanvasPanel(_context) { Document = document, DocumentPath = filepath },
        };
        _openUIDocuments.Add(data);
        SetActiveUIDocument(data);
        _showHierarchy = true;
    }

    // Makes a UI document the active one: points the shared context (Hierarchy/Inspector/save) at it. Switching
    // documents clears the widget selection, so a stale widget from another document is never shown.
    private void SetActiveUIDocument(UIDocumentData? data)
    {
        if (ReferenceEquals(_activeUIDocument, data)) return;

        _activeUIDocument = data;
        _context.EditingDocument = data?.Document;
        _context.EditingDocumentPath = data?.Path;
        _context.SelectedWidget = null;
        if (data != null) _context.HierarchyTarget = HierarchyTarget.UI;
    }

    // Writes the active UI document back to its source path. Called on Ctrl+S while a document tab is focused.
    private void SaveUIDocument()
    {
        if (_activeUIDocument == null) return;

        try
        {
            Spot.UI.Serialization.UISerializer.Save(_activeUIDocument.Document, _activeUIDocument.Path);
            Log.Info("Saved UI document '{0}'.", System.IO.Path.GetFileName(_activeUIDocument.Path));
        }
        catch (System.Exception ex)
        {
            Log.Error("Failed to save UI document '{0}': {1}", _activeUIDocument.Path, ex.Message);
        }
    }

    private void OpenAnimatorController(string filepath)
    {
        var existing = _animatorEditors.FirstOrDefault(
            e => string.Equals(e.Path, filepath, System.StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            _animatorEditors.Add(new AnimatorControllerPanel(filepath));
        }
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
    private void ActivateProjectPipeline()
    {
        var project = Project.Active;
        if (project == null)
        {
            return;
        }

        Spot.Assets.AssetDatabase.Refresh(project.GetAssetDirectory());
        Spot.Assets.AssetDatabase.InstallLibraryResolver(System.IO.Path.Combine(project.ProjectDirectory, Spot.Core.ProjectStructure.LibraryFolder));

        LoadProjectAssembly(project);
        StartScriptWatcher(project);
    }

    // The collectible host for the active project's script assembly. Loading through it (instead of a plain
    // Assembly.Load into the default context) lets the editor reload scripts without restarting.
    private static readonly ScriptHost s_scriptHost = new();

    private static void LoadProjectAssembly(Project project)
    {
        string? dll = FindProjectAssembly(project);
        if (dll != null)
        {
            s_scriptHost.Load(dll);
        }
    }

    // The newest built <Name>.dll under the project's bin tree, or null when the project hasn't been built.
    // Checks two locations because a project inside a solution whose Directory.Build.props sets
    // BaseOutputPath may redirect builds to a sibling repo-level bin/<ProjectName>/ folder rather
    // than the project-local bin/.
    private static string? FindProjectAssembly(Project project)
    {
        string dllName = project.Config.Name + ".dll";

        // 1. Project-local bin/ (standard layout for standalone projects).
        string? found = FindNewestDll(System.IO.Path.Combine(project.ProjectDirectory, "bin"), dllName);
        if (found != null) return found;

        // 2. Sibling repo-level bin/<ProjectName>/ (Directory.Build.props with BaseOutputPath
        //    = $(MSBuildThisFileDirectory)bin\$(MSBuildProjectName)\ redirects there).
        string? parent = System.IO.Path.GetDirectoryName(
            project.ProjectDirectory.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar));
        if (parent != null)
        {
            found = FindNewestDll(System.IO.Path.Combine(parent, "bin", project.Config.Name), dllName);
        }

        return found;
    }

    private static string? FindNewestDll(string dir, string dllName)
    {
        if (!System.IO.Directory.Exists(dir)) return null;
        var dlls = System.IO.Directory.GetFiles(dir, dllName, System.IO.SearchOption.AllDirectories);
        return System.Linq.Enumerable.FirstOrDefault(
            System.Linq.Enumerable.OrderByDescending(dlls, f => System.IO.File.GetLastWriteTimeUtc(f)));
    }

    // Set by the script file watcher when a .cs under Assets changes, so the editor can offer (or perform) a
    // reload without restarting. Read on the UI thread; the reload itself runs there too.
    private volatile bool _scriptsOutOfDate;
    private System.IO.FileSystemWatcher? _scriptWatcher;

    // Watches the project's Assets tree for script edits. Flags the editor to reload rather than reloading from
    // the watcher's own thread, so the actual swap always happens on the UI thread mid-frame.
    private void StartScriptWatcher(Project project)
    {
        StopScriptWatcher();

        string assets = project.GetAssetDirectory();
        if (!System.IO.Directory.Exists(assets))
        {
            return;
        }

        try
        {
            var watcher = new System.IO.FileSystemWatcher(assets, "*.cs")
            {
                IncludeSubdirectories = true,
                NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.Size,
            };
            void OnChange()
            {
                _scriptsOutOfDate = true;
                _scriptsChangedAtTick = System.Environment.TickCount64;
            }

            watcher.Changed += (_, _) => OnChange();
            watcher.Created += (_, _) => OnChange();
            watcher.Deleted += (_, _) => OnChange();
            watcher.Renamed += (_, _) => OnChange();
            watcher.EnableRaisingEvents = true;
            _scriptWatcher = watcher;
        }
        catch (System.Exception ex)
        {
            // A missing directory or a platform quirk must never take the editor down; scripts can still be
            // reloaded manually from the menu.
            Spot.Core.Log.CoreWarn("Could not watch project scripts for changes: {0}", ex.Message);
        }
    }

    private void StopScriptWatcher()
    {
        _scriptWatcher?.Dispose();
        _scriptWatcher = null;
    }

    /// <summary>
    /// Rebuilds the active project and swaps in the freshly compiled script assembly without restarting the
    /// editor, preserving each live script's authored field values and entity references across the reload.
    /// Only runs in edit mode; a failed build or load logs and leaves the current scripts in place.
    /// </summary>
    private void ReloadScripts()
    {
        Project? project = Spot.Core.Project.Active;
        if (project == null || _state != EditorState.Edit)
        {
            return;
        }

        _scriptsOutOfDate = false;
        Spot.Core.Log.Info("Reloading scripts...");

        // 1. Recompile. A failed build leaves the running scripts untouched.
        var result = Spot.Build.ProjectBuilder.Build(
            project,
            Spot.Build.BuildPlatform.Windows,
            onOutput: LogBuildOutput,
            onError: msg => Spot.Core.Log.Error($"[Build] {msg}"),
            fastDebug: true);

        if (!result.Success)
        {
            Spot.Core.Log.Error("Script reload aborted: build failed.");
            return;
        }

        // 2. Snapshot every live script's fields and drop the instance references, so the old load context has
        //    nothing keeping it alive and can be collected.
        var snapshots = new List<ScriptReloadSnapshot>();
        foreach (OpenSceneData sceneData in _openScenes)
        {
            foreach (Entity entity in sceneData.Scene.View<ScriptComponent>())
            {
                var comp = entity.GetComponent<ScriptComponent>();
                foreach (ScriptInstance item in comp.Items)
                {
                    System.Text.Json.Nodes.JsonObject? fields =
                        item.Instance != null ? ComponentSerialization.SerializeMembers(item.Instance) : null;
                    snapshots.Add(new ScriptReloadSnapshot(sceneData.Scene, entity, item, fields));
                    item.Instance = null;
                }
            }
        }

        // 3. Swap the assembly.
        s_scriptHost.Unload();
        string? dll = FindProjectAssembly(project);
        if (dll == null || !s_scriptHost.Load(dll))
        {
            Spot.Core.Log.Error("Script reload failed to load the rebuilt assembly; scripts are now unresolved.");
            return;
        }

        // 4. Re-resolve each script from the new assembly and restore its fields. Entity references are rebound
        //    per scene through a SceneReferences map keyed on the entities' stable ids.
        foreach (var group in System.Linq.Enumerable.GroupBy(snapshots, s => s.Scene))
        {
            var refs = new SceneReferences();
            foreach (Entity entity in group.Key.View<LabelComponent>())
            {
                refs.Register(entity.EnsurePersistentId(), entity);
            }

            foreach (ScriptReloadSnapshot snap in group)
            {
                EntityBehaviour? instance = ScriptResolver.Create(snap.Item.Guid, snap.Item.ClassName, snap.Entity);
                snap.Item.Instance = instance;
                if (instance != null && snap.Fields != null)
                {
                    ComponentSerialization.ApplyMembers(instance, snap.Fields, refs);
                }
            }

            refs.ResolveDeferred();
        }

        Spot.Core.Log.Info("Scripts reloaded.");
    }

    private readonly record struct ScriptReloadSnapshot(
        Scene Scene, Entity Entity, ScriptInstance Item, System.Text.Json.Nodes.JsonObject? Fields);

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

        // Restore the previous working session (open scenes/UI/animator, panels, cameras) when one was
        // saved and at least one of its scenes still exists; otherwise fall back to the project start scene.
        var session = Spot.Editor.Utils.EditorSession.Load(Project.Active);
        if (session != null && RestoreSession(session))
        {
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

    // Reopens the scenes / UI documents / animator windows the user had open, restores each scene's editor
    // camera and the panel visibility, and refocuses the previously active scene tab. Missing files are
    // skipped (they may have been deleted/renamed since). Returns false when no saved scene still exists, so
    // the caller can fall back to the project start scene.
    private bool RestoreSession(Spot.Editor.Utils.EditorSessionState session)
    {
        int openedScenes = 0;
        foreach (string path in session.OpenScenes)
        {
            if (!System.IO.File.Exists(path))
            {
                Log.CoreWarn("Skipping missing scene from last session: '{0}'.", path);
                continue;
            }
            int before = _openScenes.Count;
            OpenSceneAsset(path);
            if (_openScenes.Count > before) openedScenes++;
        }

        if (openedScenes == 0)
        {
            return false;
        }

        // Restore each viewport's editor camera by matching the saved pose to the opened scene tab.
        foreach (var cam in session.Cameras)
        {
            string normPath = System.IO.Path.GetFullPath(cam.Path).ToLowerInvariant();
            var data = _openScenes.FirstOrDefault(
                s => s.FilePath != null && System.IO.Path.GetFullPath(s.FilePath).ToLowerInvariant() == normPath);
            if (data == null) continue;

            var c = data.EditorCamera;
            c.Is3D = cam.Is3D;
            c.Position = new System.Numerics.Vector3(cam.PosX, cam.PosY, cam.PosZ);
            c.Pitch = cam.Pitch;
            c.Yaw = cam.Yaw;
            c.SetZoom(cam.Zoom);
        }

        // Refocus the tab that was active last session (OpenSceneAsset already left the last-opened one active).
        if (session.ActiveScene != null)
        {
            string activeNorm = System.IO.Path.GetFullPath(session.ActiveScene).ToLowerInvariant();
            var active = _openScenes.FirstOrDefault(
                s => s.FilePath != null && System.IO.Path.GetFullPath(s.FilePath).ToLowerInvariant() == activeNorm);
            if (active != null)
            {
                active.FocusNextFrame = true;
                _activeSceneData = active;
                _lastEditedSceneData = active;
                _context.ActiveScene = active.Scene;
            }
        }

        foreach (string path in session.OpenUIDocuments)
        {
            if (System.IO.File.Exists(path)) OpenUIDocument(path);
        }
        if (session.ActiveUIDocument != null)
        {
            var doc = _openUIDocuments.FirstOrDefault(
                d => string.Equals(d.Path, session.ActiveUIDocument, System.StringComparison.OrdinalIgnoreCase));
            if (doc != null)
            {
                doc.FocusNextFrame = true;
                SetActiveUIDocument(doc);
            }
        }

        foreach (string path in session.OpenAnimators)
        {
            if (System.IO.File.Exists(path)) OpenAnimatorController(path);
        }

        _showGame = session.ShowGame;
        _showHierarchy = session.ShowHierarchy;
        _showInspector = session.ShowInspector;
        _showConsole = session.ShowConsole;
        _showAssetBrowser = session.ShowAssetBrowser;
        _showProjectSettings = session.ShowProjectSettings;
        _showAudioMixer = session.ShowAudioMixer;
        _showHistory = session.ShowHistory;

        return true;
    }

    // Captures the current working session so the next launch of this project can restore it. No-op without
    // an active project (an unsaved scratch project has nowhere to write).
    private void SaveSession()
    {
        var project = Project.Active;
        if (project == null) return;

        var state = new Spot.Editor.Utils.EditorSessionState
        {
            ActiveScene = _activeSceneData?.FilePath,
            ActiveUIDocument = _activeUIDocument?.Path,
            ShowGame = _showGame,
            ShowHierarchy = _showHierarchy,
            ShowInspector = _showInspector,
            ShowConsole = _showConsole,
            ShowAssetBrowser = _showAssetBrowser,
            ShowProjectSettings = _showProjectSettings,
            ShowAudioMixer = _showAudioMixer,
            ShowHistory = _showHistory,
        };

        foreach (var sceneData in _openScenes)
        {
            if (sceneData.FilePath == null) continue;
            state.OpenScenes.Add(sceneData.FilePath);
            var c = sceneData.EditorCamera;
            state.Cameras.Add(new Spot.Editor.Utils.SceneCameraState
            {
                Path = sceneData.FilePath,
                PosX = c.Position.X,
                PosY = c.Position.Y,
                PosZ = c.Position.Z,
                Pitch = c.Pitch,
                Yaw = c.Yaw,
                Zoom = c.ZoomLevel,
                Is3D = c.Is3D,
            });
        }

        foreach (var doc in _openUIDocuments) state.OpenUIDocuments.Add(doc.Path);
        foreach (var anim in _animatorEditors) state.OpenAnimators.Add(anim.Path);

        Spot.Editor.Utils.EditorSession.Save(project, state);
    }

    public override void OnUpdate(float deltaTime)
    {
        foreach (var sceneData in _openScenes)
        {
            bool isActiveSim = _state != EditorState.Edit && sceneData == _activeSceneData;
            if (isActiveSim)
            {
                // Gate game input to the Game panel. _gamePanelFocused is evaluated from the
                // previous frame's ImGui pass (one-frame lag is imperceptible to the user).
                // Handle focus transitions before setting suppression so cursor management runs
                // while InputBlocked still matches the previous frame's state.
                if (_gamePanelFocused != _prevGamePanelFocused)
                {
                    _prevGamePanelFocused = _gamePanelFocused;
                    if (_gamePanelFocused)
                    {
                        // Gaining focus: unsuppress input first, then restore game's cursor lock.
                        Spot.Core.Input.Suppressed = false;
                        Spot.Core.Input.RestoreCursor();
                    }
                    else
                    {
                        // Losing focus: release cursor while not yet suppressed, then suppress.
                        Spot.Core.Input.ReleaseCursor();
                        Spot.Core.Input.Suppressed = true;
                    }
                }
                else
                {
                    // No transition: just maintain current suppression state.
                    Spot.Core.Input.Suppressed = !_gamePanelFocused;
                }

                // In play mode: run the full system stack for the active scene.
                if (!_isPlayPaused)
                    sceneData.Scene.UpdateRuntime(deltaTime);
                else if (_playStep)
                {
                    sceneData.Scene.UpdateRuntime(1f / 60f);
                    _playStep = false;
                }
                // Paused with no step pending: freeze (do nothing).
            }
            else
            {
                // Edit mode (or non-active scene): lightweight tick — no scripts/physics.
                sceneData.Scene.OnUpdate(deltaTime);
                sceneData.Scene.FlushDestroyed();
            }
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
            bool showAll = Spot.Physics.PhysicsDebug.ShowColliders && sceneData == _activeSceneData;
            bool showSelected = _context.Selection.HasValue && sceneData == _activeSceneData;

            if (showAll || showSelected)
            {
                static void DrawBox3DWire(Vector3 min, Vector3 max)
                {
                    Vector4 c = new(0.0f, 1.0f, 0.0f, 1.0f);
                    float t = 0.02f;
                    Renderer2D.DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, min.Y, max.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(min.X, min.Y, max.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, min.Y, min.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, max.Y, max.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, max.Y, min.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, max.Y, min.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z), c, t);
                    Renderer2D.DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, max.Y, max.Z), c, t);
                }

                void DrawEntityColliders(Entity entity)
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
                        DrawBox3DWire(bounds.Min, bounds.Max);
                    }

                    if (entity.TryGetComponent(out Spot.Scenes.RelationshipComponent? rel))
                    {
                        foreach (var child in rel.Children)
                            DrawEntityColliders(child);
                    }
                }

                Renderer2D.BeginScene(sceneData.EditorCamera.ViewProjection);

                if (showAll)
                {
                    // ShowColliders is on: draw every entity in the scene, not just the selection.
                    foreach (var entity in sceneData.Scene.View<Spot.Physics.BoxCollider2DComponent, TransformComponent>())
                    {
                        if (!entity.IsActiveInHierarchy()) continue;
                        var transform = entity.GetComponent<TransformComponent>();
                        var collider = entity.GetComponent<Spot.Physics.BoxCollider2DComponent>();
                        var bounds = collider.GetWorldBounds(new Vector2(transform.WorldPosition.X, transform.WorldPosition.Y), new Vector2(transform.WorldScale.X, transform.WorldScale.Y));
                        Renderer2D.DrawRect(bounds.Center, bounds.HalfExtents * 2.0f, new Vector4(0.0f, 1.0f, 0.0f, 1.0f), 0.02f);
                    }

                    foreach (var entity in sceneData.Scene.View<Spot.Physics.BoxCollider3DComponent, TransformComponent>())
                    {
                        if (!entity.IsActiveInHierarchy()) continue;
                        var transform = entity.GetComponent<TransformComponent>();
                        var collider = entity.GetComponent<Spot.Physics.BoxCollider3DComponent>();
                        var bounds = collider.GetWorldBounds(transform.WorldPosition, transform.WorldScale);
                        DrawBox3DWire(bounds.Min, bounds.Max);
                    }
                }
                else
                {
                    // ShowColliders is off: draw only the selected entity's colliders as a selection gizmo.
                    DrawEntityColliders(_context.Selection!.Value);
                }

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
            if (_context.Selection.HasValue && _context.Selection.Value.HasComponent<CameraComponent>() && sceneData == _activeSceneData && sceneData.ViewportVisible)
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

        // Only render the game view when its panel is actually visible. When it is tabbed behind another
        // panel (or closed) this would otherwise be a full extra scene render — shadow pass, meshes, and
        // post-processing — every frame; skipping it is a large editor win and the panel keeps its last
        // image until shown again.
        var gameScene = _gameViewVisible
            ? (_state == EditorState.Play ? _context.ActiveScene : _lastEditedSceneData?.Scene)
            : null;

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

        // Render each open UI document into its own offscreen target so its tab shows an up-to-date picture.
        foreach (UIDocumentData data in _openUIDocuments)
        {
            if (data.IsOpen) data.Panel.RenderDocument();
        }

        var window = Spot.Core.Application.Instance.Window;
        Renderer.SetViewport(0, 0, (uint)window.Width, (uint)window.Height);
        Renderer.SetClearColor(0.0f, 0.0f, 0.0f, 1.0f);
    }
    public override void OnImGuiRender()
    {
        // Only edit-mode changes belong in the history. Panels still draw and still edit the live scene
        // during play, but all of that is thrown away when play stops, so recording it would fill the
        // history with entries that undo into state the user never authored.
        _history.Enabled = _state == EditorState.Edit;

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
        // A single Hierarchy panel that shows the scene's entities or the open UI document's widgets, switching
        // automatically with the active view (clicking the UI Canvas shows the UI; clicking a scene viewport
        // shows entities) — no manual toggle needed.
        if (_showHierarchy)
        {
            ImGui.Begin("Hierarchy", ref _showHierarchy, ImGuiWindowFlags.NoCollapse);
            if (_context.EditingDocument != null && _context.HierarchyTarget == HierarchyTarget.UI)
                _uiHierarchyPanel.DrawContents();
            else
                _hierarchyPanel.DrawContents();
            ImGui.End();
        }

        DrawUIDocumentWindows(dockspaceId);

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
            sceneData.ViewportVisible = open;

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

            // Focusing a scene viewport switches the shared Hierarchy panel back to the scene's entities.
            if (isFocused)
            {
                _context.HierarchyTarget = HierarchyTarget.Scene;
            }

            if (open)
            {
                sceneData.ViewportPanel.OnImGuiRender(handleInput: isFocused || isHovered);
            }
            ImGui.End();
        }

        // Remove closed scenes. Their history entries go too: an entry cannot be plucked out of the
        // middle of the list (the rest would no longer replay in order), so closing a document discards
        // the oldest entry touching it and everything newer. Undoing into a scene that is gone would be
        // a write into a dead object.
        foreach (var closing in _openScenes.Where(s => !s.IsOpen))
        {
            int dropped = _history.DiscardDocument(closing);
            if (dropped > 0)
            {
                Spot.Core.Log.Info(
                    "Closed a scene with {0} undo {1} in the history; they were discarded.",
                    dropped, dropped == 1 ? "entry" : "entries");
            }
        }

        _openScenes.RemoveAll(s => !s.IsOpen);
        if (_activeSceneData is null || !_openScenes.Contains(_activeSceneData))
        {
            _activeSceneData = _openScenes.Count > 0 ? _openScenes[0] : null;
            _context.ActiveScene = _activeSceneData?.Scene;
            if (_activeSceneData != null) _lastEditedSceneData = _activeSceneData;
        }

        _gameViewVisible = false;
        if (_showGame)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));
            bool open = ImGui.Begin("Game", ref _showGame, ImGuiWindowFlags.NoCollapse);
            _gameViewVisible = open;
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

                bool playing = _state != EditorState.Edit;
                var drawList = ImGui.GetWindowDrawList();

                // Click-to-focus: while in play mode, clicking the Game panel gives it input focus.
                // The cursor is free when the panel is not focused, so hover detection works normally.
                bool gameWinHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.None);
                if (playing && !_gamePanelFocused && gameWinHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _gamePanelFocused = true;
                }

                // Overlay: "Click to control" when in play mode but the panel has no input focus.
                if (playing && !_gamePanelFocused && size.X > 0 && size.Y > 0)
                {
                    const string clickMsg = "Click to control";
                    var textSize = ImGui.CalcTextSize(clickMsg);
                    var textPos = new Vector2(
                        imageTopLeft.X + (size.X - textSize.X) * 0.5f,
                        imageTopLeft.Y + (size.Y - textSize.Y) * 0.5f);
                    var pad = new Vector2(10.0f, 6.0f);
                    drawList.AddRectFilled(textPos - pad, textPos + textSize + pad,
                        ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.55f)), 4.0f);
                    drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.9f)), clickMsg);
                }

                // Overlay: subtle "Esc to release" hint at the bottom when the panel holds cursor lock.
                if (playing && _gamePanelFocused && Spot.Core.Input.CursorLocked && size.X > 0 && size.Y > 0)
                {
                    const string escMsg = "Esc to release cursor";
                    var textSize = ImGui.CalcTextSize(escMsg);
                    var textPos = new Vector2(
                        imageTopLeft.X + (size.X - textSize.X) * 0.5f,
                        imageTopLeft.Y + size.Y - textSize.Y - 12.0f);
                    var pad = new Vector2(8.0f, 4.0f);
                    drawList.AddRectFilled(textPos - pad, textPos + textSize + pad,
                        ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.40f)), 3.0f);
                    drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.55f)), escMsg);
                }

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

        if (_focusConsoleRequested)
        {
            // Reveal the panel before it is submitted this frame, so focusing it by name lands on a live
            // window: it raises the dock tab, and the console puts the caret in its prompt.
            _focusConsoleRequested = false;
            _showConsole = true;
            ImGui.SetWindowFocus("Console");
            Spot.Core.Application.Instance.Console.RequestInputFocus();
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

        if (_showProfiler)
        {
            _profilerPanel.OnImGuiRender();
        }

        _audioMixerPanel.OnImGuiRender(ref _showAudioMixer);

        _historyPanel.UnattributedChanges = _unattributedChanges;
        _historyPanel.OnImGuiRender(ref _showHistory);

        if (_showAssetBrowser)
        {
            bool open = ImGui.Begin("Asset Browser", ref _showAssetBrowser, ImGuiWindowFlags.NoCollapse);
            if (open)
            {
                _assetBrowserPanel.OnImGuiRender(asWindow: false);
            }
            ImGui.End();
        }

        // Animator-controller node-graph editors: each is its own window, removed when its close button is hit.
        for (int i = _animatorEditors.Count - 1; i >= 0; i--)
        {
            bool editorOpen = true;
            _animatorEditors[i].OnImGuiRender(ref editorOpen);
            if (!editorOpen)
            {
                _animatorEditors.RemoveAt(i);
            }
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

        // Prevent ImGui's backend from resetting the hardware cursor while the game holds a cursor
        // lock. The fly-mode viewports manage this flag themselves (NoMouseCursorChange); here we
        // apply the same guard for game-side cursor lock so the cursor stays hidden during play.
        // Placed last so all viewport panels have already had their turn with the flag.
        bool gameLocksCursor = _state != EditorState.Edit && _gamePanelFocused && Spot.Core.Input.CursorLocked;
        if (gameLocksCursor)
        {
            ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;
            _gameHeldImGuiLock = true;
        }
        else if (_gameHeldImGuiLock)
        {
            ImGui.GetIO().ConfigFlags &= ~ImGuiConfigFlags.NoMouseCursorChange;
            _gameHeldImGuiLock = false;
        }

        // Last statement of the frame: commit a field edit whose interaction has finished, and otherwise
        // refresh the selection baseline so the next recorded action knows what was selected when the
        // user started it. Must come after every panel has drawn.
        UndoTracker.EndFrame();
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
        Spot.Core.Application.Instance.Console.SetHost(null);
        Spot.Editor.Utils.EditorSettings.Save(Spot.Core.Application.Instance.Window.NativeWindow);
        SaveSession();

        StopScriptWatcher();
        foreach (var sceneData in _openScenes) sceneData.Dispose();
        _gameFramebuffer?.Dispose();
        _inspectorPanel.Dispose();
        foreach (UIDocumentData data in _openUIDocuments) data.Dispose();
        _context.ActiveScene?.OnExit();
    }

    // JSON snapshot of the active scene taken when Play is pressed; restored on Stop.
    private string? _prePlaySnapshot;
    private bool _isPlayPaused;
    private bool _playStep;
    private bool _gameHeldImGuiLock;
    // Whether the Game panel currently has input focus (set by clicking into it).
    // When false the game receives no keyboard/mouse input and the cursor is free.
    private bool _gamePanelFocused;
    private bool _prevGamePanelFocused;

    private void OnPlay()
    {
        if (_state != EditorState.Edit || Project.Active == null || _activeSceneData == null) return;

        // If no game assembly is loaded yet (first Play after a clean checkout, or the project has
        // never been built), compile it now so scripts actually run. This is synchronous and blocks
        // the UI for a normal incremental build (~1-3 s); subsequent plays use the cached DLL.
        if (s_scriptHost.Assembly == null)
        {
            EnsureScriptsBuilt(Project.Active);
        }

        // Drop any in-flight edit rather than recording it: play mode is about to replace this state.
        UndoTracker.Abandon();

        _prePlaySnapshot = new SceneSerializer(_activeSceneData.Scene).SerializeToString();
        _isPlayPaused = false;
        _playStep = false;
        _gamePanelFocused = false;
        _prevGamePanelFocused = false;
        _state = EditorState.Play;
        _showGame = true;
        Spot.Core.Log.Info("Entering play mode.");
    }

    private void OnStop()
    {
        if (_state == EditorState.Edit || _activeSceneData == null) return;

        var scene = _activeSceneData.Scene;
        ScriptSystem.DestroyAll(scene);
        foreach (var e in scene.View<AudioSourceComponent>())
        {
            var src = e.GetComponent<AudioSourceComponent>();
            if (src.IsPlaying) src.Stop();
        }
        scene.TeardownPhysics();
        RestoreSnapshot(_activeSceneData, _prePlaySnapshot!);
        SyncSnapshotBaselines();
        _prePlaySnapshot = null;
        _isPlayPaused = false;
        _gamePanelFocused = false;
        _prevGamePanelFocused = false;
        // Clear suppression first so CursorLocked can actually apply the cursor-free state.
        Spot.Core.Input.Suppressed = false;
        // Return the hardware cursor to normal; game scripts never get a chance to do this on Stop.
        Spot.Core.Input.CursorLocked = false;
        _state = EditorState.Edit;
        Spot.Core.Log.Info("Exited play mode.");
    }

    // Compiles the project scripts (dotnet build → bin/) and loads the resulting assembly so that
    // ScriptResolver can instantiate game types. Called once on first Play when no assembly is loaded.
    private void EnsureScriptsBuilt(Project project)
    {
        Spot.Core.Log.Info("Building project scripts for play mode...");

        if (!BuildScriptsDll(project))
        {
            Spot.Core.Log.Error("Script build failed; game scripts will not run. See the console for build errors.");
            return;
        }

        string? dll = FindProjectAssembly(project);
        if (dll == null)
        {
            Spot.Core.Log.Error("Script build succeeded but the output DLL was not found under bin/. Cannot load scripts.");
            return;
        }

        if (!s_scriptHost.Load(dll))
        {
            Spot.Core.Log.Error("Failed to load project scripts from '{0}'.", dll);
            return;
        }

        // Re-resolve any ScriptInstance whose Instance is still null: the assembly is now available.
        foreach (var sceneData in _openScenes)
        {
            foreach (Entity entity in sceneData.Scene.View<ScriptComponent>())
            {
                var comp = entity.GetComponent<ScriptComponent>();
                foreach (ScriptInstance item in comp.Items)
                {
                    if (item.Instance == null)
                    {
                        item.Instance = ScriptResolver.Create(item.Guid, item.ClassName, entity);
                    }
                }
            }
        }
    }

    // Matches msbuild's trailing "    0 Warning(s)" / "    1 Error(s)" count lines, which carry the words
    // "warning"/"error" without being diagnostics themselves.
    private static readonly System.Text.RegularExpressions.Regex s_buildSummaryLine = new(
        @"^\s*\d+\s+(Warning|Error)\(s\)\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Mirrors a build line into the console, but only when it carries a diagnostic. msbuild narrates every
    // build ("Determining projects to restore...", "<project> -> <path>", "Build succeeded.", the
    // "0 Warning(s)" summary, "Time Elapsed ..."), which buries the console on every Play and every script
    // reload while telling the user nothing: the editor already reports whether the build worked.
    private static void LogBuildOutput(string line)
    {
        bool diagnostic =
            (line.Contains("error", StringComparison.OrdinalIgnoreCase)
             || line.Contains("warning", StringComparison.OrdinalIgnoreCase))
            && !s_buildSummaryLine.IsMatch(line);

        if (diagnostic)
        {
            Spot.Core.Log.Info("[Build] {0}", line);
        }
    }

    // Runs `dotnet build` on the project's .csproj so scripts compile into bin/. Returns true on
    // success. Synchronous — the caller's frame loop freezes for the duration of the build.
    private static bool BuildScriptsDll(Project project)
    {
        if (string.IsNullOrEmpty(project.ProjectDirectory))
        {
            return false;
        }

        // Keep EngineBin in sync with the running engine so scripts compile against the current API.
        Spot.Build.ProjectGenerator.Generate(project);

        string csprojFile = project.Config.Name + ".csproj";
        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csprojFile}\" -c Debug --nologo",
            WorkingDirectory = project.ProjectDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = new System.Diagnostics.Process { StartInfo = processInfo };
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    LogBuildOutput(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Spot.Core.Log.Error("[Build] {0}", e.Data);
            };

            if (!process.Start())
            {
                Spot.Core.Log.Error("Failed to start dotnet build process.");
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.Exception ex)
        {
            Spot.Core.Log.Error("Script build threw an exception: {0}", ex.Message);
            return false;
        }
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

        ImGuiDock.igDockBuilderDockWindow("Hierarchy", rightTop);
        ImGuiDock.igDockBuilderDockWindow("Properties", rightBottom);
        // Tabbed behind Properties: it is a reference view, wanted on demand rather than always open.
        ImGuiDock.igDockBuilderDockWindow($"{Spot.DebugUI.UI.EditorIcons.Rotate}  History", rightBottom);
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

    // Draws each open UI document as its own dockable tab (mirroring the scene tabs). Focusing a tab makes it
    // the active document, so the shared Hierarchy/Inspector follow it; closing a tab disposes its panel.
    private void DrawUIDocumentWindows(uint dockspaceId)
    {
        for (int i = 0; i < _openUIDocuments.Count; i++)
        {
            UIDocumentData data = _openUIDocuments[i];
            if (!data.IsOpen) continue;

            string name = System.IO.Path.GetFileNameWithoutExtension(data.Path);
            string title = $"{name} (UI)###UIDoc_{data.Path}";

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));
            if (data.FocusNextFrame)
            {
                ImGui.SetNextWindowFocus();
                data.FocusNextFrame = false;
            }
            if (data.FirstFrame)
            {
                uint targetDock = _lastGameDockId != 0 ? _lastGameDockId : dockspaceId;
                ImGui.SetNextWindowDockID(targetDock, ImGuiCond.FirstUseEver);
                data.FirstFrame = false;
            }

            bool open = ImGui.Begin(title, ref data.IsOpen,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            ImGui.PopStyleVar();

            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows | ImGuiFocusedFlags.RootWindow))
            {
                SetActiveUIDocument(data);
                _context.HierarchyTarget = HierarchyTarget.UI;
            }

            if (open) data.Panel.OnImGuiRender();
            ImGui.End();
        }

        for (int i = _openUIDocuments.Count - 1; i >= 0; i--)
        {
            if (!_openUIDocuments[i].IsOpen)
            {
                _openUIDocuments[i].Dispose();
                _openUIDocuments.RemoveAt(i);
            }
        }

        if (_activeUIDocument != null && !_openUIDocuments.Contains(_activeUIDocument))
        {
            SetActiveUIDocument(_openUIDocuments.Count > 0 ? _openUIDocuments[^1] : null);
        }
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
                    if (ImGui.MenuItem("Browser (WebGL2)")) BuildProject(Spot.Build.BuildPlatform.Browser);
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Regenerate Project Files"))
                {
                    if (ImGui.MenuItem("Update Build Files (.csproj, DLLs)")) Spot.Build.ProjectGenerator.Generate(Project.Active!, overwriteProgram: false);
                    if (ImGui.MenuItem("Full Reset (Includes Program.cs)")) Spot.Build.ProjectGenerator.Generate(Project.Active!, overwriteProgram: true);
                    ImGui.EndMenu();
                }
                ImGui.Separator();
                string reloadLabel = _scriptsOutOfDate ? "Reload Scripts *" : "Reload Scripts";
                if (ImGui.MenuItem(reloadLabel, "Ctrl+R", false, _state == EditorState.Edit)) ReloadScripts();
                ImGui.MenuItem("Auto-Reload Scripts", "", ref _autoReloadScripts);
                ImGui.Separator();
            }

            if (ImGui.MenuItem("Exit")) RequestExit();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Edit"))
        {
            bool canUndo = _state == EditorState.Edit && _history.CanUndo;
            bool canRedo = _state == EditorState.Edit && _history.CanRedo;

            // Naming the action is most of what makes undo feel trustworthy: the user can see what
            // Ctrl+Z is about to take back before pressing it.
            string undoLabel = canUndo ? $"Undo {_history.UndoLabel}" : "Undo";
            string redoLabel = canRedo ? $"Redo {_history.RedoLabel}" : "Redo";
            if (ImGui.MenuItem(undoLabel, "Ctrl+Z", false, canUndo)) Undo();
            if (ImGui.MenuItem(redoLabel, "Ctrl+Shift+Z", false, canRedo)) Redo();
            ImGui.Separator();
            ImGui.MenuItem("History", "Ctrl+H", ref _showHistory);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            if (ImGui.BeginMenu("Panels"))
            {
                ImGui.MenuItem("Game", "", ref _showGame);
                ImGui.MenuItem("Hierarchy", "", ref _showHierarchy);
                ImGui.MenuItem("Properties", "", ref _showInspector);
                ImGui.MenuItem("Console", "", ref _showConsole);
                ImGui.MenuItem("Asset Browser", "", ref _showAssetBrowser);
                ImGui.MenuItem("Profiler", "", ref _showProfiler);
                ImGui.MenuItem("Audio Mixer", "Ctrl+M", ref _showAudioMixer);
                ImGui.MenuItem("History", "Ctrl+H", ref _showHistory);
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



    // Draws the centered play / pause / step toolbar inside the main menu bar.
    private void DrawPlayControl()
    {
        var palette = EditorThemeManager.Current.Palette;
        float size = ImGui.GetFrameHeight();
        const float gap = 2.0f;
        float totalWidth = size * 3 + gap * 2;

        // Center the three-button group; clamp so we never overlap existing menu items.
        float centerX = (ImGui.GetWindowWidth() - totalWidth) * 0.5f;
        if (centerX > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(centerX);

        var drawList = ImGui.GetWindowDrawList();
        bool playing = _state != EditorState.Edit;

        // ── button helper ──────────────────────────────────────────────
        // Returns (hovered, clicked) for one icon button at current cursor.
        static (bool hovered, bool clicked) IconButton(string id, float sz)
        {
            ImGui.InvisibleButton(id, new Vector2(sz, sz));
            return (ImGui.IsItemHovered(), ImGui.IsItemClicked(ImGuiMouseButton.Left));
        }

        float pad = size * 0.22f;

        // ── 1. Play / Stop ─────────────────────────────────────────────
        Vector2 p0 = ImGui.GetCursorScreenPos();
        var (h0, c0) = IconButton("##play", size);
        if (h0) drawList.AddRectFilled(p0, p0 + new Vector2(size, size), ImGui.GetColorU32(palette.FrameBgHovered), 4.0f);

        if (!playing)
        {
            // Green triangle: Play
            uint col = ImGui.GetColorU32(new Vector4(0.35f, 0.85f, 0.45f, 1.0f));
            drawList.AddTriangleFilled(p0 + new Vector2(pad, pad), p0 + new Vector2(pad, size - pad), p0 + new Vector2(size - pad, size * 0.5f), col);
            if (c0) OnPlay();
            if (h0) ImGui.SetTooltip("Play");
        }
        else
        {
            // Red square: Stop
            uint col = ImGui.GetColorU32(palette.LogError);
            drawList.AddRectFilled(p0 + new Vector2(pad, pad), p0 + new Vector2(size - pad, size - pad), col, 2.0f);
            if (c0) OnStop();
            if (h0) ImGui.SetTooltip("Stop");
        }

        ImGui.SameLine(0, gap);

        // ── 2. Pause / Resume ──────────────────────────────────────────
        Vector2 p1 = ImGui.GetCursorScreenPos();
        var (h1, c1) = IconButton("##pause", size);
        uint pauseCol = playing
            ? ImGui.GetColorU32(palette.Text)
            : ImGui.GetColorU32(palette.TextDisabled);
        if (h1 && playing) drawList.AddRectFilled(p1, p1 + new Vector2(size, size), ImGui.GetColorU32(palette.FrameBgHovered), 4.0f);

        if (!_isPlayPaused)
        {
            // Two vertical bars (pause icon).
            float bw = size * 0.18f;
            float bh = size - pad * 2;
            drawList.AddRectFilled(p1 + new Vector2(pad, pad), p1 + new Vector2(pad + bw, pad + bh), pauseCol, 1.0f);
            drawList.AddRectFilled(p1 + new Vector2(size - pad - bw, pad), p1 + new Vector2(size - pad, pad + bh), pauseCol, 1.0f);
            if (c1 && playing) OnPause();
            if (h1) ImGui.SetTooltip(playing ? "Pause (Ctrl+P)" : "Pause (not playing)");
        }
        else
        {
            // Right-pointing triangle (resume icon).
            drawList.AddTriangleFilled(p1 + new Vector2(pad, pad), p1 + new Vector2(pad, size - pad), p1 + new Vector2(size - pad, size * 0.5f), pauseCol);
            if (c1 && playing) OnResume();
            if (h1) ImGui.SetTooltip("Resume (Ctrl+P)");
        }

        ImGui.SameLine(0, gap);

        // ── 3. Step ────────────────────────────────────────────────────
        Vector2 p2 = ImGui.GetCursorScreenPos();
        bool stepEnabled = playing && _isPlayPaused;
        var (h2, c2) = IconButton("##step", size);
        uint stepCol = stepEnabled ? ImGui.GetColorU32(palette.Text) : ImGui.GetColorU32(palette.TextDisabled);
        if (h2 && stepEnabled) drawList.AddRectFilled(p2, p2 + new Vector2(size, size), ImGui.GetColorU32(palette.FrameBgHovered), 4.0f);

        // Step icon: small triangle + vertical bar (>|)
        float sw = size * 0.18f;
        drawList.AddTriangleFilled(p2 + new Vector2(pad, pad), p2 + new Vector2(pad, size - pad), p2 + new Vector2(size - pad - sw - gap, size * 0.5f), stepCol);
        drawList.AddRectFilled(p2 + new Vector2(size - pad - sw, pad), p2 + new Vector2(size - pad, size - pad), stepCol, 1.0f);
        if (c2 && stepEnabled) OnStep();
        if (h2) ImGui.SetTooltip(stepEnabled ? "Step (advance one frame)" : "Step (pause first)");
    }

    private void OnPause()
    {
        if (_state == EditorState.Play) _isPlayPaused = true;
    }

    private void OnResume()
    {
        if (_state == EditorState.Play) _isPlayPaused = false;
    }

    private void OnStep()
    {
        if (_state == EditorState.Play && _isPlayPaused) _playStep = true;
    }

    // Keyboard shortcuts handled once per frame (editor/edit mode only).
    private void HandleShortcuts()
    {
        bool ctrl = Spot.Core.Input.GetKey(Spot.Core.Key.LeftControl) || Spot.Core.Input.GetKey(Spot.Core.Key.RightControl);
        if ((_state == EditorState.Edit || _state == EditorState.Play) && ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.S))
        {
            // Save what you're working in: a focused UI document tab, otherwise the active scene.
            if (_context.HierarchyTarget == HierarchyTarget.UI && _activeUIDocument != null)
                SaveUIDocument();
            else
                SaveScene();
        }
        if (_state == EditorState.Edit && ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.N))
        {
            NewScene();
        }
        // Undo/redo. Read through ImGui so a held Ctrl+Z repeats, and skip entirely while a text field
        // has focus so the field keeps ImGui's own text undo instead of the scene rolling back under it.
        // Shift is tested first, or Ctrl+Shift+Z would trigger both branches and cancel itself out.
        var io = ImGui.GetIO();
        if (_state == EditorState.Edit && io.KeyCtrl && !io.WantTextInput)
        {
            if (io.KeyShift)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.Z, true)) Redo();
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.Z, true))
            {
                Undo();
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.Y, true))
            {
                Redo();
            }
        }
        if (_state == EditorState.Edit && ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.R))
        {
            ReloadScripts();
        }

        // Ctrl+M toggles the Audio Mixer in both edit and play mode: hearing the mix while the game runs is
        // most of the point of having it.
        if (ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.M))
        {
            _showAudioMixer = !_showAudioMixer;
        }

        // Ctrl+H toggles the History panel.
        if (ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.H))
        {
            _showHistory = !_showHistory;
        }

        // Play-mode controls: Ctrl+P toggles pause/resume; Ctrl+Right steps one frame while paused.
        // (Space is intentionally NOT used here — it's commonly bound to game actions like jump.)
        if (_state == EditorState.Play)
        {
            if (ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.P))
            {
                if (_isPlayPaused) OnResume(); else OnPause();
            }
            if (ctrl && Spot.Core.Input.GetKeyDown(Spot.Core.Key.Right))
                OnStep();

            // Escape releases the Game panel's input focus (frees the cursor back to the editor).
            // Use ImGui's key check so it fires even while the game holds cursor lock.
            if (_gamePanelFocused && ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                _gamePanelFocused = false;
            }
        }

        // Auto-reload: once script edits have settled (a short debounce past the last file event) and the user
        // isn't mid-interaction, rebuild and swap the assembly. Manual reload stays available regardless.
        if (_state == EditorState.Edit && _autoReloadScripts && _scriptsOutOfDate
            && System.Environment.TickCount64 - _scriptsChangedAtTick > ScriptReloadDebounceMs
            && !EditorIsInteracting())
        {
            ReloadScripts();
        }
    }

    // How long to wait after the last detected script file change before auto-reloading, so a burst of saves
    // (or an editor writing a temp file then renaming) collapses into a single rebuild.
    private const long ScriptReloadDebounceMs = 600;

    // True while the user is mid-interaction (dragging the gizmo, or editing an ImGui field), used to
    // coalesce a continuous edit into a single history entry: snapshots are only committed once the
    // interaction settles.
    private static bool EditorIsInteracting() =>
        ImGui.GetIO().MouseDown[0] || ImGui.IsAnyItemActive();

    // How many unattributed scene changes the catch-all has had to record. Shown in the History panel
    // as the migration signal: every mutation site routed through a real action drops this toward zero,
    // and a non-zero count in a normal editing session names a gap worth closing.
    private int _unattributedChanges;

    // The gap notice is told once per session and then stays quiet: it is a migration signal, not news
    // the user needs repeated. The running count lives in the History panel's footer instead.
    private bool _gapNoticeShown;

    /// <summary>
    /// Records any scene change that did not come through the undo history as a single coarse entry.
    /// This is what makes Ctrl+Z complete even where a mutation site has not been migrated to a
    /// precise action yet: the worst case is a whole-scene entry with a generic label, never a change
    /// that cannot be undone at all.
    /// </summary>
    private void CaptureUnattributedChange(OpenSceneData sceneData, string current)
    {
        // First look at this scene: adopt the current state as the baseline rather than inventing an
        // entry for the act of opening it.
        if (sceneData.LastPushSnapshot == null)
        {
            sceneData.LastPushSnapshot = current;
            return;
        }

        if (current == sceneData.LastPushSnapshot || EditorIsInteracting())
        {
            return;
        }

        string before = sceneData.LastPushSnapshot;
        _history.Push(new DocumentSnapshotAction(
            "Scene Change", sceneData, before, current, json => RestoreSnapshot(sceneData, json)));
        sceneData.LastPushSnapshot = current;

        _unattributedChanges++;
        if (!_gapNoticeShown)
        {
            _gapNoticeShown = true;
            Spot.Core.Log.Info(
                "Some edits are being undone as whole-scene 'Scene Change' steps rather than named ones. "
                + "Everything is still undoable; the History panel (Ctrl+H) counts them.");
        }
    }

    private void Undo()
    {
        // Commit anything mid-edit first, so Ctrl+Z takes back the edit the user just made rather than
        // the one before it.
        UndoTracker.Flush();
        _history.Undo();
    }

    private void Redo()
    {
        UndoTracker.Flush();
        _history.Redo();
    }

    // Set whenever the history moves, so the next periodic check re-baselines the catch-all instead of
    // mistaking the result of a recorded action for an unattributed change.
    private bool _baselinesStale;

    // Re-reads every open scene as the catch-all's baseline, and refreshes the unsaved-changes marker
    // while the serialization is in hand so the tab's "*" updates immediately after an undo rather than
    // lagging until the next periodic check.
    private void SyncSnapshotBaselines()
    {
        foreach (var sceneData in _openScenes)
        {
            string current = new SceneSerializer(sceneData.Scene).SerializeToString();
            sceneData.LastPushSnapshot = current;
            sceneData.IsDirty = sceneData.FilePath == null
                || sceneData.SavedSnapshot == null
                || current != sceneData.SavedSnapshot;
            sceneData.DirtyCheckCounter = 0;
        }
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
            // A recorded action just moved a scene, so adopt the result as the catch-all's baseline
            // before checking anything. Otherwise the check below would see the action's own effect as
            // an unattributed change and record a second, coarse entry for it — two undos for one edit.
            if (_baselinesStale)
            {
                _baselinesStale = false;
                SyncSnapshotBaselines();
            }

            foreach (var sceneData in _openScenes)
            {
                if (++sceneData.DirtyCheckCounter < 15)
                {
                    continue;
                }
                sceneData.DirtyCheckCounter = 0;

                // Serialize once and reuse for both dirty-tracking and the undo catch-all. Unsaved scenes
                // (no file yet) are always considered dirty.
                string current = new SceneSerializer(sceneData.Scene).SerializeToString();
                sceneData.IsDirty = sceneData.FilePath == null
                    || sceneData.SavedSnapshot == null
                    || current != sceneData.SavedSnapshot;

                CaptureUnattributedChange(sceneData, current);
            }
        }

        string projectName = Project.Active?.Config.Name ?? "Untitled Project";
        string title = $"Spot {Spot.Core.Application.Instance.EngineVersion} - {projectName}";
        if (_state == EditorState.Play)
        {
            title += _isPlayPaused ? " (Paused)" : " (Playing)";
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

        // Commit a half-finished edit before writing, so what lands on disk is what the history says.
        UndoTracker.Flush();

        new SceneSerializer(sceneData.Scene).Serialize(sceneData.FilePath);
        EnsureStartScene(sceneData.FilePath);
        sceneData.SavedSnapshot = new SceneSerializer(sceneData.Scene).SerializeToString();
        sceneData.LastPushSnapshot = sceneData.SavedSnapshot;
        sceneData.IsDirty = false;
        sceneData.DirtyCheckCounter = 0;
        _history.MarkSaved(sceneData);
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

        string sptprojPath = project.FilePath;
        if (string.IsNullOrEmpty(sptprojPath))
            sptprojPath = System.IO.Path.Combine(project.ProjectDirectory, project.Config.Name + ".sptproj");

        Project.SaveActive(sptprojPath);
        Spot.Core.Log.Info("Start scene set to '{0}'", project.Config.StartScene);
    }

    // Persists the mixer's bus layout into the active project. Project.SaveActive captures the live layout, so
    // this only has to decide where the .sptproj lives.
    private void SaveAudioMixerLayout()
    {
        var project = Project.Active;
        if (project == null || string.IsNullOrEmpty(project.ProjectDirectory)) return;

        string sptprojPath = project.FilePath;
        if (string.IsNullOrEmpty(sptprojPath))
            sptprojPath = System.IO.Path.Combine(project.ProjectDirectory, project.Config.Name + ".sptproj");

        try
        {
            Project.SaveActive(sptprojPath);
        }
        catch (System.Exception ex)
        {
            Spot.Core.Log.Warn("Could not save the audio mixer layout: {0}", ex.Message);
        }
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

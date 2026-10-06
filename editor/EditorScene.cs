using System.Numerics;
using ImGuiNET;
using Spot.Engine;
using Spot.Engine.Graphics;
using Spot.Engine.Scenes;
using Spot.Engine.Events;
using Spot.Build;
using Spot.Editor.Panels;
using Spot.DebugUI;
using Spot.DebugUI.Panels;
using Spot.DebugUI.UI;
using Spot.Editor.UI;
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

    // Whether the viewport looks through the scene's game camera (its active primary camera) instead of the
    // editor camera. Play turns it on for the playing scene, so the scene becomes the game, and Stop turns it
    // off; F8 switches it at any time, like Unreal's eject/possess.
    public bool GameView;

    public OpenSceneData(EditorContext context)
    {
        ViewportPanel = new ViewportPanel(context, () => Scene);
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
    public required Spot.Engine.UI.UIRoot Document;
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

    private List<OpenSceneData> _openScenes = new();
    private OpenSceneData? _activeSceneData = null;

    // The scene being played, pinned when Play is pressed: focusing another scene tab during play must not
    // move the simulation (or Stop's snapshot restore) onto that scene.
    private OpenSceneData? _playSceneData;

    // One dockable node-graph editor window per open .sptcontroller asset.
    private readonly List<AnimatorControllerPanel> _animatorEditors = new();

    // Unsaved-changes confirmation state: a scene panel pending close, and the app-quit prompt.
    private OpenSceneData? _pendingCloseScene;
    private bool _showQuitConfirm;

    private string? _lastWindowTitle;

    // Per-panel visibility, toggled from View > Panels and by each window's close button.
    private bool _showHierarchy = true;
    private bool _showInspector = true;
    private bool _showConsole = true;

    // The dock node the scene viewports live in, where newly opened scene and UI tabs are docked.
    private uint _lastViewportDockId;

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

        // DebugUI knows nothing about projects (it also runs as the runtime overlay), so the editor tells it
        // the project name new component scripts take their namespace from.
        Spot.DebugUI.UI.ComponentScripts.ProjectNameSource = () => Project.Active?.Config.Name;

        // Panels record actions without knowing about scene tabs, so tell the history how to map a
        // scene back to the tab that owns it — that is what puts the "*" on the right tab.
        EditorHistory.SceneDocumentResolver =
            scene => _openScenes.FirstOrDefault(s => ReferenceEquals(s.Scene, scene));

        // Panels name their structural edits (adding dropped assets, ...) through this instead of leaving
        // them to the catch-all's generic "Scene Change".
        EditorHistory.SceneEditRecorder = RecordSceneEdit;

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
            if (entity.HasComponent<Transform>() && _activeSceneData != null)
            {
                _activeSceneData.EditorCamera.Focus(entity.GetComponent<Transform>().WorldPosition);
            }
        };
    }

    public override void OnEnter()
    {
        _warmupFrames = 3;
        EditorThemeManager.SetTheme(EditorThemes.SpotDark);
        Spot.Editor.Utils.EditorSettings.LoadAndApply(Spot.Engine.Application.Instance.Window.NativeWindow);
        ImGui.LoadIniSettingsFromDisk("imgui.ini");

        // Intercept window-close requests so we can confirm unsaved changes first.
        Spot.Engine.Application.Instance.CanClose = CanCloseApp;

        // The editor docks the console as a native panel, so take ownership of its window: the engine
        // then stops drawing its own floating "Console" (ImGui would merge the two by name and draw the
        // body — prompt included — twice) and stops capturing input from the console's open state, which
        // in the editor had no way to be dismissed and left the game's input dead. The ' key now reveals
        // and focuses the docked panel instead.
        Spot.Engine.Application.Instance.Console.SetHost(FocusConsolePanel);

        LoadStartScene();
    }

    // Reveals the docked Console panel and hands it the keyboard, the editor's answer to the engine's
    // "open the console" request (the ' key). Runs during event handling, before OnUpdate, so taking the
    // controls away from the game is picked up by the same frame's input transition: the cursor is freed and
    // game input suppressed, exactly as Escape does. Without that, keys typed into the prompt would also
    // drive the game and the camera would stay on mouse-look.
    private void FocusConsolePanel()
    {
        _focusConsoleRequested = true;
        _gameHasInput = false;
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

        Spot.Engine.UI.UIRoot document = Spot.Engine.UI.UISerializer.Load(filepath);
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
            Spot.Engine.UI.UISerializer.Save(_activeUIDocument.Document, _activeUIDocument.Path);
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

        Spot.Engine.Assets.AssetDatabase.Refresh(project.GetAssetDirectory());
        Spot.Engine.Assets.AssetDatabase.InstallLibraryResolver(System.IO.Path.Combine(project.ProjectDirectory, Spot.Build.ProjectStructure.LibraryFolder));

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
    // Besides the project-local bin/ (standalone projects), checks bin/<Name>/ under every ancestor directory:
    // a Directory.Build.props anywhere above the project that sets BaseOutputPath =
    // $(MSBuildThisFileDirectory)bin\$(MSBuildProjectName)\ redirects the build there (this repo's does, for
    // samples/HelloEngine). The newest match across all of them wins, so a stale build elsewhere never shadows it.
    private static string? FindProjectAssembly(Project project)
    {
        string name = project.Config.Name;
        var searchDirs = new List<string> { System.IO.Path.Combine(project.ProjectDirectory, "bin") };
        for (var dir = new System.IO.DirectoryInfo(project.ProjectDirectory).Parent; dir != null; dir = dir.Parent)
        {
            searchDirs.Add(System.IO.Path.Combine(dir.FullName, "bin", name));
        }

        var dlls = new List<string>();
        foreach (string dir in searchDirs)
        {
            dlls.AddRange(FindDlls(dir, name + ".dll"));
        }

        return System.Linq.Enumerable.FirstOrDefault(
            System.Linq.Enumerable.OrderByDescending(dlls, f => System.IO.File.GetLastWriteTimeUtc(f)));
    }

    private static string[] FindDlls(string dir, string dllName)
    {
        if (!System.IO.Directory.Exists(dir)) return [];
        try
        {
            return System.IO.Directory.GetFiles(dir, dllName, System.IO.SearchOption.AllDirectories);
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
        {
            // An unreadable folder up the tree must not stop Play; it just isn't a candidate.
            Spot.Engine.Log.CoreWarn("Could not search '{0}' for the script assembly: {1}", dir, ex.Message);
            return [];
        }
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
            Spot.Engine.Log.CoreWarn("Could not watch project scripts for changes: {0}", ex.Message);
        }
    }

    private void StopScriptWatcher()
    {
        _scriptWatcher?.Dispose();
        _scriptWatcher = null;
    }

    // The script build running in the background, if any, with when it started (for the status indicator), and
    // whether Play was pressed while it ran (or before any scripts were built) and should start once it's done.
    private System.Threading.Tasks.Task<Spot.Build.BuildResult>? _scriptBuild;
    private long _scriptBuildStartedAt;
    private bool _playWhenScriptsReady;

    // The last script build's outcome, shown briefly in the menu bar (a failure stays until the next build).
    private bool? _scriptBuildSucceeded;
    private long _scriptBuildFinishedAt;

    // Build output arrives on the build's threads; it is queued and logged from the UI thread.
    private readonly System.Collections.Concurrent.ConcurrentQueue<(bool IsError, string Line)> _scriptBuildOutput = new();

    private bool ScriptsBuilding => _scriptBuild is not null;

    /// <summary>
    /// Rebuilds the active project's scripts in the background and, once the build finishes, swaps in the new
    /// assembly without restarting the editor (see <see cref="CompleteScriptReload"/>), preserving each
    /// component's authored field values and entity references. The editor stays responsive meanwhile and the
    /// menu bar shows the progress. Only starts in edit mode; a reload already running absorbs the request
    /// (an edit made during it marks the scripts out of date again, so auto-reload follows up).
    /// </summary>
    private void ReloadScripts()
    {
        Project? project = Spot.Build.Project.Active;
        if (project == null || _state != EditorState.Edit || ScriptsBuilding)
        {
            return;
        }

        _scriptsOutOfDate = false;
        _scriptBuildSucceeded = null;
        _scriptBuildStartedAt = System.Environment.TickCount64;
        _scriptBuild = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                return Spot.Build.ProjectBuilder.Build(
                    project,
                    Spot.Build.BuildPlatform.Windows,
                    onOutput: line => _scriptBuildOutput.Enqueue((false, line)),
                    onError: line => _scriptBuildOutput.Enqueue((true, line)),
                    fastDebug: true);
            }
            catch (System.Exception ex)
            {
                _scriptBuildOutput.Enqueue((true, "Script build threw: " + ex.Message));
                return new Spot.Build.BuildResult(false, -1, string.Empty);
            }
        });
    }

    // Called every frame on the UI thread: relays the build's output to the console and, once the build has
    // finished, completes the reload (or reports the failure) and starts a Play that was waiting for it.
    private void PollScriptBuild()
    {
        while (_scriptBuildOutput.TryDequeue(out var output))
        {
            if (output.IsError)
                Spot.Engine.Log.Error("[Build] {0}", output.Line);
            else
                LogBuildOutput(output.Line);
        }

        if (_scriptBuild is not { IsCompleted: true } build)
        {
            return;
        }

        _scriptBuild = null;
        _scriptBuildFinishedAt = System.Environment.TickCount64;
        Project? project = Spot.Build.Project.Active;
        bool built = build.Result.Success && project != null;
        _scriptBuildSucceeded = built && CompleteScriptReload(project!);
        if (!built)
        {
            Spot.Engine.Log.Error("Script reload aborted: build failed.");
        }

        bool play = _playWhenScriptsReady;
        _playWhenScriptsReady = false;
        if (play && _state == EditorState.Edit)
        {
            OnPlay();
        }
    }

    /// <summary>
    /// The UI-thread half of a script reload, run once the build succeeded: swaps in the freshly compiled
    /// assembly. A failed load logs and leaves the components unresolved (their data is kept).
    /// </summary>
    /// <returns><see langword="true"/> if the new scripts are loaded.</returns>
    private bool CompleteScriptReload(Project project)
    {
        // Play was entered while the build ran: swapping types under a running game is not safe. Leave the old
        // scripts in place and mark them out of date, so the reload happens once play stops.
        if (_state != EditorState.Edit)
        {
            _scriptsOutOfDate = true;
            return true;
        }

        // 1. Turn every component from the old assembly back into scene data (MissingComponents), so nothing in
        //    the open scenes keeps the old load context alive and it can be collected.
        foreach (OpenSceneData sceneData in _openScenes)
        {
            SceneSerializer.UnresolveUserComponents(sceneData.Scene, type => type.Assembly.IsCollectible);
        }

        // 2. Swap the assembly, first forgetting the reflection caches that still reference the old types.
        ComponentSerialization.ClearTypeCaches();
        Spot.DebugUI.UI.ComponentScripts.ForgetLoadedTypes();
        s_scriptHost.Unload();
        string? dll = FindProjectAssembly(project);
        if (dll == null || !s_scriptHost.Load(dll))
        {
            Spot.Engine.Log.Error("Script reload failed to load the rebuilt assembly; scripts are now unresolved.");
            return false;
        }

        // 3. Rebuild the components from the new assembly, fields and entity references included.
        foreach (OpenSceneData sceneData in _openScenes)
        {
            SceneSerializer.ResolveMissingComponents(sceneData.Scene);
        }

        return true;
    }

    private void LoadStartScene()
    {
        _openScenes.Clear();
        _activeSceneData = null;
        _context.ActiveScene = null;
        _context.Selection = null;

        if (Project.Active == null)
        {
            Project.New();
            var newSceneData = new OpenSceneData(_context);
            _openScenes.Add(newSceneData);
            _activeSceneData = newSceneData;
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

            data.EditorCamera.RestoreState(cam);
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
        PollScriptBuild();

        // Opening a project mid-play replaces every scene tab, the playing one included (and a closed tab
        // takes its scene with it). Leave play mode rather than keep a scene running that is no longer open.
        if (_state != EditorState.Edit && (_playSceneData == null || !_openScenes.Contains(_playSceneData)))
        {
            OnStop();
        }

        foreach (var sceneData in _openScenes)
        {
            bool isActiveSim = _state != EditorState.Edit && sceneData == _playSceneData;
            if (isActiveSim)
            {
                // Gate game input on the playing viewport holding the controls. _gameHasInput is evaluated
                // from the previous frame's ImGui pass (one-frame lag is imperceptible to the user).
                // Handle transitions before setting suppression so cursor management runs
                // while InputBlocked still matches the previous frame's state.
                if (_gameHasInput != _prevGameHasInput)
                {
                    _prevGameHasInput = _gameHasInput;
                    if (_gameHasInput)
                    {
                        // Gaining focus: unsuppress input first, then restore game's cursor lock.
                        Spot.Engine.Input.Suppressed = false;
                        Spot.Engine.Input.RestoreCursor();
                    }
                    else
                    {
                        // Losing focus: release cursor while not yet suppressed, then suppress.
                        Spot.Engine.Input.ReleaseCursor();
                        Spot.Engine.Input.Suppressed = true;
                    }
                }
                else
                {
                    // No transition: just maintain current suppression state.
                    Spot.Engine.Input.Suppressed = !_gameHasInput;
                }

                // While the viewport shows the game, the game is presented in that rectangle of the window: its UI
                // hit-testing and its scripts' pointer read the viewport's size and a mouse position measured from
                // its corner, as they would full-window in a build. The rectangle is the previous frame's ImGui
                // layout, like _gameHasInput. Cleared right after, since the editor works in window space.
                if (sceneData.GameView && _gameViewSize.X > 0 && _gameViewSize.Y > 0)
                {
                    Spot.Engine.Display.SetView(_gameViewOrigin.X, _gameViewOrigin.Y, _gameViewSize.X, _gameViewSize.Y);
                }

                try
                {
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
                finally
                {
                    Spot.Engine.Display.ClearView();
                }
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
                    if (_lastSelectedParticleEntity.Value.TryGetComponent(out ParticleSystemRenderer? oldParticles))
                    {
                        oldParticles.Clear();
                        oldParticles.Stop();
                    }
                }
            }

            if (currentSelected.HasValue && currentSelected.Value.IsActiveInHierarchy() && currentSelected.Value.TryGetComponent(out ParticleSystemRenderer? particles))
            {
                if (!particles.IsPlaying)
                {
                    particles.Play();
                }
                Spot.Engine.Scenes.ParticleSystem.UpdateEntity(currentSelected.Value, deltaTime);
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
        // Render Scene Views
        foreach (var sceneData in _openScenes)
        {
            if (!sceneData.IsOpen) continue;

            if (sceneData.GameView)
            {
                // A full game render (shadows, post-processing) is only worth it when the tab is on screen; a
                // hidden one keeps its last picture until shown again.
                if (sceneData.ViewportVisible) RenderGameView(sceneData);
                continue;
            }

            sceneData.Framebuffer.Bind();
            Renderer.SetClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            Renderer.Clear();

            if (sceneData.EditorCamera.Is3D)
            {
                Renderer.SetDepthTest(true);
                Renderer.SetFaceCulling(true);
            }

            RenderSystem.Render(sceneData.Scene, sceneData.EditorCamera.ViewProjection, sceneData.EditorCamera.Position, renderUI: false);

            // The grid draws the world axes itself, in the theme's axis colors. In 3D it is depth-tested
            // against the scene just rendered, so geometry in front hides it, while a surface lying on the
            // ground plane shows the grid over it steadily rather than z-fighting with it.
            var palette = EditorThemeManager.Current.Palette;
            var gridStyle = EditorGridStyle.Default with
            {
                AxisXColor = palette.AxisX,
                AxisYColor = palette.AxisY,
                AxisZColor = palette.AxisZ,
            };

            if (sceneData.EditorCamera.Is3D)
            {
                EditorGrid.Draw3D(sceneData.EditorCamera.ViewProjection, sceneData.EditorCamera.Position, gridStyle);

                // The overlays below (colliders, gizmos, icons) are crossed quads with no single front-face
                // winding, drawn over everything.
                Renderer.SetDepthTest(false);
                Renderer.SetFaceCulling(false);
            }
            else
            {
                Renderer2D.BeginScene(sceneData.EditorCamera.ViewProjection);
                EditorGrid.Draw2D(gridStyle);
                Renderer2D.EndScene();
            }

            // Debug Physics Rendering
            bool showAll = Spot.Engine.Physics.PhysicsDebug.ShowColliders && sceneData == _activeSceneData;
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
                    if (entity.HasComponent<BoxCollider2D>() && entity.HasComponent<Transform>())
                    {
                        var transform = entity.GetComponent<Transform>();
                        var collider = entity.GetComponent<BoxCollider2D>();
                        var bounds = collider.GetWorldBounds(new Vector2(transform.WorldPosition.X, transform.WorldPosition.Y), new Vector2(transform.WorldScale.X, transform.WorldScale.Y));
                        Renderer2D.DrawRect(bounds.Center, bounds.HalfExtents * 2.0f, new Vector4(0.0f, 1.0f, 0.0f, 1.0f), 0.02f);
                    }

                    if (entity.HasComponent<BoxCollider3D>() && entity.HasComponent<Transform>())
                    {
                        var transform = entity.GetComponent<Transform>();
                        var collider = entity.GetComponent<BoxCollider3D>();
                        var bounds = collider.GetWorldBounds(transform.WorldPosition, transform.WorldScale);
                        DrawBox3DWire(bounds.Min, bounds.Max);
                    }

                    if (entity.TryGetComponent(out Spot.Engine.Relationship? rel))
                    {
                        foreach (var child in rel.Children)
                            DrawEntityColliders(child);
                    }
                }

                Renderer2D.BeginScene(sceneData.EditorCamera.ViewProjection);

                if (showAll)
                {
                    // ShowColliders is on: draw every entity in the scene, not just the selection.
                    foreach (var entity in sceneData.Scene.View<BoxCollider2D, Transform>())
                    {
                        if (!entity.IsActiveInHierarchy()) continue;
                        var transform = entity.GetComponent<Transform>();
                        var collider = entity.GetComponent<BoxCollider2D>();
                        var bounds = collider.GetWorldBounds(new Vector2(transform.WorldPosition.X, transform.WorldPosition.Y), new Vector2(transform.WorldScale.X, transform.WorldScale.Y));
                        Renderer2D.DrawRect(bounds.Center, bounds.HalfExtents * 2.0f, new Vector4(0.0f, 1.0f, 0.0f, 1.0f), 0.02f);
                    }

                    foreach (var entity in sceneData.Scene.View<BoxCollider3D, Transform>())
                    {
                        if (!entity.IsActiveInHierarchy()) continue;
                        var transform = entity.GetComponent<Transform>();
                        var collider = entity.GetComponent<BoxCollider3D>();
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
                && _context.Selection.Value.HasComponent<Camera>()
                && _context.Selection.Value.HasComponent<Transform>())
            {
                var camEntity = _context.Selection.Value;
                var camComp = camEntity.GetComponent<Camera>();
                var camTransform = camEntity.GetComponent<Transform>();

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
            if (_context.Selection.HasValue && _context.Selection.Value.HasComponent<Camera>() && sceneData == _activeSceneData && sceneData.ViewportVisible)
            {
                sceneData.CameraPreviewFramebuffer.Bind();
                var entity = _context.Selection.Value;
                var cc = entity.GetComponent<Camera>();
                if (entity.HasComponent<Transform>())
                {
                    var transform = entity.GetComponent<Transform>();
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

        // Render each open UI document into its own offscreen target so its tab shows an up-to-date picture.
        foreach (UIDocumentData data in _openUIDocuments)
        {
            if (data.IsOpen) data.Panel.RenderDocument();
        }

        var window = Spot.Engine.Application.Instance.Window;
        Renderer.SetViewport(0, 0, (uint)window.Width, (uint)window.Height);
        Renderer.SetClearColor(0.0f, 0.0f, 0.0f, 1.0f);
    }

    // Renders a scene viewport through the scene's game camera, the picture a build of the game would show:
    // no grid, gizmos or editor icons. Without a camera that would render, the viewport is cleared to black and
    // the ImGui pass explains why.
    private static void RenderGameView(OpenSceneData sceneData)
    {
        sceneData.Framebuffer.Bind();
        Renderer.SetClearColor(0.0f, 0.0f, 0.0f, 1.0f);
        Renderer.Clear();

        if (sceneData.Scene.TryGetActivePrimaryCamera(out Entity cameraEntity))
        {
            var cc = cameraEntity.GetComponent<Camera>();
            var transform = cameraEntity.GetComponent<Transform>();
            bool is3D = cc.ProjectionType == SceneCameraProjection.Perspective;

            Vector4 clearColor = cc.BackgroundColor;
            Renderer.SetClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
            Renderer.Clear();

            if (is3D)
            {
                Renderer.SetDepthTest(true);
                Renderer.SetFaceCulling(true);
            }

            RenderSystem.Render(sceneData.Scene, cc.GetViewProjection(transform), transform.WorldPosition);

            if (is3D)
            {
                Renderer.SetDepthTest(false);
                Renderer.SetFaceCulling(false);
            }
        }

        sceneData.Framebuffer.Unbind();
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
            EditorGui.MarkFocusedTab();
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

            // Generate unique title but nice display name. The playing scene's tab carries a play mark: that
            // viewport is the game now.
            string stableId = sceneData.FilePath != null ? sceneData.FilePath : $"Untitled_{i}";
            string playMark = IsPlaying(sceneData) ? $"{EditorIcons.Play}  " : "";
            string title = $"{playMark}{sceneName}{(sceneData.IsDirty ? "*" : "")}###Scene_{stableId}";

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0.0f, 0.0f));

            if (sceneData.FocusNextFrame)
            {
                ImGui.SetNextWindowFocus();
                sceneData.FocusNextFrame = false;
            }
            if (sceneData.FirstFrame)
            {
                uint targetDock = _lastViewportDockId != 0 ? _lastViewportDockId : dockspaceId;
                ImGui.SetNextWindowDockID(targetDock, ImGuiCond.FirstUseEver);
                sceneData.FirstFrame = false;
            }

            bool wasOpen = sceneData.IsOpen;
            bool open = ImGui.Begin(title, ref sceneData.IsOpen, ImGuiWindowFlags.NoCollapse);
            ImGui.PopStyleVar();
            if (open) EditorGui.MarkFocusedTab();
            sceneData.ViewportVisible = open;

            uint viewportDockId = ImGui.GetWindowDockID();
            if (viewportDockId != 0) _lastViewportDockId = viewportDockId;

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
            }

            // Focusing a scene viewport switches the shared Hierarchy panel back to the scene's entities.
            if (isFocused)
            {
                _context.HierarchyTarget = HierarchyTarget.Scene;
            }

            if (open)
            {
                Vector2 imageMin = ImGui.GetCursorScreenPos();
                Vector2 imageSize = ImGui.GetContentRegionAvail();

                // The game renders into this viewport when played, so its camera takes the viewport's aspect.
                // That also keeps the camera preview and frustum gizmo true to what play will show.
                if (imageSize.X > 0 && imageSize.Y > 0 && sceneData.Scene.TryGetActivePrimaryCamera(out Entity gameCamera))
                {
                    gameCamera.GetComponent<Camera>().SetViewportSize(imageSize.X, imageSize.Y);
                }

                // Where the playing game's picture sits in the window, for its input (see OnUpdate). ImGui's screen
                // space is the window's own unless platform windows are enabled, so measure from the main viewport.
                if (sceneData == _playSceneData && sceneData.GameView)
                {
                    _gameViewOrigin = imageMin - ImGui.GetMainViewport().Pos;
                    _gameViewSize = imageSize;
                }

                sceneData.ViewportPanel.OnImGuiRender(handleInput: isFocused || isHovered, gameView: sceneData.GameView);
                DrawGameViewOverlays(sceneData, imageMin, imageSize);
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
                Spot.Engine.Log.Info(
                    "Closed a scene with {0} undo {1} in the history; they were discarded.",
                    dropped, dropped == 1 ? "entry" : "entries");
            }
        }

        _openScenes.RemoveAll(s => !s.IsOpen);
        if (_activeSceneData is null || !_openScenes.Contains(_activeSceneData))
        {
            _activeSceneData = _openScenes.Count > 0 ? _openScenes[0] : null;
            _context.ActiveScene = _activeSceneData?.Scene;
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
            Spot.Engine.Application.Instance.Console.RequestInputFocus();
        }

        if (_showConsole)
        {
            bool open = ImGui.Begin("Console", ref _showConsole, ImGuiWindowFlags.NoCollapse);
            if (open) EditorGui.MarkFocusedTab();
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
            if (open) EditorGui.MarkFocusedTab();
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

        var lastLine = Spot.Engine.Application.Instance.Console.LastLine;
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
        // The mouse is withheld from ImGui as well: the hidden cursor is parked at the window centre,
        // which may sit over any panel, so the game's clicks would otherwise press editor buttons.
        // Placed last so all viewport panels have already had their turn with the flags.
        const ImGuiConfigFlags gameLockFlags = ImGuiConfigFlags.NoMouseCursorChange | ImGuiConfigFlags.NoMouse;
        bool gameLocksCursor = _state != EditorState.Edit && _gameHasInput && Spot.Engine.Input.CursorLocked;
        if (gameLocksCursor)
        {
            ImGui.GetIO().ConfigFlags |= gameLockFlags;
            _gameHeldImGuiLock = true;
        }
        else if (_gameHeldImGuiLock)
        {
            ImGui.GetIO().ConfigFlags &= ~gameLockFlags;
            _gameHeldImGuiLock = false;
        }

        // Last statement of the frame: commit a field edit whose interaction has finished, and otherwise
        // refresh the selection baseline so the next recorded action knows what was selected when the
        // user started it. Must come after every panel has drawn.
        UndoTracker.EndFrame();
    }

    // ----- Game view -------------------------------------------------------------------------------

    private bool IsPlaying(OpenSceneData sceneData) => _state != EditorState.Edit && sceneData == _playSceneData;

    // Hints painted over a scene viewport that shows the game camera, or that play mode ejected to the editor
    // camera, plus click-to-control for the playing scene. Draw-list only, so they never take input from the
    // viewport underneath.
    private void DrawGameViewOverlays(OpenSceneData sceneData, Vector2 min, Vector2 size)
    {
        bool playing = IsPlaying(sceneData);
        if ((!playing && !sceneData.GameView) || size.X <= 0 || size.Y <= 0) return;

        if (!sceneData.GameView)
        {
            // Ejected: the game keeps running, but the viewport is the editor's until F8 hands it back.
            DrawViewportHint(min, size, "Editor camera  ·  F8 to return to the game");
            return;
        }

        // Clicking the picture hands the game the controls. Only the image counts, so clicking the tab to
        // bring the viewport forward doesn't also start driving the game.
        if (playing && !_gameHasInput && ImGui.IsWindowHovered()
            && ImGui.IsMouseHoveringRect(min, min + size) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _gameHasInput = true;
        }

        // Without a camera that would render nothing shows, so explain the black viewport instead of leaving
        // it unexplained, a common first-time snag.
        if (!sceneData.Scene.HasActivePrimaryCamera())
            DrawViewportMessage(min, size, "No camera in scene");
        else if (playing && !_gameHasInput)
            DrawViewportMessage(min, size, "Click to control");

        if (!playing)
            DrawViewportHint(min, size, "Game camera  ·  F8 for the editor camera");
        else if (!_gameHasInput)
            DrawViewportHint(min, size, "F8 for the editor camera");
        else if (Spot.Engine.Input.CursorLocked)
            DrawViewportHint(min, size, "Esc to release the cursor  ·  F8 for the editor camera");
    }

    // A message in a floating pill at the centre of a viewport.
    private static void DrawViewportMessage(Vector2 min, Vector2 size, string text)
    {
        var drawList = ImGui.GetWindowDrawList();
        Vector2 textSize = ImGui.CalcTextSize(text);
        Vector2 textPos = min + (size - textSize) * 0.5f;
        var pad = new Vector2(12.0f, 7.0f);
        EditorGui.OverlayPanel(drawList, textPos - pad, textPos + textSize + pad);
        drawList.AddText(textPos, ImGui.GetColorU32(EditorThemeManager.Current.Palette.Text), text);
    }

    // A subtle hint centred along the bottom edge of a viewport.
    private static void DrawViewportHint(Vector2 min, Vector2 size, string text)
    {
        var drawList = ImGui.GetWindowDrawList();
        Vector2 textSize = ImGui.CalcTextSize(text);
        var textPos = new Vector2(min.X + (size.X - textSize.X) * 0.5f, min.Y + size.Y - textSize.Y - 12.0f);
        var pad = new Vector2(9.0f, 4.0f);
        EditorGui.OverlayPanel(drawList, textPos - pad, textPos + textSize + pad, 4.0f);
        drawList.AddText(textPos, ImGui.GetColorU32(EditorThemeManager.Current.Palette.TextDisabled), text);
    }

    // The scene F8 acts on: the playing scene during play, otherwise the scene being edited.
    private OpenSceneData? GameViewTarget => _state != EditorState.Edit ? _playSceneData : _activeSceneData;

    // Switches a viewport between the game camera and the editor camera (F8), like Unreal's eject/possess.
    // During play it acts on the playing scene: ejecting takes the controls back for the editor and starts
    // the editor camera at the game camera's view, so you inspect the running world from where the player
    // stands; possessing hands the controls back to the game. In edit mode it previews the scene through
    // its game camera.
    private void ToggleGameView()
    {
        OpenSceneData? target = GameViewTarget;
        if (target == null) return;

        target.GameView = !target.GameView;
        if (_state == EditorState.Edit) return;

        _gameHasInput = target.GameView;
        if (target.GameView)
            target.FocusNextFrame = true;
        else
            MoveEditorCameraToGameCamera(target);
    }

    // Puts the editor camera where the game camera is, looking the same way. Skipped when the two don't share
    // a projection: a 2D editor view has no pose matching a perspective game camera, and vice versa.
    private static void MoveEditorCameraToGameCamera(OpenSceneData sceneData)
    {
        if (!sceneData.Scene.TryGetActivePrimaryCamera(out Entity cameraEntity)) return;

        var cc = cameraEntity.GetComponent<Camera>();
        var transform = cameraEntity.GetComponent<Transform>();
        EditorCamera camera = sceneData.EditorCamera;
        bool perspective = cc.ProjectionType == SceneCameraProjection.Perspective;
        if (perspective != camera.Is3D) return;

        Vector3 position = transform.WorldPosition;
        if (perspective)
        {
            // Read the look direction off the camera's own view-projection (as the frustum gizmo does), so it
            // matches what the game renders whatever rotation convention the component uses.
            if (!Matrix4x4.Invert(cc.GetViewProjection(transform), out Matrix4x4 invVP)) return;
            Vector3 near = Unproject(invVP, 0.0f);
            Vector3 far = Unproject(invVP, 1.0f);
            Vector3 forward = far - near;
            if (forward.LengthSquared() < 1e-12f || !float.IsFinite(forward.LengthSquared())) return;
            forward = Vector3.Normalize(forward);

            // Inverse of EditorCamera's forward: (cos p sin y, sin p, -cos p cos y).
            const float pitchLimit = MathF.PI / 2.0f - 0.01f;
            camera.Yaw = MathF.Atan2(forward.X, -forward.Z);
            camera.Pitch = Math.Clamp(MathF.Asin(Math.Clamp(forward.Y, -1.0f, 1.0f)), -pitchLimit, pitchLimit);
            camera.Position = position;
        }
        else
        {
            camera.Position = new Vector3(position.X, position.Y, camera.Position.Z);
        }
        camera.UpdateView();

        // The centre of the view at NDC depth z (0 = near plane, 1 = far plane), in world space.
        static Vector3 Unproject(Matrix4x4 invVP, float z)
        {
            Vector4 p = Vector4.Transform(new Vector4(0.0f, 0.0f, z, 1.0f), invVP);
            return new Vector3(p.X, p.Y, p.Z) / p.W;
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
        _context.ActiveScene = newSceneData.Scene;
        _context.Selection = null;
    }

    private void BuildProject(Spot.Build.BuildPlatform platform)
    {
        var project = Project.Active;
        if (project == null || string.IsNullOrEmpty(project.ProjectDirectory)) return;

        Spot.Engine.Log.Info($"Starting build process for {platform}...");

        System.Threading.Tasks.Task.Run(() =>
        {
            var result = Spot.Build.ProjectBuilder.Build(project, platform,
                onOutput: msg => Spot.Engine.Log.Info(msg),
                onError: msg => Spot.Engine.Log.Error(msg));

            if (result.Success)
            {
                Spot.Engine.Log.Info("Build completed successfully!");
                if (System.OperatingSystem.IsWindows())
                {
                    try { System.Diagnostics.Process.Start("explorer.exe", $"\"{result.OutputDir}\""); } catch { }
                }
            }
            else
            {
                Spot.Engine.Log.Error($"Build failed with exit code {result.ExitCode}. See above for details.");
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
                    Spot.Engine.Log.CoreWarn($"Dropping directories is not fully supported yet: '{file}'");
                }
            }
            catch (System.Exception ex)
            {
                Spot.Engine.Log.CoreError($"Failed to copy dropped file '{file}': {ex.Message}");
            }
        }
        return true;
    }

    public override void OnExit()
    {
        Spot.Engine.Application.Instance.CanClose = null;
        Spot.Engine.Application.Instance.Console.SetHost(null);
        Spot.Editor.Utils.EditorSettings.Save(Spot.Engine.Application.Instance.Window.NativeWindow);
        SaveSession();
        CaptureProjectThumbnail();

        StopScriptWatcher();
        foreach (var sceneData in _openScenes) sceneData.Dispose();
        _inspectorPanel.Dispose();
        foreach (UIDocumentData data in _openUIDocuments) data.Dispose();
        _context.ActiveScene?.OnExit();
    }

    // JSON snapshot of the active scene taken when Play is pressed; restored on Stop.
    private string? _prePlaySnapshot;
    private bool _isPlayPaused;
    private bool _playStep;
    private bool _gameHeldImGuiLock;
    // Whether the game holds the controls: set when play starts, on possess (F8) and by clicking the playing
    // viewport; cleared by Esc, eject (F8) and the console. When false the game receives no keyboard/mouse
    // input and the cursor is free.
    private bool _gameHasInput;
    private bool _prevGameHasInput;

    // The playing viewport's image rectangle in window pixels, recorded by the ImGui pass.
    private Vector2 _gameViewOrigin;
    private Vector2 _gameViewSize;

    private void OnPlay()
    {
        if (_state != EditorState.Edit || Project.Active == null || _activeSceneData == null) return;

        // Scripts are compiling (or were never built — the first Play after a clean checkout): build them in the
        // background and start playing as soon as they're loaded, rather than freezing the editor or running the
        // game without its components. The menu bar shows the wait.
        if (ScriptsBuilding || s_scriptHost.Assembly == null)
        {
            _playWhenScriptsReady = true;
            ReloadScripts();
            return;
        }

        // Drop any in-flight edit rather than recording it: play mode is about to replace this state.
        UndoTracker.Abandon();

        _playSceneData = _activeSceneData;
        _prePlaySnapshot = new SceneSerializer(_playSceneData.Scene).SerializeToString();
        _isPlayPaused = false;
        _playStep = false;

        // The scene becomes the game: its viewport switches to the game camera and the game gets the controls
        // straight away, as in Unreal. F8 ejects to the editor camera; Esc frees the cursor.
        _playSceneData.GameView = true;
        _gameViewSize = Vector2.Zero; // measured afresh by this play's first ImGui pass
        _playSceneData.FocusNextFrame = true;
        _gameHasInput = true;
        _prevGameHasInput = false;
        _state = EditorState.Play;
    }

    private void OnStop()
    {
        if (_state == EditorState.Edit) return;

        if (_playSceneData != null)
        {
            var scene = _playSceneData.Scene;
            ComponentSystem.DestroyAll(scene);

            // What the game built beside its entities — its UI, its render passes — goes with it, even when no
            // snapshot is restored below; otherwise the edit-mode viewport would keep drawing the game's HUD.
            scene.ClearRuntimeState();
            foreach (var e in scene.View<AudioSource>())
            {
                var src = e.GetComponent<AudioSource>();
                if (src.IsPlaying) src.Stop();
            }
            scene.TeardownPhysics();
            if (_prePlaySnapshot != null) RestoreSnapshot(_playSceneData, _prePlaySnapshot);

            // Back to editing, so back to the editor camera.
            _playSceneData.GameView = false;
            _playSceneData = null;
        }
        SyncSnapshotBaselines();
        _prePlaySnapshot = null;
        _isPlayPaused = false;
        _gameHasInput = false;
        _prevGameHasInput = false;
        // Clear suppression first so CursorLocked can actually apply the cursor-free state.
        Spot.Engine.Input.Suppressed = false;
        // Return the hardware cursor to normal; game scripts never get a chance to do this on Stop.
        Spot.Engine.Input.CursorLocked = false;
        _state = EditorState.Edit;
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
            Spot.Engine.Log.Info("[Build] {0}", line);
        }
    }

    // Rebuilds the default docked arrangement: Hierarchy and Inspector on the right,
    // Asset Browser and Console side-by-side along the bottom, and the scene viewports in the center.
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
                uint targetDock = _lastViewportDockId != 0 ? _lastViewportDockId : dockspaceId;
                ImGui.SetNextWindowDockID(targetDock, ImGuiCond.FirstUseEver);
                data.FirstFrame = false;
            }

            bool open = ImGui.Begin(title, ref data.IsOpen,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            ImGui.PopStyleVar();
            if (open) EditorGui.MarkFocusedTab();

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
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10.0f, 7.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(12.0f, 7.0f));
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
                _showHierarchy = _showInspector = _showConsole = _showAssetBrowser = true;
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
        DrawScriptStatus();

        ImGui.EndMainMenuBar();
        ImGui.PopStyleVar(2);
    }



    // How long "Scripts reloaded" stays in the menu bar after a successful reload.
    private const long ScriptStatusLingerMs = 3000;

    // The script status at the right end of the menu bar, so a reload never looks like a frozen editor: a
    // spinner with the elapsed time while scripts compile (and a note when Play is waiting for them), a short
    // "reloaded" confirmation, a failure that stays until the next build and opens the console when clicked,
    // and — with auto-reload off — a reminder that edited scripts are waiting for Ctrl+R.
    private void DrawScriptStatus()
    {
        var palette = EditorThemeManager.Current.Palette;
        long now = System.Environment.TickCount64;

        string glyph;
        string label;
        Vector4 color;
        string tooltip;
        bool spinner = false;
        System.Action? onClick = null;

        if (ScriptsBuilding)
        {
            spinner = true;
            glyph = string.Empty;
            long seconds = (now - _scriptBuildStartedAt) / 1000;
            label = _playWhenScriptsReady
                ? $"Compiling scripts... {seconds}s  (Play starts when ready)"
                : $"Compiling scripts... {seconds}s";
            color = palette.Text;
            tooltip = "The project's scripts are being rebuilt. The editor stays usable meanwhile; components update when it finishes.";
        }
        else if (_scriptBuildSucceeded == false)
        {
            glyph = EditorIcons.Warning;
            label = "Script build failed";
            color = palette.LogError;
            tooltip = "The scripts didn't compile; the previous ones are still in use. Click to open the Console.";
            onClick = () => _focusConsoleRequested = true;
        }
        else if (_scriptBuildSucceeded == true && now - _scriptBuildFinishedAt < ScriptStatusLingerMs)
        {
            glyph = EditorIcons.Check;
            label = "Scripts reloaded";
            color = new Vector4(0.42f, 0.80f, 0.50f, 1.0f);
            tooltip = "The rebuilt scripts are loaded.";
        }
        else if (_scriptsOutOfDate && !_autoReloadScripts && _state == EditorState.Edit)
        {
            glyph = EditorIcons.Rotate;
            label = "Scripts changed  (Ctrl+R)";
            color = palette.TextDisabled;
            tooltip = "Scripts were edited. Click (or press Ctrl+R) to rebuild and reload them.";
            onClick = ReloadScripts;
        }
        else
        {
            return;
        }

        const float iconWidth = 18.0f;
        const float rightMargin = 16.0f;
        float textWidth = ImGui.CalcTextSize(label).X;
        float width = iconWidth + 6.0f + textWidth;
        float x = ImGui.GetWindowWidth() - width - rightMargin;
        if (x < ImGui.GetCursorPosX() + 8.0f)
        {
            return; // no room beside the menus and the play controls
        }

        ImGui.SameLine(x);
        float barHeight = ImGui.GetFrameHeight();
        Vector2 origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##scriptStatus", new Vector2(width, barHeight));
        bool hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetTooltip(tooltip);
            if (onClick is not null) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        }

        if (onClick is not null && ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            onClick();
        }

        var drawList = ImGui.GetWindowDrawList();
        float textY = origin.Y + (barHeight - ImGui.GetTextLineHeight()) * 0.5f;
        Vector2 iconCenter = new(origin.X + iconWidth * 0.5f, origin.Y + barHeight * 0.5f);
        if (spinner)
        {
            // A three-quarter arc turning about once a second, in the accent color.
            float start = (float)(now % 1000) / 1000.0f * MathF.Tau;
            drawList.PathArcTo(iconCenter, 6.0f, start, start + MathF.PI * 1.5f, 24);
            drawList.PathStroke(ImGui.GetColorU32(palette.Accent), ImDrawFlags.None, 2.0f);
        }
        else
        {
            Vector2 glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(iconCenter - glyphSize * 0.5f, ImGui.GetColorU32(color), glyph);
        }

        Vector4 labelColor = hovered && onClick is not null ? palette.Text : color;
        drawList.AddText(new Vector2(origin.X + iconWidth + 6.0f, textY), ImGui.GetColorU32(labelColor), label);
    }

    // Draws the centered play / pause / step / camera controls inside the main menu bar. The three transport
    // buttons share one subtle capsule and the camera switch sits beside it. Buttons follow the viewport
    // toolbar's states: a neutral lift on hover, and a wash while their state is on (red while playing, accent
    // while paused, a solid accent while the viewport looks through the game camera).
    private void DrawPlayControl()
    {
        var palette = EditorThemeManager.Current.Palette;
        float barHeight = ImGui.GetFrameHeight();
        float size = MathF.Round(barHeight - 6.0f);
        const float gap = 2.0f;
        const float inset = 2.0f;
        const float groupGap = 10.0f;
        float transportWidth = size * 3 + gap * 2;
        float totalWidth = inset + transportWidth + inset + groupGap + size;

        // Center the group; clamp so we never overlap existing menu items. The buttons are shorter than the
        // bar, so each is moved down to the bar's middle: in the menu bar's horizontal layout SameLine returns
        // the cursor to the top of the line, so the offset is applied before every button, not just the first.
        float centerX = (ImGui.GetWindowWidth() - totalWidth) * 0.5f;
        if (centerX > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(centerX);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + inset);
        float rowY = ImGui.GetCursorPosY() + MathF.Round((barHeight - size) * 0.5f);
        ImGui.SetCursorPosY(rowY);

        var drawList = ImGui.GetWindowDrawList();
        bool playing = _state != EditorState.Edit;
        Vector4 text = palette.Text;

        Vector2 origin = ImGui.GetCursorScreenPos();
        Vector2 capsuleMin = origin - new Vector2(inset, inset);
        Vector2 capsuleMax = origin + new Vector2(transportWidth + inset, size + inset);
        drawList.AddRectFilled(capsuleMin, capsuleMax, ImGui.GetColorU32(new Vector4(text.X, text.Y, text.Z, 0.05f)), 5.0f);
        drawList.AddRect(capsuleMin, capsuleMax, ImGui.GetColorU32(new Vector4(text.X, text.Y, text.Z, 0.06f)), 5.0f);

        // One square icon button at the cursor: paints its background for the state, returns (hovered, clicked).
        (bool hovered, bool clicked) IconButton(string id, Vector4? onColor, bool enabled)
        {
            ImGui.SetCursorPosY(rowY);
            Vector2 p = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton(id, new Vector2(size, size));
            bool hovered = ImGui.IsItemHovered();
            Vector4? fill = onColor is Vector4 on ? new Vector4(on.X, on.Y, on.Z, hovered ? 0.42f : 0.30f)
                : hovered && enabled ? new Vector4(text.X, text.Y, text.Z, 0.10f)
                : null;
            if (fill is Vector4 f)
                drawList.AddRectFilled(p, p + new Vector2(size, size), ImGui.GetColorU32(f), 3.0f);
            return (hovered, enabled && ImGui.IsItemClicked(ImGuiMouseButton.Left));
        }

        float pad = MathF.Round(size * 0.27f);

        // ── 1. Play / Stop ─────────────────────────────────────────────
        Vector2 p0 = ImGui.GetCursorScreenPos();
        var (h0, c0) = IconButton("##play", playing ? palette.LogError : null, enabled: true);

        if (!playing)
        {
            // Green triangle: Play
            uint col = ImGui.GetColorU32(new Vector4(0.42f, 0.80f, 0.50f, 1.0f));
            drawList.AddTriangleFilled(p0 + new Vector2(pad + 1, pad), p0 + new Vector2(pad + 1, size - pad), p0 + new Vector2(size - pad + 1, size * 0.5f), col);
            if (c0) OnPlay();
            if (h0) ImGui.SetTooltip(ScriptsBuilding ? "Play (starts once the scripts finish compiling)" : "Play");
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
        ImGui.SetCursorPosY(rowY);

        // ── 2. Pause / Resume ──────────────────────────────────────────
        Vector2 p1 = ImGui.GetCursorScreenPos();
        var (h1, c1) = IconButton("##pause", playing && _isPlayPaused ? palette.Accent : null, enabled: playing);
        uint pauseCol = playing
            ? ImGui.GetColorU32(palette.Text)
            : ImGui.GetColorU32(palette.TextDisabled);

        if (!_isPlayPaused)
        {
            // Two vertical bars (pause icon).
            float bw = MathF.Round(size * 0.16f);
            float bh = size - pad * 2;
            float bx = MathF.Round((size - bw * 3) * 0.5f);
            drawList.AddRectFilled(p1 + new Vector2(bx, pad), p1 + new Vector2(bx + bw, pad + bh), pauseCol, 1.0f);
            drawList.AddRectFilled(p1 + new Vector2(bx + bw * 2, pad), p1 + new Vector2(bx + bw * 3, pad + bh), pauseCol, 1.0f);
            if (c1) OnPause();
            if (h1) ImGui.SetTooltip(playing ? "Pause (Ctrl+P)" : "Pause (not playing)");
        }
        else
        {
            // Right-pointing triangle (resume icon).
            drawList.AddTriangleFilled(p1 + new Vector2(pad + 1, pad), p1 + new Vector2(pad + 1, size - pad), p1 + new Vector2(size - pad + 1, size * 0.5f), pauseCol);
            if (c1) OnResume();
            if (h1) ImGui.SetTooltip("Resume (Ctrl+P)");
        }

        ImGui.SameLine(0, gap);
        ImGui.SetCursorPosY(rowY);

        // ── 3. Step ────────────────────────────────────────────────────
        Vector2 p2 = ImGui.GetCursorScreenPos();
        bool stepEnabled = playing && _isPlayPaused;
        var (h2, c2) = IconButton("##step", null, stepEnabled);
        uint stepCol = stepEnabled ? ImGui.GetColorU32(palette.Text) : ImGui.GetColorU32(palette.TextDisabled);

        // Step icon: small triangle + vertical bar (>|)
        float sw = MathF.Round(size * 0.14f);
        drawList.AddTriangleFilled(p2 + new Vector2(pad, pad), p2 + new Vector2(pad, size - pad), p2 + new Vector2(size - pad - sw - gap, size * 0.5f), stepCol);
        drawList.AddRectFilled(p2 + new Vector2(size - pad - sw, pad), p2 + new Vector2(size - pad, size - pad), stepCol, 1.0f);
        if (c2) OnStep();
        if (h2) ImGui.SetTooltip(stepEnabled ? "Step (advance one frame)" : "Step (pause first)");

        // ── 4. Game / editor camera ────────────────────────────────────
        // Shows which camera the viewport looks through (gamepad = game, camera = editor), lit while it is the
        // game's; clicking switches, as F8 does.
        ImGui.SameLine(0, inset + groupGap);
        ImGui.SetCursorPosY(rowY);
        Vector2 p3 = ImGui.GetCursorScreenPos();
        OpenSceneData? viewTarget = GameViewTarget;
        bool gameView = viewTarget?.GameView ?? false;
        ImGui.InvisibleButton("##gameview", new Vector2(size, size));
        bool h3 = ImGui.IsItemHovered();
        bool c3 = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        if (gameView)
            drawList.AddRectFilled(p3, p3 + new Vector2(size, size), ImGui.GetColorU32(h3 ? palette.AccentHovered : palette.Accent), 3.0f);
        else if (h3 && viewTarget != null)
            drawList.AddRectFilled(p3, p3 + new Vector2(size, size), ImGui.GetColorU32(new Vector4(text.X, text.Y, text.Z, 0.10f)), 3.0f);

        string glyph = gameView ? EditorIcons.Gamepad : EditorIcons.Camera;
        Vector2 glyphSize = ImGui.CalcTextSize(glyph);
        Vector4 glyphColor = gameView ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f) : viewTarget != null ? palette.Text : palette.TextDisabled;
        drawList.AddText(p3 + (new Vector2(size, size) - glyphSize) * 0.5f, ImGui.GetColorU32(glyphColor), glyph);

        if (c3) ToggleGameView();
        if (h3)
        {
            ImGui.SetTooltip(gameView
                ? "Game camera: switch to the editor camera (F8)"
                : "Editor camera: switch to the game camera (F8)");
        }
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
        bool ctrl = Spot.Engine.Input.GetKey(Spot.Engine.Key.LeftControl) || Spot.Engine.Input.GetKey(Spot.Engine.Key.RightControl);
        if ((_state == EditorState.Edit || _state == EditorState.Play) && ctrl && Spot.Engine.Input.GetKeyDown(Spot.Engine.Key.S))
        {
            // Save what you're working in: a focused UI document tab, otherwise the active scene.
            if (_context.HierarchyTarget == HierarchyTarget.UI && _activeUIDocument != null)
                SaveUIDocument();
            else
                SaveScene();
        }
        if (_state == EditorState.Edit && ctrl && Spot.Engine.Input.GetKeyDown(Spot.Engine.Key.N))
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
        if (_state == EditorState.Edit && ctrl && Spot.Engine.Input.GetKeyDown(Spot.Engine.Key.R))
        {
            ReloadScripts();
        }

        // Ctrl+M toggles the Audio Mixer in both edit and play mode: hearing the mix while the game runs is
        // most of the point of having it.
        if (ctrl && Spot.Engine.Input.GetKeyDown(Spot.Engine.Key.M))
        {
            _showAudioMixer = !_showAudioMixer;
        }

        // Ctrl+H toggles the History panel.
        if (ctrl && Spot.Engine.Input.GetKeyDown(Spot.Engine.Key.H))
        {
            _showHistory = !_showHistory;
        }

        // F8 switches between the game camera and the editor camera, like Unreal's eject/possess. Read through
        // ImGui so it still fires while the game holds the controls (game input reads are blocked otherwise).
        if (ImGui.IsKeyPressed(ImGuiKey.F8, false))
        {
            ToggleGameView();
        }

        // Play-mode controls: Ctrl+P toggles pause/resume; Ctrl+Right steps one frame while paused.
        // (Space is intentionally NOT used here — it's commonly bound to game actions like jump.)
        if (_state == EditorState.Play)
        {
            if (ctrl && Spot.Engine.Input.GetKeyDown(Spot.Engine.Key.P))
            {
                if (_isPlayPaused) OnResume(); else OnPause();
            }
            if (ctrl && Spot.Engine.Input.GetKeyDown(Spot.Engine.Key.Right))
                OnStep();

            // Escape takes the controls back from the game (frees the cursor for the editor).
            // Use ImGui's key check so it fires even while the game holds cursor lock.
            if (_gameHasInput && ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                _gameHasInput = false;
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
    }

    /// <summary>
    /// Records a structural edit a panel just made to <paramref name="scene"/> as one named entry: the scene
    /// as the catch-all last saw it, and as it is now. Taken at once rather than at the next periodic check,
    /// so a quick follow-up edit (dragging the new entity's gizmo) cannot fold the change into its own
    /// baseline and leave it impossible to undo.
    /// </summary>
    private void RecordSceneEdit(Scene scene, string label)
    {
        OpenSceneData? sceneData = _openScenes.FirstOrDefault(s => ReferenceEquals(s.Scene, scene));
        if (sceneData == null || !_history.Enabled)
        {
            return;
        }

        // Land any half-finished field edit first, so it keeps its own entry ahead of this one.
        UndoTracker.Flush();

        string current = new SceneSerializer(scene).SerializeToString();
        string? before = sceneData.LastPushSnapshot;
        sceneData.LastPushSnapshot = current;
        if (before == null || before == current)
        {
            return;
        }

        _history.Push(new DocumentSnapshotAction(
            label, sceneData, before, current, json => RestoreSnapshot(sceneData, json)));
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
        string title = $"Spot {Spot.Engine.Application.Instance.EngineVersion} - {projectName}";
        if (_state == EditorState.Play)
        {
            title += _isPlayPaused ? " (Paused)" : " (Playing)";
        }

        if (title != _lastWindowTitle)
        {
            _lastWindowTitle = title;
            Spot.Engine.Application.Instance.Window.NativeWindow.Title = title;
        }
    }


    private void NewScene()
    {
        var newSceneData = new OpenSceneData(_context);
        newSceneData.FocusNextFrame = true;
        _openScenes.Add(newSceneData);
        _activeSceneData = newSceneData;
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
        if (sceneData == _activeSceneData) CaptureProjectThumbnail();
        return true;
    }

    // Refreshes the picture the launcher shows for this project from a scene viewport that is on screen: the active
    // one when visible, otherwise any visible one (a tab behind another keeps an old, possibly never-drawn, picture).
    private void CaptureProjectThumbnail()
    {
        if (Project.Active == null) return;
        OpenSceneData? source = _activeSceneData is { IsOpen: true, ViewportVisible: true }
            ? _activeSceneData
            : _openScenes.FirstOrDefault(s => s.IsOpen && s.ViewportVisible);
        if (source == null) return;
        Spot.Editor.Utils.ProjectThumbnail.Capture(source.Framebuffer, Project.Active.ProjectDirectory);
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
        else Spot.Engine.Application.Instance.Quit();
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
                    Spot.Engine.Application.Instance.Quit();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Exit Without Saving", new Vector2(160, 0)))
            {
                _showQuitConfirm = false;
                ImGui.CloseCurrentPopup();
                Spot.Engine.Application.Instance.Quit();
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
        Spot.Engine.Log.Info("Start scene set to '{0}'", project.Config.StartScene);
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
            Spot.Engine.Log.Warn("Could not save the audio mixer layout: {0}", ex.Message);
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
            _context.ActiveScene = newSceneData.Scene;
        }
        _context.Selection = null;
    }

}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using ImGuiNET;
using Spot.Console;
using Spot.Events;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Core.Services;
using Spot.Assets;
using System.IO;

namespace Spot.Core;

/// <summary>
/// Describes how an <see cref="Application"/> should be created.
/// </summary>
public class ApplicationSpec
{
    /// <summary>
    /// Gets or sets the application name.
    /// </summary>
    public string Name { get; set; } = "Spot Application";

    /// <summary>
    /// Gets or sets the window specification.
    /// </summary>
    public WindowSpec Window { get; set; } = new WindowSpec();

    /// <summary>
    /// Gets or sets an optional path to a TrueType font (.ttf) used for the ImGui UI. When null or
    /// missing on disk, ImGui's built-in default font is used instead.
    /// </summary>
    public string? FontPath { get; set; }

    /// <summary>
    /// Gets or sets the pixel size used when loading <see cref="FontPath"/>.
    /// </summary>
    public int FontSize { get; set; } = 16;

    /// <summary>
    /// Extra fonts to bake into the ImGui atlas alongside the primary <see cref="FontPath"/> — for
    /// example a heavier weight for titles or a monospaced face for the console. They are exposed, in
    /// order, via <see cref="Application.Fonts"/> (after index 0, the primary). A missing file is
    /// skipped and the primary font substituted in its slot, so indices stay stable for callers.
    /// </summary>
    public List<FontSpec> AdditionalFonts { get; set; } = new();

    /// <summary>
    /// An optional icon font merged into the primary font, so its glyphs render inline with text at
    /// the same baseline (toolbar/menu/tree icons). When null, no icon glyphs are available.
    /// </summary>
    public IconFontSpec? IconFont { get; set; }

    /// <summary>
    /// Gets or sets the root directory for source assets. Used by the editor (which loads from a project's
    /// <c>Assets/</c>) and as a fallback when no <see cref="ContentDirectory"/> is set.
    /// </summary>
    public string? AssetDirectory { get; set; }

    /// <summary>
    /// Gets or sets the root directory of cooked content shipped with the game (the build's <c>Content/</c>).
    /// When set it takes precedence over <see cref="AssetDirectory"/> as the root that scenes, fonts, and
    /// relative references resolve against — a shipped game loads cooked content, never source assets.
    /// </summary>
    public string? ContentDirectory { get; set; }

    /// <summary>
    /// Gets or sets the asset manifest path (absolute, or relative to <see cref="ContentDirectory"/>). When set
    /// and present, the game loads it and resolves <c>guid:</c> references to cooked artifacts through it.
    /// </summary>
    public string? ManifestPath { get; set; }

    /// <summary>
    /// Gets or sets the path to the start scene to load automatically on startup.
    /// </summary>
    public string? StartScene { get; set; }

    /// <summary>
    /// Gets or sets the project's default input bindings: action names mapped to the keys/buttons that
    /// trigger them (e.g. "forward" -> W, Up). Applied at startup via <see cref="Input.SetDefaultBindings"/>
    /// so both the editor and shipped games get them, and so the <c>resetbinds</c> console command can
    /// restore them after runtime rebinds. The game manages its own action names.
    /// </summary>
    public Dictionary<string, InputBinding[]> DefaultBindings { get; set; } = new();

    /// <summary>Gets the effective asset root: the cooked <see cref="ContentDirectory"/> if set, else <see cref="AssetDirectory"/>.</summary>
    public string? AssetRoot =>
        !string.IsNullOrEmpty(ContentDirectory) ? ContentDirectory : AssetDirectory;
}

/// <summary>
/// Describes one extra TrueType font to load into the ImGui atlas (see
/// <see cref="ApplicationSpec.AdditionalFonts"/>).
/// </summary>
public sealed class FontSpec
{
    /// <summary>
    /// Creates a font spec from a <c>.ttf</c> path and a pixel size. Pass <paramref name="glyphRanges"/>
    /// (an ImGui <c>[lo, hi, …, 0]</c> array) to bake only a tight set of codepoints — e.g. an icon font
    /// at a large size for use as its own standalone face; it must stay referenced by the caller (the
    /// engine pins it while the atlas is built). When null, the font's default (Latin) ranges are baked.
    /// </summary>
    public FontSpec(string path, float size, ushort[]? glyphRanges = null)
    {
        Path = path;
        Size = size;
        GlyphRanges = glyphRanges;
    }

    /// <summary>The path to the <c>.ttf</c> file.</summary>
    public string Path { get; }

    /// <summary>The pixel size to rasterize the font at.</summary>
    public float Size { get; }

    /// <summary>An optional tight glyph range (<c>[lo, hi, …, 0]</c>); null bakes the default ranges.</summary>
    public ushort[]? GlyphRanges { get; }
}

/// <summary>
/// Describes an icon font to merge into the primary UI font (see <see cref="ApplicationSpec.IconFont"/>).
/// </summary>
public sealed class IconFontSpec
{
    /// <summary>
    /// Creates an icon-font spec. <paramref name="glyphRanges"/> is an ImGui glyph-range array
    /// (<c>[lo, hi, lo, hi, …, 0]</c>) listing only the codepoints to bake, keeping the atlas small; it
    /// must stay referenced by the caller (the engine pins it while the atlas is built).
    /// </summary>
    public IconFontSpec(string path, float size, ushort[] glyphRanges)
    {
        Path = path;
        Size = size;
        GlyphRanges = glyphRanges;
    }

    /// <summary>The path to the icon <c>.ttf</c> file.</summary>
    public string Path { get; }

    /// <summary>The pixel size to rasterize the icons at.</summary>
    public float Size { get; }

    /// <summary>The ImGui glyph-range array (<c>[lo, hi, …, 0]</c>) of icons to bake.</summary>
    public ushort[] GlyphRanges { get; }
}

/// <summary>
/// Represents the running application and owns the main loop.
/// </summary>
public class Application
{
    private static Application? s_instance;

    // Upper bound on a single frame's delta time (seconds). Frames longer than this are treated as
    // this long so a stall can't destabilize physics; ~10 FPS worth of catch-up per frame.
    private const float MaxDeltaTime = 0.1f;

    private readonly ApplicationSpec _spec;
    private readonly DevConsole _console = new();
    private readonly List<IEngineService> _services = new();
    private Window? _window;
    
    private bool _running;
    private float _deltaTime;
    private string? _lastFrameError;
    private bool _frameFaulted;
    private Stopwatch? _stopwatch;
    private TimeSpan _lastTime;

    private ImGuiService? _imguiService;

    /// <summary>
    /// Gets the runtime debugger service.
    /// </summary>
    public IDebugOverlay? Debugger { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Application"/> class.
    /// </summary>
    /// <param name="spec">The application specification.</param>
    public Application(ApplicationSpec spec)
    {
        _spec = spec;
        _running = false;
        _deltaTime = 0.0f;
        s_instance = this;
    }

    /// <summary>
    /// Gets the current application instance.
    /// </summary>
    public static Application Instance =>
        s_instance ?? throw new InvalidOperationException("No application has been created.");

    /// <summary>
    /// Gets the application name.
    /// </summary>
    public string Name => _spec.Name;

    /// <summary>
    /// Gets the application window.
    /// </summary>
    public Window Window =>
        _window ?? throw new InvalidOperationException("The window has not been created yet.");

    /// <summary>
    /// Gets the total time in seconds since the application started.
    /// </summary>
    public float Time => (float)(_stopwatch?.Elapsed.TotalSeconds ?? 0.0);

    /// <summary>
    /// Gets the developer console.
    /// </summary>
    public DevConsole Console => _console;

    /// <summary>
    /// The fonts loaded into the ImGui atlas: index 0 is the primary UI font, followed by each entry
    /// of <see cref="ApplicationSpec.AdditionalFonts"/> in order. Empty until the ImGui service has
    /// initialized. Use with <see cref="ImGui.PushFont"/> to render titles, monospaced text, etc.
    /// </summary>
    public IReadOnlyList<ImFontPtr> Fonts => _imguiService?.Fonts ?? Array.Empty<ImFontPtr>();

    // Names already reported as missing, so a script calling GetFont every frame warns only once each.
    private readonly HashSet<string> _warnedMissingFonts = new();

    /// <summary>
    /// A font discovered under the project's <c>Assets/Fonts</c> directory, by file name (without
    /// extension), baked nearest to <paramref name="size"/> px. Push it with <see cref="ImGui.PushFont"/>
    /// to draw crisp text at that size instead of rescaling the UI font (which blurs). Falls back to the
    /// primary UI font when no such font exists, so the result is always safe to push.
    /// </summary>
    public ImFontPtr GetFont(string name, float size)
    {
        ImFontPtr? font = _imguiService?.GetFont(name, size);
        if (font.HasValue)
        {
            return font.Value;
        }

        if (_warnedMissingFonts.Add(name))
        {
            Log.CoreWarn("Font '{0}' not found under Assets/Fonts; using the default UI font.", name);
        }

        IReadOnlyList<ImFontPtr> fonts = Fonts;
        return fonts.Count > 0 ? fonts[0] : default;
    }

    public string EngineVersion => SpotEngine.GetVersion();

    public void AddService(IEngineService service)
    {
        _services.Add(service);
    }

    // Best-effort discovery of the optional runtime debug overlay from Spot.DebugUI, if that assembly ships
    // alongside the app. Loaded by name so the engine never references the ImGui authoring panels directly;
    // returns null (and the app runs without the overlay) when the assembly is absent.
    private static IDebugOverlay? TryLoadOptionalDebugOverlay()
    {
        try
        {
            Type? type = Type.GetType("Spot.DebugUI.RuntimeDebuggerService, Spot.DebugUI");
            return type is not null ? Activator.CreateInstance(type) as IDebugOverlay : null;
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Optional debug overlay (Spot.DebugUI) could not be loaded: {0}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Runs the main application loop until the application stops.
    /// </summary>
    /// <param name="startScene">The scene to load first (for example a menu).</param>
    public void Run(Scene? startScene = null)
    {
        Initialize(startScene);

        while (_running)
        {
            try
            {
                PollEvents();
                Update();
                Render();
            }
            catch (Exception ex)
            {
                ReportFrameError("frame", ex);
            }
        }

        Shutdown();
    }

    private void Initialize(Scene? startScene)
    {
        Log.Init(new DevConsoleSink(_console));
        Log.CoreInfo("Initializing '{0}'", _spec.Name);

        // Establish the asset root and, for a cooked/shipped game, the content manifest that resolves
        // guid: references — before any service or scene loads an asset.
        InitializeContent();

        _window = new Window(_spec.Window);
        _window.SetEventCallback(OnEvent);

        AddService(new GraphicsService());
        AddService(new AudioService());
        _imguiService = new ImGuiService(_spec);
        AddService(_imguiService);
        // The host may inject a debug overlay (the editor does); otherwise, if the optional Spot.DebugUI
        // assembly ships alongside the app, host its overlay automatically so any build can inspect its
        // scene at runtime — without the engine taking a compile-time dependency on the ImGui panels.
        Debugger ??= TryLoadOptionalDebugOverlay();
        if (Debugger != null)
        {
            AddService(Debugger);
        }

        foreach (var service in _services)
        {
            service.Init(this);
        }

        // Apply the project's default input bindings before any scene loads, since a scene's scripts
        // may query actions in OnCreate. Works for editor play and shipped games alike, as both build
        // the spec in code.
        Input.SetDefaultBindings(_spec.DefaultBindings);

        _running = true;
        if (startScene is not null)
        {
            SceneManager.Load(startScene);
        }
        else if (!string.IsNullOrEmpty(_spec.StartScene))
        {
            // Resolves guid: references and paths relative to the asset root, and logs (never throws)
            // when the scene is missing or malformed — the same runtime path used for scene switches.
            SceneManager.Load(_spec.StartScene);
        }

        _stopwatch = Stopwatch.StartNew();
        _lastTime = _stopwatch.Elapsed;
    }

    // Points the asset system at this app's content: sets the resolution root and, for a shipped game with a
    // cooked manifest, installs the resolver that turns guid: references into cooked-artifact paths. Missing
    // content logs and continues (the engine must never crash on bad data) rather than aborting startup.
    private void InitializeContent()
    {
        if (!string.IsNullOrEmpty(_spec.AssetRoot))
        {
            AssetPath.Root = ResolveContentPath(_spec.AssetRoot);
        }

        if (string.IsNullOrEmpty(_spec.ManifestPath))
        {
            return;
        }

        string manifestPath = _spec.ManifestPath;
        if (!Path.IsPathRooted(manifestPath) && !string.IsNullOrEmpty(_spec.ContentDirectory))
        {
            manifestPath = Path.Combine(_spec.ContentDirectory, manifestPath);
        }

        manifestPath = ResolveContentPath(manifestPath);

        if (File.Exists(manifestPath))
        {
            AssetManifest.Load(manifestPath);
            AssetPath.ContentResolver = static reference => AssetManifest.Active?.ResolveGuidRef(reference);
        }
        else
        {
            Log.CoreError("Content manifest not found: {0}", manifestPath);
        }
    }

    // Cooked content ships next to the executable, so a relative content path must resolve against the
    // app's base directory — not the current working directory, which lets a shipped game run from any
    // launch location (e.g. `dotnet run` from the repo root). Absolute paths are returned unchanged.
    private static string ResolveContentPath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    private void PollEvents()
    {
        _frameFaulted = false;
        Input.NewFrame();

        // The engine owns input while the dev console or debugger panels are open:
        // cursor forced free, game input withheld.
        Input.SetEngineCaptured(_console.IsOpen || (Debugger?.IsOpen ?? false));

        _window!.PollEvents();
    }

    private void Update()
    {
        TimeSpan now = _stopwatch!.Elapsed;
        _deltaTime = (float)(now - _lastTime).TotalSeconds;
        _lastTime = now;

        // Clamp the frame delta so a hitch (window drag, GC pause, a heavy asset load) can't feed a
        // huge dt into physics/scripts and explode springs or tunnel bodies through colliders. A
        // stalled frame simply runs in slow motion instead of blowing up the simulation.
        _deltaTime = Math.Min(_deltaTime, MaxDeltaTime);

        // Publish the frame clock from the clamped real delta. Gameplay advances on the scaled delta
        // (Time.DeltaTime, respecting Time.TimeScale); engine services stay on the real delta so
        // pausing or slow-mo never starves audio, asset streaming, or the editor.
        Spot.Core.Time.NewFrame(_deltaTime);

        try
        {
            // Finish any background asset loads (build their GPU buffers) before this frame renders, so
            // newly-ready models can be drawn the same frame. Time-budgeted internally.
            ModelImporter.ProcessPendingUploads();

            SceneManager.ApplyPendingSwitch();
            SceneManager.Update(Spot.Core.Time.DeltaTime);

            foreach (var service in _services)
            {
                service.Update(_deltaTime);
            }
        }
        catch (Exception ex)
        {
            ReportFrameError("scene update", ex);
        }
    }

    private void Render()
    {
        try
        {
            Renderer.Clear();
            SceneManager.Render();
        }
        catch (Exception ex)
        {
            ReportFrameError("scene render", ex);
        }

        try
        {
            SceneManager.ImGuiRender();
            _console.OnImGuiRender();
            foreach (var service in _services)
            {
                service.ImGuiRender();
            }
        }
        catch (Exception ex)
        {
            ReportFrameError("UI render", ex);
        }
        finally
        {
            _imguiService?.RenderFrame();
        }

        _window!.SwapBuffers();

        if (!_frameFaulted)
        {
            _lastFrameError = null;
        }
    }

    private void Shutdown()
    {
        Log.CoreInfo("Shutting down '{0}'", _spec.Name);
        SceneManager.Shutdown();

        foreach (var service in _services)
        {
            service.Shutdown();
        }

        _window?.Dispose();
        _window = null;

        // Last, so every shutdown line above is flushed to the rolling log file before we release it.
        Log.CloseAndFlush();
    }

    /// <summary>
    /// An optional gate invoked when a window close is requested. Return <see langword="false"/> to
    /// veto the close (for example to first confirm unsaved changes); the application keeps running.
    /// Call <see cref="Quit"/> to close unconditionally once the user confirms.
    /// </summary>
    public Func<bool>? CanClose { get; set; }

    /// <summary>
    /// Requests the application to stop after the current frame.
    /// </summary>
    public void Quit() => _running = false;

    /// <summary>
    /// Logs a recovered per-frame exception without ever tearing the application down. Consecutive
    /// identical failures are collapsed so a fault that reoccurs every frame does not flood the
    /// console; a different (or cleared) error is logged again.
    /// </summary>
    private void ReportFrameError(string phase, Exception ex)
    {
        _frameFaulted = true;

        string signature = phase + ":" + ex.ToString();
        if (signature == _lastFrameError)
        {
            return;
        }

        _lastFrameError = signature;
        Log.CoreError("Recovered from an exception during {0}; continuing. {1}", phase, ex);
    }

    private void OnEvent(Event e)
    {
        try
        {
            Input.OnEvent(e);

            var dispatcher = new EventDispatcher(e);
            dispatcher.Dispatch<WindowCloseEvent>(OnWindowClose);
            dispatcher.Dispatch<WindowResizeEvent>(OnWindowResize);
            dispatcher.Dispatch<KeyTypedEvent>(OnKeyTyped);

            // While the engine owns input, keyboard/mouse events don't reach the game; non-input
            // events (e.g. window resize) still flow so the scene keeps its framebuffers correct.
            if (!e.Handled && !(Input.EngineCaptured && e.IsInCategory(EventCategory.Input)))
            {
                SceneManager.DispatchEvent(e);
            }
        }
        catch (Exception ex)
        {
            ReportFrameError("event handling", ex);
        }
    }

    private bool OnKeyTyped(KeyTypedEvent e)
    {
        if (e.Character == '\'' && !ImGui.GetIO().WantTextInput)
        {
            _console.Toggle();
            return true;
        }

        return false;
    }

    private bool OnWindowClose(WindowCloseEvent e)
    {
        if (CanClose != null && !CanClose())
        {
            _window?.CancelClose();
            return true;
        }

        _running = false;
        return true;
    }

    private bool OnWindowResize(WindowResizeEvent e)
    {
        Renderer.Api.Viewport(0, 0, (uint)e.Width, (uint)e.Height);
        return false;
    }
}

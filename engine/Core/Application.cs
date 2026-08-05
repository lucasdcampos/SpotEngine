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
    /// Gets or sets the root directory for assets.
    /// </summary>
    public string? AssetDirectory { get; set; }

    /// <summary>
    /// Gets or sets the path to the start scene to load automatically on startup.
    /// </summary>
    public string? StartScene { get; set; }
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

    public string EngineVersion => SpotEngine.GetVersion();

    public void AddService(IEngineService service)
    {
        _services.Add(service);
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

        _window = new Window(_spec.Window);
        _window.SetEventCallback(OnEvent);

        AddService(new GraphicsService());
        _imguiService = new ImGuiService(_spec);
        AddService(_imguiService);

        foreach (var service in _services)
        {
            service.Init(this);
        }

        _running = true;
        if (startScene is not null)
        {
            SceneManager.Load(startScene);
        }
        else if (!string.IsNullOrEmpty(_spec.StartScene))
        {
            if (!string.IsNullOrEmpty(_spec.AssetDirectory))
            {
                AssetPath.Root = _spec.AssetDirectory;
            }
            
            string scenePath = _spec.StartScene;
            if (!string.IsNullOrEmpty(_spec.AssetDirectory))
            {
                scenePath = Path.Combine(_spec.AssetDirectory, scenePath);
            }

            if (File.Exists(scenePath))
            {
                var realScene = new Scene();
                var serializer = new SceneSerializer(realScene);
                serializer.Deserialize(scenePath);
                SceneManager.Load(realScene);
            }
            else
            {
                Log.CoreError("Start scene not found: {0}", scenePath);
            }
        }

        _stopwatch = Stopwatch.StartNew();
        _lastTime = _stopwatch.Elapsed;
    }

    private void PollEvents()
    {
        _frameFaulted = false;
        Input.NewFrame();
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

        try
        {
            // Finish any background asset loads (build their GPU buffers) before this frame renders, so
            // newly-ready models can be drawn the same frame. Time-budgeted internally.
            ModelImporter.ProcessPendingUploads();

            SceneManager.ApplyPendingSwitch();
            SceneManager.Update(_deltaTime);

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

            if (!e.Handled)
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
        Log.CoreInfo("Window resized: {0}x{1}", e.Width, e.Height);
        return false;
    }
}

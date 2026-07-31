using System.Diagnostics;
using ImGuiNET;
using Silk.NET.OpenGL;
using Spot.Console;
using Spot.Events;
using Spot.Rendering;
using Spot.Scenes;
using ImGuiController = Silk.NET.OpenGL.Extensions.ImGui.ImGuiController;
using ImGuiFontConfig = Silk.NET.OpenGL.Extensions.ImGui.ImGuiFontConfig;

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
}

/// <summary>
/// Represents the running application and owns the main loop.
/// </summary>
public class Application
{
    private static Application? s_instance;

    private readonly ApplicationSpec _spec;
    private readonly DevConsole _console = new();
    private Window? _window;
    private GL? _gl;
    private ImGuiController? _imguiController;
    private bool _running;
    private float _deltaTime;
    private string? _lastFrameError;
    private bool _frameFaulted;

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
    /// Gets the OpenGL API for the current context. Used internally by the engine; game code
    /// renders through <see cref="Renderer"/> and the rendering resource types instead.
    /// </summary>
    internal GL Gl =>
        _gl ?? throw new InvalidOperationException("The OpenGL context has not been created yet.");

    /// <summary>
    /// Gets the developer console.
    /// </summary>
    public DevConsole Console => _console;

    public string EngineVersion => SpotEngine.GetVersion();

    /// <summary>
    /// Runs the main application loop until the application stops.
    /// </summary>
    /// <param name="startScene">The scene to load first (for example a menu).</param>
    public void Run(Scene? startScene = null)
    {
        Log.Init(new DevConsoleSink(_console));
        Log.CoreInfo("Initializing '{0}'", _spec.Name);

        _window = new Window(_spec.Window);
        _window.SetEventCallback(OnEvent);

        _gl = GL.GetApi(_window.NativeWindow);
        Renderer.Init(_gl);
        Renderer2D.Init();
        Renderer3D.Init();
        Spot.Assets.ModelImporter.Register(new Spot.Assets.AssimpModelImporter());
        Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);
        Log.CoreInfo("OpenGL {0}", _gl.GetStringS(StringName.Version));

        // Load a custom UI font when one is configured and present; otherwise fall back gracefully to
        // ImGui's default font. The font must be supplied at controller construction because that is
        // when the font atlas texture is built.
        ImGuiFontConfig? fontConfig = null;
        if (!string.IsNullOrEmpty(_spec.FontPath) && File.Exists(_spec.FontPath))
        {
            fontConfig = new ImGuiFontConfig(_spec.FontPath, _spec.FontSize);
        }
        else if (!string.IsNullOrEmpty(_spec.FontPath))
        {
            Log.CoreWarn("UI font not found at '{0}', using the default font.", _spec.FontPath);
        }

        _imguiController = new ImGuiController(_gl, _window.NativeWindow, _window.Input, fontConfig);
        ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        ImGui.StyleColorsDark();

        _running = true;
        if (startScene is not null)
        {
            SceneManager.Load(startScene);
        }

        var stopwatch = Stopwatch.StartNew();
        TimeSpan lastTime = stopwatch.Elapsed;
        while (_running)
        {
            _frameFaulted = false;

            // Top-level safety net: no single frame — game logic, a UI panel, an input/event handler,
            // or the window's own bookkeeping — is allowed to take the whole engine down. Anything that
            // throws here is logged and the loop continues. Only failures during startup, before this
            // loop (window/GL/ImGui creation), are treated as unrecoverable and left to stop the app.
            try
            {
                Input.NewFrame();
                _window.PollEvents();

                TimeSpan now = stopwatch.Elapsed;
                _deltaTime = (float)(now - lastTime).TotalSeconds;
                lastTime = now;

                // Apply any pending scene switch at the frame boundary, then run the active scene.
                try
                {
                    SceneManager.ApplyPendingSwitch();
                    SceneManager.Update(_deltaTime);

                    Renderer.Clear();
                    SceneManager.Render();
                }
                catch (Exception ex)
                {
                    ReportFrameError("scene update/render", ex);
                }

                // NewFrame (Update) must always be matched by Render so ImGui's frame stays balanced;
                // the finally guarantees that even when the scene-supplied UI in between throws.
                _imguiController.Update(_deltaTime);
                try
                {
                    SceneManager.ImGuiRender();
                    _console.OnImGuiRender();
                }
                catch (Exception ex)
                {
                    ReportFrameError("UI render", ex);
                }
                finally
                {
                    _imguiController.Render();
                }

                _window.SwapBuffers();
            }
            catch (Exception ex)
            {
                ReportFrameError("frame", ex);
            }

            // A clean frame clears the de-dup latch so the same fault is reported again if it returns.
            if (!_frameFaulted)
            {
                _lastFrameError = null;
            }
        }

        Log.CoreInfo("Shutting down '{0}'", _spec.Name);
        SceneManager.Shutdown();

        Renderer2D.Shutdown();
        _imguiController.Dispose();
        _window.Dispose();
        _imguiController = null;
        _gl = null;
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
        // Event handlers run arbitrary game and editor code — button clicks, key bindings, the
        // CanClose gate. A fault in any one of them must not crash the engine, so each event is
        // isolated here (and kept independent of others in the same poll) and merely logged.
        try
        {
            // The input state always sees every event, even ones handled below.
            Input.OnEvent(e);

            var dispatcher = new EventDispatcher(e);
            dispatcher.Dispatch<WindowCloseEvent>(OnWindowClose);
            dispatcher.Dispatch<WindowResizeEvent>(OnWindowResize);
            dispatcher.Dispatch<KeyTypedEvent>(OnKeyTyped);

            // Forward anything the engine did not consume to the active scene.
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
        // The console toggles on the apostrophe character. Using the typed character (rather than the
        // physical key) keeps the behavior correct regardless of the keyboard layout.
        if (e.Character == '\'' && !ImGui.GetIO().WantTextInput)
        {
            _console.Toggle();
            return true;
        }

        return false;
    }

    private bool OnWindowClose(WindowCloseEvent e)
    {
        // Let the application veto the close (for example to confirm unsaved changes).
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
        _gl?.Viewport(0, 0, (uint)e.Width, (uint)e.Height);
        Log.CoreInfo("Window resized: {0}x{1}", e.Width, e.Height);
        return false;
    }
}

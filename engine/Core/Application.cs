using System.Diagnostics;
using ImGuiNET;
using Silk.NET.OpenGL;
using Spot.Console;
using Spot.Events;
using Spot.Rendering;
using Spot.Scenes;
using ImGuiController = Silk.NET.OpenGL.Extensions.ImGui.ImGuiController;

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
        Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);
        Log.CoreInfo("OpenGL {0}", _gl.GetStringS(StringName.Version));

        _imguiController = new ImGuiController(_gl, _window.NativeWindow, _window.Input);
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
            _window.PollEvents();

            TimeSpan now = stopwatch.Elapsed;
            _deltaTime = (float)(now - lastTime).TotalSeconds;
            lastTime = now;

            // Apply any pending scene switch at the frame boundary, then run the active scene.
            SceneManager.ApplyPendingSwitch();
            SceneManager.Update(_deltaTime);

            Renderer.Clear();
            SceneManager.Render();

            _imguiController.Update(_deltaTime);
            SceneManager.ImGuiRender();
            _console.OnImGuiRender();
            _imguiController.Render();

            _window.SwapBuffers();
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
    /// Requests the application to stop after the current frame.
    /// </summary>
    public void Quit() => _running = false;

    private void OnEvent(Event e)
    {
        var dispatcher = new EventDispatcher(e);
        dispatcher.Dispatch<WindowCloseEvent>(OnWindowClose);
        dispatcher.Dispatch<WindowResizeEvent>(OnWindowResize);
        dispatcher.Dispatch<KeyTypedEvent>(OnKeyTyped);
    }

    private bool OnKeyTyped(KeyTypedEvent e)
    {
        // The console toggles on the apostrophe character. Using the typed character (rather than the
        // physical key) keeps the behavior correct regardless of the keyboard layout.
        if (e.KeyCode == '\'' && !ImGui.GetIO().WantTextInput)
        {
            _console.Toggle();
            return true;
        }

        return false;
    }

    private bool OnWindowClose(WindowCloseEvent e)
    {
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

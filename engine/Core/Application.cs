using System;
using System.Collections.Generic;
using System.Diagnostics;
using ImGuiNET;
using Spot.Console;
using Spot.Events;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Core.Services;

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

        try
        {
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

using System.Diagnostics;

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
    private Window? _window;
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
    /// Runs the main application loop until the application stops.
    /// </summary>
    public void Run()
    {
        Log.Init();
        Log.CoreInfo("Initializing '{0}'", _spec.Name);

        _window = new Window(_spec.Window);

        _running = true;
        OnInit();

        var stopwatch = Stopwatch.StartNew();
        TimeSpan lastTime = stopwatch.Elapsed;
        while (_running && !_window.ShouldClose())
        {
            _window.PollEvents();

            TimeSpan now = stopwatch.Elapsed;
            _deltaTime = (float)(now - lastTime).TotalSeconds;
            lastTime = now;

            OnUpdate(_deltaTime);
        }

        Log.CoreInfo("Shutting down '{0}'", _spec.Name);
        OnShutdown();

        _window.Dispose();
        _window = null;
    }

    /// <summary>
    /// Requests the application to stop after the current frame.
    /// </summary>
    public void Quit() => _running = false;

    /// <summary>
    /// Called once after the application starts.
    /// </summary>
    protected virtual void OnInit()
    {
    }

    /// <summary>
    /// Called every frame with the elapsed time since the previous frame.
    /// </summary>
    /// <param name="deltaTime">The elapsed time in seconds.</param>
    protected virtual void OnUpdate(float deltaTime)
    {
    }

    /// <summary>
    /// Called once before the application stops.
    /// </summary>
    protected virtual void OnShutdown()
    {
    }
}

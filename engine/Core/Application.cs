namespace Spot.Core;

/// <summary>
/// Represents the running application and owns the main loop.
/// </summary>
public class Application
{
    private bool _running;

    /// <summary>
    /// Initializes a new instance of the <see cref="Application"/> class.
    /// </summary>
    public Application()
    {
        _running = false;
    }

    /// <summary>
    /// Runs the main application loop until the application stops.
    /// </summary>
    public void Run()
    {
        _running = true;
        int steps = 0;
        Console.WriteLine("Application initializing");
        while (_running)
        {
            steps++;
            if (steps > 999)
            {
                _running = false;
            }
        }

        Console.WriteLine("Application stopping");
    }
}

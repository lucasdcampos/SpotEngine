using Spot;
using Spot.Core;

var spec = new ApplicationSpec
{
    Name = "My Game",
};

var app = new GameApp(spec);
app.Run();

internal sealed class GameApp : Application
{
    private int _frames;

    public GameApp(ApplicationSpec spec)
        : base(spec)
    {
    }

    protected override void OnInit()
    {
        Log.Info("Game started - engine version {0}", Engine.GetVersion());
    }

    protected override void OnUpdate(float deltaTime)
    {
        _frames++;
        if (_frames >= 5)
        {
            Log.Info("Completed {0} frames, shutting down", _frames);
            Quit();
        }
    }

    protected override void OnShutdown()
    {
        Log.Info("Game shutdown complete");
    }
}

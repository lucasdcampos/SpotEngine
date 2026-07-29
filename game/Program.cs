using Spot;
using Spot.Core;

var spec = new ApplicationSpec
{
    Name = "My Game",
};
spec.Window.Title = "My Game";
spec.Window.Width = 1280;
spec.Window.Height = 720;

var app = new GameApp(spec);
app.Run();

internal sealed class GameApp : Application
{
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
    }

    protected override void OnShutdown()
    {
        Log.Info("Game shutdown complete");
    }
}

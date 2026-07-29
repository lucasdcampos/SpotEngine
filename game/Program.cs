using System.Globalization;
using Silk.NET.OpenGL;
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
    private bool _godMode;

    public GameApp(ApplicationSpec spec)
        : base(spec)
    {
    }

    protected override void OnInit()
    {
        Log.Info("Game started - engine version {0}", Engine.GetVersion());
        Gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);

        Console.Register("god", _ =>
        {
            _godMode = !_godMode;
            Console.Print(_godMode ? "God mode ON" : "God mode OFF");
        }, "Toggle god mode");

        Console.Register("bg", args =>
        {
            if (args.Count < 3)
            {
                Instance.Console.Print("[error] Usage: bg <r> <g> <b>  (values 0-255)");
                return;
            }

            Gl.ClearColor(
                float.Parse(args[0], CultureInfo.InvariantCulture) / 255.0f,
                float.Parse(args[1], CultureInfo.InvariantCulture) / 255.0f,
                float.Parse(args[2], CultureInfo.InvariantCulture) / 255.0f,
                1.0f);
        }, "Set background color: bg <r> <g> <b>");
    }

    protected override void OnUpdate(float deltaTime)
    {
    }

    protected override void OnRender()
    {
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    protected override void OnShutdown()
    {
        Log.Info("Game shutdown complete");
    }
}

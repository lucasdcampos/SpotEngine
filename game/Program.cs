using ImGuiNET;
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
    public GameApp(ApplicationSpec spec)
        : base(spec)
    {
    }

    protected override void OnInit()
    {
        Log.Info("Game started - engine version {0}", Engine.GetVersion());
        Gl.ClearColor(0.1f, 0.1f, 0.15f, 1.0f);
    }

    protected override void OnUpdate(float deltaTime)
    {
    }

    protected override void OnRender()
    {
        Gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    protected override void OnImGuiRender()
    {
        ImGui.Begin("SpotEngine");
        ImGui.Text($"FPS: {ImGui.GetIO().Framerate:F1}");
        ImGui.End();
    }

    protected override void OnShutdown()
    {
        Log.Info("Game shutdown complete");
    }
}

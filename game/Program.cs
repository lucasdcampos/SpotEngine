using System.Globalization;
using System.Numerics;
using Spot;
using Spot.Core;
using Spot.Rendering;

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
    private OrthographicCamera? _camera;
    private Texture2D? _texture;
    private readonly Transform _spriteTransform = new();

    public GameApp(ApplicationSpec spec)
        : base(spec)
    {
    }

    protected override void OnInit()
    {
        Log.Info("Game started - engine version {0}", Engine.GetVersion());
        Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);

        // Assets are copied next to the executable, so resolve them relative to the base
        // directory rather than the (unpredictable) current working directory.
        _texture = new Texture2D(Path.Combine(AppContext.BaseDirectory, "assets", "spot.png"));

        // A 2D orthographic camera: the view is two world units tall, and its width follows the
        // window aspect ratio (kept up to date in OnUpdate).
        float aspect = (float)Window.Width / Window.Height;
        _camera = new OrthographicCamera(-aspect, aspect, -1.0f, 1.0f);

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

            Renderer.SetClearColor(
                float.Parse(args[0], CultureInfo.InvariantCulture) / 255.0f,
                float.Parse(args[1], CultureInfo.InvariantCulture) / 255.0f,
                float.Parse(args[2], CultureInfo.InvariantCulture) / 255.0f,
                1.0f);
        }, "Set background color: bg <r> <g> <b>");
    }

    protected override void OnUpdate(float deltaTime)
    {
        // Keep the camera's horizontal extent matched to the current window aspect ratio.
        float aspect = (float)Window.Width / Window.Height;
        _camera?.SetProjection(-aspect, aspect, -1.0f, 1.0f);

        // Spin the textured sprite to show a transform driving a quad.
        _spriteTransform.Rotation += new Vector3(0.0f, 0.0f, 45.0f * deltaTime);
    }

    protected override void OnRender()
    {
        Renderer.Clear();

        if (_camera is null || _texture is null)
        {
            return;
        }

        Renderer2D.BeginScene(_camera);

        // A grid of colored quads: all batched into a single draw call.
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                var position = new Vector2(-1.2f + (x * 0.22f), -0.44f + (y * 0.22f));
                var color = new Vector4(x / 5.0f, y / 5.0f, 0.4f, 1.0f);
                Renderer2D.DrawQuad(position, new Vector2(0.18f, 0.18f), color);
            }
        }

        // A textured sprite, transformed (scaled + spinning) on top.
        _spriteTransform.Position = new Vector3(0.7f, 0.0f, 0.0f);
        _spriteTransform.Scale = new Vector3(0.8f, 0.8f, 1.0f);
        Renderer2D.DrawQuad(_spriteTransform.Matrix, _texture);

        Renderer2D.EndScene();
    }

    protected override void OnShutdown()
    {
        _texture?.Dispose();
        Log.Info("Game shutdown complete");
    }
}

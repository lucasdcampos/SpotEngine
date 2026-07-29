using System.Globalization;
using System.Numerics;
using Spot;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;

var spec = new ApplicationSpec
{
    Name = "My Game",
};
spec.Window.Title = "My Game";
spec.Window.Width = 1280;
spec.Window.Height = 720;

var app = new GameApp(spec);
app.Run();

/// <summary>A user-defined component holding a quad's color.</summary>
internal sealed class QuadColor
{
    public Vector4 Value { get; set; } = Vector4.One;
}

internal sealed class GameApp : Application
{
    private bool _godMode;
    private OrthographicCamera? _camera;
    private readonly Scene _scene = new();

    public GameApp(ApplicationSpec spec)
        : base(spec)
    {
    }

    protected override void OnInit()
    {
        Log.Info("Game started - engine version {0}", Engine.GetVersion());
        Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);

        // A 2D orthographic camera: the view is two world units tall, and its width follows the
        // window aspect ratio (kept up to date in OnUpdate).
        float aspect = (float)Window.Width / Window.Height;
        _camera = new OrthographicCamera(-aspect, aspect, -1.0f, 1.0f);

        // Populate the scene: a row of entities, each with its Transform (added automatically) and
        // a user-defined QuadColor component.
        const int count = 6;
        for (int i = 0; i < count; i++)
        {
            Entity entity = _scene.CreateEntity($"Quad {i}");

            Transform transform = entity.GetComponent<Transform>();
            transform.Position = new Vector3(-1.1f + (i * 0.44f), 0.0f, 0.0f);
            transform.Scale = new Vector3(0.3f, 0.3f, 1.0f);

            entity.AddComponent(new QuadColor
            {
                Value = new Vector4(i / (float)count, 0.5f, 1.0f - (i / (float)count), 1.0f),
            });
        }

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

        // Spin every entity in place by mutating its Transform component.
        foreach (Entity entity in _scene.View<Transform>())
        {
            entity.GetComponent<Transform>().Rotation += new Vector3(0.0f, 0.0f, 45.0f * deltaTime);
        }
    }

    protected override void OnRender()
    {
        Renderer.Clear();

        if (_camera is null)
        {
            return;
        }

        // Render the scene: draw a quad for every entity that has both a Transform and a QuadColor.
        Renderer2D.BeginScene(_camera);
        foreach (Entity entity in _scene.View<Transform, QuadColor>())
        {
            Renderer2D.DrawQuad(entity.GetComponent<Transform>().Matrix, entity.GetComponent<QuadColor>().Value);
        }

        Renderer2D.EndScene();
    }

    protected override void OnShutdown()
    {
        Log.Info("Game shutdown complete");
    }
}

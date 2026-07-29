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

internal sealed class GameApp : Application
{
    private bool _godMode;
    private OrthographicCamera? _camera;
    private Texture2D? _texture;
    private readonly Scene _scene = new();

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

        // A row of solid-color sprites...
        const int count = 5;
        for (int i = 0; i < count; i++)
        {
            Entity entity = _scene.CreateEntity($"Quad {i}");

            Transform transform = entity.GetComponent<Transform>();
            transform.Position = new Vector3(-1.1f + (i * 0.34f), -0.5f, 0.0f);
            transform.Scale = new Vector3(0.25f, 0.25f, 1.0f);

            entity.AddComponent(new Sprite2D
            {
                Color = new Vector4(i / (float)count, 0.5f, 1.0f - (i / (float)count), 1.0f),
            });
        }

        // ...and one textured sprite in the middle.
        Entity logo = _scene.CreateEntity("Logo");
        Transform logoTransform = logo.GetComponent<Transform>();
        logoTransform.Position = new Vector3(0.0f, 0.35f, 0.0f);
        logoTransform.Scale = new Vector3(0.9f, 0.9f, 1.0f);
        logo.AddComponent(new Sprite2D { Texture = _texture });

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

        // Render the scene: draw a quad for every entity that has both a Transform and a Sprite2D.
        // (A dedicated render system will encapsulate this loop later.)
        Renderer2D.BeginScene(_camera);
        foreach (Entity entity in _scene.View<Transform, Sprite2D>())
        {
            Transform transform = entity.GetComponent<Transform>();
            Sprite2D sprite = entity.GetComponent<Sprite2D>();

            if (sprite.Texture is not null)
            {
                Renderer2D.DrawQuad(transform.Matrix, sprite.Texture, sprite.Color);
            }
            else
            {
                Renderer2D.DrawQuad(transform.Matrix, sprite.Color);
            }
        }

        Renderer2D.EndScene();
    }

    protected override void OnShutdown()
    {
        _texture?.Dispose();
        Log.Info("Game shutdown complete");
    }
}

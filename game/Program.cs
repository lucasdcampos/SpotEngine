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
    private const string VertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aColor;
        layout (location = 2) in vec2 aTexCoord;

        uniform mat4 uViewProjection;
        uniform mat4 uTransform;

        out vec3 vColor;
        out vec2 vTexCoord;

        void main()
        {
            vColor = aColor;
            vTexCoord = aTexCoord;
            gl_Position = uViewProjection * uTransform * vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource =
        """
        #version 330 core
        in vec3 vColor;
        in vec2 vTexCoord;

        uniform sampler2D uTexture;

        out vec4 fragColor;

        void main()
        {
            fragColor = texture(uTexture, vTexCoord) * vec4(vColor, 1.0);
        }
        """;

    // A quad: four corners, each with a position (vec3), a color (vec3), and a texture coordinate (vec2).
    private static readonly float[] Vertices =
    {
        // Position            // Color              // UV
         0.5f,  0.5f, 0.0f,    1.0f, 0.0f, 0.0f,    1.0f, 1.0f, // top-right    - red
         0.5f, -0.5f, 0.0f,    0.0f, 1.0f, 0.0f,    1.0f, 0.0f, // bottom-right - green
        -0.5f, -0.5f, 0.0f,    0.0f, 0.0f, 1.0f,    0.0f, 0.0f, // bottom-left  - blue
        -0.5f,  0.5f, 0.0f,    1.0f, 1.0f, 0.0f,    0.0f, 1.0f, // top-left     - yellow
    };

    // Two triangles making up the quad.
    private static readonly uint[] Indices =
    {
        0, 1, 3,
        1, 2, 3,
    };

    private bool _godMode;
    private Shader? _shader;
    private VertexBuffer? _vbo;
    private IndexBuffer? _ibo;
    private VertexArray? _vao;
    private OrthographicCamera? _camera;
    private Texture2D? _texture;
    private readonly Transform _quadTransform = new();

    public GameApp(ApplicationSpec spec)
        : base(spec)
    {
    }

    protected override void OnInit()
    {
        Log.Info("Game started - engine version {0}", Engine.GetVersion());
        Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);

        // Build the quad: a vertex buffer with position + color per vertex, an index buffer
        // pairing them into two triangles, and a shader program to color it.
        _vao = new VertexArray();
        _vbo = new VertexBuffer(Vertices, ShaderDataType.Float3, ShaderDataType.Float3, ShaderDataType.Float2);
        _vao.AddVertexBuffer(_vbo);
        _ibo = new IndexBuffer(Indices);
        _vao.SetIndexBuffer(_ibo);

        _shader = new Shader(VertexShaderSource, FragmentShaderSource);
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

        // Spin the quad to show transforms driving the model matrix.
        _quadTransform.Rotation += new Vector3(0.0f, 0.0f, 45.0f * deltaTime);
    }

    protected override void OnRender()
    {
        Renderer.Clear();

        if (_shader is not null && _camera is not null && _vao is not null)
        {
            _texture?.Bind(0);

            _shader.Use();
            _shader.SetUniform("uViewProjection", _camera.ViewProjection);
            _shader.SetUniform("uTransform", _quadTransform.Matrix);
            _shader.SetUniform("uTexture", 0);
            Renderer.DrawIndexed(_vao);
        }
    }

    protected override void OnShutdown()
    {
        _shader?.Dispose();
        _texture?.Dispose();
        _vbo?.Dispose();
        _ibo?.Dispose();
        _vao?.Dispose();
        Log.Info("Game shutdown complete");
    }
}

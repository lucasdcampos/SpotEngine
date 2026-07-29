using System.Globalization;
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

        out vec3 vColor;

        void main()
        {
            vColor = aColor;
            gl_Position = vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource =
        """
        #version 330 core
        in vec3 vColor;

        out vec4 fragColor;

        void main()
        {
            fragColor = vec4(vColor, 1.0);
        }
        """;

    // A quad: four corners, each with a position (vec3) and a color (vec3).
    private static readonly float[] Vertices =
    {
        // Position            // Color
         0.5f,  0.5f, 0.0f,    1.0f, 0.0f, 0.0f, // top-right    - red
         0.5f, -0.5f, 0.0f,    0.0f, 1.0f, 0.0f, // bottom-right - green
        -0.5f, -0.5f, 0.0f,    0.0f, 0.0f, 1.0f, // bottom-left  - blue
        -0.5f,  0.5f, 0.0f,    1.0f, 1.0f, 0.0f, // top-left     - yellow
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
        _vbo = new VertexBuffer(Vertices, ShaderDataType.Float3, ShaderDataType.Float3);
        _vao.AddVertexBuffer(_vbo);
        _ibo = new IndexBuffer(Indices);
        _vao.SetIndexBuffer(_ibo);

        _shader = new Shader(VertexShaderSource, FragmentShaderSource);

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
    }

    protected override void OnRender()
    {
        Renderer.Clear();

        _shader?.Use();
        if (_vao is not null)
        {
            Renderer.DrawIndexed(_vao);
        }
    }

    protected override void OnShutdown()
    {
        _shader?.Dispose();
        _vbo?.Dispose();
        _ibo?.Dispose();
        _vao?.Dispose();
        Log.Info("Game shutdown complete");
    }
}

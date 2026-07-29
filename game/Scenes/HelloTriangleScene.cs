using ImGuiNET;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Game.Scenes;

/// <summary>
/// A scene that draws a triangle using the low-level rendering API directly: a shader, a vertex
/// array/buffer, and a raw draw call -- no scene entities or Renderer2D involved.
/// </summary>
internal sealed class HelloTriangleScene : Scene
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

    private static readonly float[] Vertices =
    {
        // Position            // Color
         0.0f,  0.5f, 0.0f,    1.0f, 0.0f, 0.0f, // top    - red
         0.5f, -0.5f, 0.0f,    0.0f, 1.0f, 0.0f, // right  - green
        -0.5f, -0.5f, 0.0f,    0.0f, 0.0f, 1.0f, // left   - blue
    };

    private Shader? _shader;
    private VertexBuffer? _vbo;
    private VertexArray? _vao;

    public override void OnEnter()
    {
        Renderer.SetClearColor(0.05f, 0.05f, 0.08f, 1.0f);

        _vao = new VertexArray();
        _vbo = new VertexBuffer(Vertices, ShaderDataType.Float3, ShaderDataType.Float3);
        _vao.AddVertexBuffer(_vbo);
        _shader = new Shader(VertexShaderSource, FragmentShaderSource);
    }

    public override void OnRender()
    {
        _shader?.Use();
        if (_vao is not null)
        {
            Renderer.DrawArrays(_vao, 3);
        }
    }

    public override void OnImGuiRender()
    {
        ImGui.Begin("Hello Triangle");
        ImGui.TextUnformatted("Low-level API: Shader + VertexArray + Renderer.DrawArrays");
        ImGui.Separator();
        if (ImGui.Button("Back to menu"))
        {
            SceneManager.Load(new MenuScene());
        }

        ImGui.End();
    }

    public override void OnExit()
    {
        _shader?.Dispose();
        _vbo?.Dispose();
        _vao?.Dispose();
    }
}

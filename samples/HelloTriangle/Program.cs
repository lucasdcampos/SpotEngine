// Level 1 — the lowest layer: a triangle drawn straight through the graphics device (IGraphicsDevice), the same
// backend-neutral API every Spot renderer is built on. No wrappers, no batching — just buffers, a vertex array
// and a shader program. Escape quits; `--frames N` exits after N frames (smoke tests).
using Spot.Engine;
using Spot.Engine.Graphics;

const string VertexSource = """
    #version 330 core
    layout (location = 0) in vec2 aPosition;
    layout (location = 1) in vec3 aColor;
    uniform float uAngle;
    out vec3 vColor;
    void main()
    {
        float c = cos(uAngle), s = sin(uAngle);
        vColor = aColor;
        gl_Position = vec4(c * aPosition.x - s * aPosition.y, s * aPosition.x + c * aPosition.y, 0.0, 1.0);
    }
    """;

const string FragmentSource = """
    #version 330 core
    in vec3 vColor;
    out vec4 fragColor;
    void main() { fragColor = vec4(vColor, 1.0); }
    """;

int maxFrames = args.Length == 2 && args[0] == "--frames" && int.TryParse(args[1], out int n) ? n : -1;

using var window = new Window(new WindowSpec { Title = "Spot — Hello Triangle (raw device)", Width = 800, Height = 600 });
IGraphicsDevice gpu = Renderer.Device;

// Interleaved vertices: position (x, y), color (r, g, b).
float[] vertices =
{
    0.0f, 0.6f, 1.0f, 0.3f, 0.2f,
    -0.6f, -0.45f, 0.2f, 1.0f, 0.3f,
    0.6f, -0.45f, 0.2f, 0.4f, 1.0f,
};

VertexArrayHandle vao = gpu.CreateVertexArray();
gpu.BindVertexArray(vao);
BufferHandle vbo = gpu.CreateBuffer();
gpu.BindBuffer(BufferKind.Vertex, vbo);
gpu.BufferData<float>(BufferKind.Vertex, vertices, BufferUsageKind.StaticDraw);
const uint Stride = 5 * sizeof(float);
gpu.EnableVertexAttribArray(0);
gpu.VertexAttribPointer(0, 2, VertexAttribType.Float, false, Stride, 0);
gpu.EnableVertexAttribArray(1);
gpu.VertexAttribPointer(1, 3, VertexAttribType.Float, false, Stride, 2 * sizeof(float));

ShaderHandle Compile(ShaderStage stage, string source)
{
    ShaderHandle shader = gpu.CreateShader(stage);
    gpu.ShaderSource(shader, gpu.PreprocessShaderSource(stage, source)); // adapts GLSL to the backend
    gpu.CompileShader(shader);
    if (!gpu.GetShaderCompileStatus(shader))
    {
        Log.Error("{0} shader: {1}", stage, gpu.GetShaderInfoLog(shader));
    }

    return shader;
}

ShaderHandle vs = Compile(ShaderStage.Vertex, VertexSource);
ShaderHandle fs = Compile(ShaderStage.Fragment, FragmentSource);
ProgramHandle program = gpu.CreateProgram();
gpu.AttachShader(program, vs);
gpu.AttachShader(program, fs);
gpu.LinkProgram(program);
if (!gpu.GetProgramLinkStatus(program))
{
    Log.Error("Link: {0}", gpu.GetProgramInfoLog(program));
}

gpu.DeleteShader(vs);
gpu.DeleteShader(fs);
UniformLocation angle = gpu.GetUniformLocation(program, "uAngle");

for (int frame = 0; window.IsOpen && frame != maxFrames; frame++)
{
    window.PollEvents();
    Time.Tick();
    if (Input.GetKeyDown(Key.Escape))
    {
        window.Close();
    }

    gpu.SetClearColor(0.05f, 0.05f, 0.07f, 1.0f);
    gpu.Clear(color: true, depth: false);
    gpu.UseProgram(program);
    gpu.SetUniform(angle, Time.ElapsedTime);
    gpu.BindVertexArray(vao);
    gpu.DrawArrays(PrimitiveKind.Triangles, 0, 3);

    window.SwapBuffers();
}

gpu.DeleteProgram(program);
gpu.DeleteBuffer(vbo);
gpu.DeleteVertexArray(vao);

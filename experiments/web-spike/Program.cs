using System;
using System.Runtime.InteropServices.JavaScript;
using StbImageSharp;

// Entry point. Runs once; rendering is driven from JS via [JSExport] Init/Frame.
Console.WriteLine("[spike] .NET wasm runtime is alive.");
try
{
    Spike.ManagedDependencySmokeTest();
}
catch (Exception ex)
{
    // Non-fatal: never let the smoke test abort rendering.
    Console.WriteLine("[spike] SMOKE TEST FAILED: " + ex.GetType().FullName + ": " + ex.Message);
    Console.WriteLine(ex.StackTrace);
}

// GL enum constants (standard OpenGL ES / WebGL2 values).
static class Gl
{
    public const int DepthBufferBit = 0x0100;
    public const int ColorBufferBit = 0x4000;
    public const int Triangles = 0x0004;
    public const int TriangleStrip = 0x0005;
    public const int ArrayBuffer = 0x8892;
    public const int StaticDraw = 0x88E4;
    public const int Float = 0x1406;
    public const int FragmentShader = 0x8B30;
    public const int VertexShader = 0x8B31;
    public const int CompileStatus = 0x8B81;
    public const int LinkStatus = 0x8B82;
    public const int DepthTest = 0x0B71;
}

partial class Spike
{
    // ---- WebGL2 interop surface (implemented in main.js). C# drives every GL call. ----
    [JSImport("gl.viewport", "main.js")] internal static partial void Viewport(int x, int y, int w, int h);
    [JSImport("gl.clearColor", "main.js")] internal static partial void ClearColor(double r, double g, double b, double a);
    [JSImport("gl.clear", "main.js")] internal static partial void Clear(int mask);
    [JSImport("gl.enable", "main.js")] internal static partial void Enable(int cap);

    [JSImport("gl.createShader", "main.js")] internal static partial int CreateShader(int type);
    [JSImport("gl.shaderSource", "main.js")] internal static partial void ShaderSource(int shader, string src);
    [JSImport("gl.compileShader", "main.js")] internal static partial void CompileShader(int shader);
    [JSImport("gl.getShaderCompileStatus", "main.js")] internal static partial bool GetShaderCompileStatus(int shader);
    [JSImport("gl.getShaderInfoLog", "main.js")] internal static partial string GetShaderInfoLog(int shader);

    [JSImport("gl.createProgram", "main.js")] internal static partial int CreateProgram();
    [JSImport("gl.attachShader", "main.js")] internal static partial void AttachShader(int program, int shader);
    [JSImport("gl.linkProgram", "main.js")] internal static partial void LinkProgram(int program);
    [JSImport("gl.getProgramLinkStatus", "main.js")] internal static partial bool GetProgramLinkStatus(int program);
    [JSImport("gl.getProgramInfoLog", "main.js")] internal static partial string GetProgramInfoLog(int program);
    [JSImport("gl.useProgram", "main.js")] internal static partial void UseProgram(int program);
    [JSImport("gl.getUniformLocation", "main.js")] internal static partial int GetUniformLocation(int program, string name);
    [JSImport("gl.uniform1f", "main.js")] internal static partial void Uniform1f(int loc, double v);
    [JSImport("gl.uniform3f", "main.js")] internal static partial void Uniform3f(int loc, double r, double g, double b);

    [JSImport("gl.createBuffer", "main.js")] internal static partial int CreateBuffer();
    [JSImport("gl.bindBuffer", "main.js")] internal static partial void BindBuffer(int target, int buffer);
    [JSImport("gl.bufferData", "main.js")] internal static partial void BufferData(int target, [JSMarshalAs<JSType.Array<JSType.Number>>] double[] data, int usage);

    [JSImport("gl.createVertexArray", "main.js")] internal static partial int CreateVertexArray();
    [JSImport("gl.bindVertexArray", "main.js")] internal static partial void BindVertexArray(int vao);
    [JSImport("gl.enableVertexAttribArray", "main.js")] internal static partial void EnableVertexAttribArray(int index);
    [JSImport("gl.vertexAttribPointer", "main.js")] internal static partial void VertexAttribPointer(int index, int size, int type, bool normalized, int stride, int offset);

    [JSImport("gl.drawArrays", "main.js")] internal static partial void DrawArrays(int mode, int first, int count);

    private const string VertexSrc = """
        #version 300 es
        precision highp float;
        layout(location = 0) in vec2 aPos;
        uniform float uAngle;
        void main()
        {
            float c = cos(uAngle);
            float s = sin(uAngle);
            mat2 rot = mat2(c, -s, s, c);
            gl_Position = vec4(rot * aPos, 0.0, 1.0);
        }
        """;

    private const string FragmentSrc = """
        #version 300 es
        precision highp float;
        uniform vec3 uColor;
        out vec4 fragColor;
        void main()
        {
            fragColor = vec4(uColor, 1.0);
        }
        """;

    private static int s_program;
    private static int s_uAngle;
    private static int s_uColor;
    private static float s_time;

    // Called from JS once the canvas + WebGL2 context exist.
    [JSExport]
    internal static void Init(int width, int height)
    {
        Viewport(0, 0, width, height);
        Enable(Gl.DepthTest);

        int vs = CompileShaderOrThrow(Gl.VertexShader, VertexSrc, "vertex");
        int fs = CompileShaderOrThrow(Gl.FragmentShader, FragmentSrc, "fragment");

        s_program = CreateProgram();
        AttachShader(s_program, vs);
        AttachShader(s_program, fs);
        LinkProgram(s_program);
        if (!GetProgramLinkStatus(s_program))
            throw new InvalidOperationException("Program link failed: " + GetProgramInfoLog(s_program));

        s_uAngle = GetUniformLocation(s_program, "uAngle");
        s_uColor = GetUniformLocation(s_program, "uColor");

        // Unit quad as a triangle strip.
        double[] verts =
        {
            -0.5, -0.5,
             0.5, -0.5,
            -0.5,  0.5,
             0.5,  0.5,
        };
        int vao = CreateVertexArray();
        BindVertexArray(vao);
        int vbo = CreateBuffer();
        BindBuffer(Gl.ArrayBuffer, vbo);
        BufferData(Gl.ArrayBuffer, verts, Gl.StaticDraw);
        EnableVertexAttribArray(0);
        VertexAttribPointer(0, 2, Gl.Float, false, 0, 0);

        Console.WriteLine("[spike] GL init complete: shaders compiled + linked, quad uploaded.");
    }

    // Called every animation frame from JS with the delta in seconds.
    [JSExport]
    internal static void Frame(double dt)
    {
        s_time += (float)dt;

        ClearColor(0.06, 0.08, 0.12, 1.0);
        Clear(Gl.ColorBufferBit | Gl.DepthBufferBit);

        UseProgram(s_program);
        Uniform1f(s_uAngle, s_time);

        // Cycle color over time so motion is obvious.
        double r = 0.5 + 0.5 * Math.Sin(s_time * 1.7);
        double g = 0.5 + 0.5 * Math.Sin(s_time * 1.7 + 2.094);
        double b = 0.5 + 0.5 * Math.Sin(s_time * 1.7 + 4.188);
        Uniform3f(s_uColor, r, g, b);

        DrawArrays(Gl.TriangleStrip, 0, 4);
    }

    private static int CompileShaderOrThrow(int type, string src, string label)
    {
        int shader = CreateShader(type);
        ShaderSource(shader, src);
        CompileShader(shader);
        if (!GetShaderCompileStatus(shader))
            throw new InvalidOperationException($"{label} shader compile failed: " + GetShaderInfoLog(shader));
        return shader;
    }

    // Proves real managed NuGets (StbImageWriteSharp + StbImageSharp, used by the engine's
    // asset path) link and execute under wasm + trimming: encode a 2x2 PNG, then decode it back.
    internal static void ManagedDependencySmokeTest()
    {
        byte[] pixels =
        {
            255, 0, 0, 255,   0, 255, 0, 255,
            0, 0, 255, 255,   255, 255, 0, 255,
        };
        byte[] png;
        using (var ms = new System.IO.MemoryStream())
        {
            new StbImageWriteSharp.ImageWriter()
                .WritePng(pixels, 2, 2, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, ms);
            png = ms.ToArray();
        }
        Console.WriteLine($"[spike] smoke: encoded PNG = {png.Length} bytes; decoding...");
        ImageResult image = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
        Console.WriteLine($"[spike] StbImage round-trip OK: {image.Width}x{image.Height}, {image.Data.Length} bytes. Managed deps work under wasm.");
    }
}

using System.Numerics;

namespace Spot.Rendering;

/// <summary>
/// A batched renderer for screen-space user interface: solid and textured quads, nine-slice sprites and
/// text, all in pixel coordinates with the origin at the top-left and y pointing down. Like
/// <see cref="Renderer2D"/> it accumulates quads into one buffer and draws them together, but it always
/// alpha-blends (so glyph edges and translucent panels blend smoothly instead of being alpha-tested) and
/// supports a stack of scissor rectangles for clipping. It runs as the final pass of the frame, after
/// post-processing, straight to the screen.
/// </summary>
public static class UIRenderer
{
    private const int MaxQuads = 8_000;
    private const int MaxVertices = MaxQuads * 4;
    private const int MaxIndices = MaxQuads * 6;
    private const int FloatsPerVertex = 3 + 4 + 2; // position, color, texture coordinate

    private const string VertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec4 aColor;
        layout (location = 2) in vec2 aTexCoord;

        uniform mat4 uProjection;

        out vec4 vColor;
        out vec2 vTexCoord;

        void main()
        {
            vColor = aColor;
            vTexCoord = aTexCoord;
            gl_Position = uProjection * vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource =
        """
        #version 330 core
        in vec4 vColor;
        in vec2 vTexCoord;

        uniform sampler2D uTexture;

        out vec4 fragColor;

        void main()
        {
            // The glyph atlas and the 1x1 white quad both keep RGB at 1 and carry coverage in alpha, so
            // texture * color yields colored, smoothly blended text and solid quads through one path.
            fragColor = texture(uTexture, vTexCoord) * vColor;
        }
        """;

    private static VertexArray? s_vao;
    private static VertexBuffer? s_vbo;
    private static IndexBuffer? s_ibo;
    private static Shader? s_shader;
    private static Texture2D? s_whiteTexture;

    private static float[] s_vertices = Array.Empty<float>();
    private static int s_vertexCursor;
    private static uint s_indexCount;
    private static Texture2D? s_currentTexture;

    private static Matrix4x4 s_projection = Matrix4x4.Identity;
    private static float s_screenWidth;
    private static float s_screenHeight;
    private static bool s_active;

    private static readonly List<PositionedGlyph> s_glyphScratch = new(256);
    private static readonly List<Vector4> s_clipStack = new();

    /// <summary>Creates the shared batch resources. Called once by the application after the renderer is ready.</summary>
    internal static void Init()
    {
        s_vertices = new float[MaxVertices * FloatsPerVertex];

        s_vao = new VertexArray();
        s_vbo = new VertexBuffer(
            (uint)s_vertices.Length,
            ShaderDataType.Float3,
            ShaderDataType.Float4,
            ShaderDataType.Float2);
        s_vao.AddVertexBuffer(s_vbo);

        uint[] indices = new uint[MaxIndices];
        uint offset = 0;
        for (int i = 0; i < MaxIndices; i += 6)
        {
            indices[i + 0] = offset + 0;
            indices[i + 1] = offset + 1;
            indices[i + 2] = offset + 2;
            indices[i + 3] = offset + 2;
            indices[i + 4] = offset + 3;
            indices[i + 5] = offset + 0;
            offset += 4;
        }

        s_ibo = new IndexBuffer(indices);
        s_vao.SetIndexBuffer(s_ibo);

        s_shader = new Shader(VertexShaderSource, FragmentShaderSource);

        ReadOnlySpan<byte> white = stackalloc byte[] { 255, 255, 255, 255 };
        s_whiteTexture = new Texture2D(1, 1, white);
    }

    /// <summary>Releases the shared batch resources. Called once by the application on shutdown.</summary>
    internal static void Shutdown()
    {
        s_shader?.Dispose();
        s_whiteTexture?.Dispose();
        s_vbo?.Dispose();
        s_ibo?.Dispose();
        s_vao?.Dispose();
    }

    /// <summary>The width of the current UI surface in pixels, valid between <see cref="Begin"/> and <see cref="End"/>.</summary>
    public static float ScreenWidth => s_screenWidth;

    /// <summary>The height of the current UI surface in pixels, valid between <see cref="Begin"/> and <see cref="End"/>.</summary>
    public static float ScreenHeight => s_screenHeight;

    /// <summary>
    /// Begins a UI pass over a surface of the given pixel size, mapping (0,0) to the top-left and
    /// (<paramref name="screenWidth"/>, <paramref name="screenHeight"/>) to the bottom-right. Sets the
    /// blend and depth state UI needs; <see cref="End"/> restores it.
    /// </summary>
    /// <param name="screenWidth">The surface width in pixels.</param>
    /// <param name="screenHeight">The surface height in pixels.</param>
    public static void Begin(float screenWidth, float screenHeight)
    {
        s_screenWidth = screenWidth;
        s_screenHeight = screenHeight;
        // Top-left origin, y down: swap top/bottom in the off-center ortho.
        s_projection = Matrix4x4.CreateOrthographicOffCenter(0f, screenWidth, screenHeight, 0f, -1f, 1f);
        s_active = true;
        s_clipStack.Clear();

        Renderer.Device.SetCapability(GraphicsCapability.DepthTest, false);
        Renderer.Device.SetCapability(GraphicsCapability.Blend, true);
        Renderer.Device.SetBlendFunc(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);

        StartBatch();
    }

    /// <summary>Ends the UI pass, drawing anything still batched and restoring the depth/blend/scissor state.</summary>
    public static void End()
    {
        Flush();
        if (s_active)
        {
            Renderer.Device.SetCapability(GraphicsCapability.ScissorTest, false);
            Renderer.Device.SetCapability(GraphicsCapability.Blend, false);
            Renderer.Device.SetCapability(GraphicsCapability.DepthTest, true);
            s_active = false;
        }
    }

    /// <summary>
    /// Restricts subsequent drawing to the intersection of <paramref name="rect"/> and any clip already on
    /// the stack. Must be paired with <see cref="PopClip"/>.
    /// </summary>
    /// <param name="rect">The clip rectangle as <c>(x, y, width, height)</c> in top-left pixel coordinates.</param>
    public static void PushClip(Vector4 rect)
    {
        Vector4 clip = s_clipStack.Count > 0 ? Intersect(s_clipStack[^1], rect) : rect;
        s_clipStack.Add(clip);
        ApplyClip(clip);
    }

    /// <summary>Removes the most recent clip pushed by <see cref="PushClip"/>, restoring the previous one.</summary>
    public static void PopClip()
    {
        if (s_clipStack.Count == 0) return;

        Flush();
        s_clipStack.RemoveAt(s_clipStack.Count - 1);
        if (s_clipStack.Count > 0)
        {
            ApplyClip(s_clipStack[^1]);
        }
        else
        {
            Renderer.Device.SetCapability(GraphicsCapability.ScissorTest, false);
        }
    }

    /// <summary>Draws a solid-colored rectangle.</summary>
    /// <param name="position">The top-left corner in pixels.</param>
    /// <param name="size">The width and height in pixels.</param>
    /// <param name="color">The RGBA color.</param>
    public static void DrawQuad(Vector2 position, Vector2 size, Vector4 color) =>
        DrawQuad(position, size, color, WhiteTexture, new Vector4(0f, 0f, 1f, 1f));

    /// <summary>Draws a textured, tinted rectangle.</summary>
    /// <param name="position">The top-left corner in pixels.</param>
    /// <param name="size">The width and height in pixels.</param>
    /// <param name="tint">The color multiplied with the sampled texture.</param>
    /// <param name="texture">The texture to map onto the rectangle.</param>
    /// <param name="uv">The source rectangle as <c>(u0, v0, u1, v1)</c>. <c>v0</c> maps to the top edge.</param>
    public static void DrawQuad(Vector2 position, Vector2 size, Vector4 tint, Texture2D texture, Vector4 uv)
    {
        if (s_indexCount >= MaxIndices || (s_currentTexture is not null && s_currentTexture != texture))
        {
            Flush();
            StartBatch();
        }

        s_currentTexture = texture;

        // Top-left, top-right, bottom-right, bottom-left — matching the shared index pattern.
        Emit(position.X, position.Y, uv.X, uv.Y, tint);
        Emit(position.X + size.X, position.Y, uv.Z, uv.Y, tint);
        Emit(position.X + size.X, position.Y + size.Y, uv.Z, uv.W, tint);
        Emit(position.X, position.Y + size.Y, uv.X, uv.W, tint);

        s_indexCount += 6;
    }

    /// <summary>
    /// Draws a nine-slice sprite: the four corners keep their size while the edges and center stretch to
    /// fill <paramref name="size"/>. Good for panels and buttons that resize without distorting their border.
    /// </summary>
    /// <param name="position">The top-left corner in pixels.</param>
    /// <param name="size">The overall width and height in pixels.</param>
    /// <param name="tint">The color multiplied with the sampled texture.</param>
    /// <param name="texture">The source sprite.</param>
    /// <param name="border">The inset of the fixed border as <c>(left, top, right, bottom)</c> in texels.</param>
    public static void DrawNineSlice(Vector2 position, Vector2 size, Vector4 tint, Texture2D texture, Vector4 border)
    {
        float tw = texture.Width;
        float th = texture.Height;
        if (tw <= 0f || th <= 0f) return;

        // Destination column x's and row y's; clamp borders so they never exceed the target size.
        float bl = MathF.Min(border.X, size.X * 0.5f);
        float bt = MathF.Min(border.Y, size.Y * 0.5f);
        float br = MathF.Min(border.Z, size.X * 0.5f);
        float bb = MathF.Min(border.W, size.Y * 0.5f);

        Span<float> xs = stackalloc float[4] { position.X, position.X + bl, position.X + size.X - br, position.X + size.X };
        Span<float> ys = stackalloc float[4] { position.Y, position.Y + bt, position.Y + size.Y - bb, position.Y + size.Y };

        // Source columns/rows in 0..1 texture space.
        Span<float> us = stackalloc float[4] { 0f, border.X / tw, 1f - border.Z / tw, 1f };
        Span<float> vs = stackalloc float[4] { 0f, border.Y / th, 1f - border.W / th, 1f };

        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                var cellPos = new Vector2(xs[c], ys[r]);
                var cellSize = new Vector2(xs[c + 1] - xs[c], ys[r + 1] - ys[r]);
                if (cellSize.X <= 0f || cellSize.Y <= 0f) continue;
                var cellUv = new Vector4(us[c], vs[r], us[c + 1], vs[r + 1]);
                DrawQuad(cellPos, cellSize, tint, texture, cellUv);
            }
        }
    }

    /// <summary>Measures the pixel size a call to <see cref="DrawText"/> with the same arguments would occupy.</summary>
    /// <param name="font">The font to measure with.</param>
    /// <param name="text">The string to measure.</param>
    /// <param name="size">The pixel size.</param>
    /// <param name="options">Wrapping, alignment and spacing options.</param>
    public static Vector2 MeasureText(Font font, string text, float size, TextLayoutOptions options) =>
        TextLayout.Measure(font, text, size, options);

    /// <summary>Draws a string with the given font at a pixel size.</summary>
    /// <param name="font">The font to draw with.</param>
    /// <param name="text">The string to draw.</param>
    /// <param name="position">The top-left corner of the text block in pixels.</param>
    /// <param name="size">The pixel size to render at.</param>
    /// <param name="color">The text color.</param>
    /// <param name="options">Wrapping, alignment and spacing options.</param>
    /// <returns>The size of the drawn text block in pixels.</returns>
    public static Vector2 DrawText(Font font, string text, Vector2 position, float size, Vector4 color, TextLayoutOptions options)
    {
        Vector2 blockSize = TextLayout.Build(font, text, size, options, s_glyphScratch);
        foreach (PositionedGlyph glyph in s_glyphScratch)
        {
            DrawQuad(position + glyph.Position, glyph.Size, color, font.Atlas, glyph.Uv);
        }

        return blockSize;
    }

    private static Texture2D WhiteTexture =>
        s_whiteTexture ?? throw new InvalidOperationException("UIRenderer has not been initialized.");

    private static void Emit(float x, float y, float u, float v, Vector4 color)
    {
        s_vertices[s_vertexCursor++] = x;
        s_vertices[s_vertexCursor++] = y;
        s_vertices[s_vertexCursor++] = 0f;
        s_vertices[s_vertexCursor++] = color.X;
        s_vertices[s_vertexCursor++] = color.Y;
        s_vertices[s_vertexCursor++] = color.Z;
        s_vertices[s_vertexCursor++] = color.W;
        s_vertices[s_vertexCursor++] = u;
        s_vertices[s_vertexCursor++] = v;
    }

    private static void StartBatch()
    {
        s_vertexCursor = 0;
        s_indexCount = 0;
        s_currentTexture = null;
    }

    private static void Flush()
    {
        if (s_indexCount == 0 || s_shader is null || s_vbo is null || s_vao is null || s_currentTexture is null)
        {
            return;
        }

        s_vbo.SetData(s_vertices.AsSpan(0, s_vertexCursor));

        s_currentTexture.Bind(0);
        s_shader.Use();
        s_shader.SetUniform("uProjection", s_projection);
        s_shader.SetUniform("uTexture", 0);

        Renderer.DrawIndexed(s_vao, s_indexCount);

        StartBatch();
    }

    private static void ApplyClip(Vector4 clip)
    {
        // Geometry batched under the old clip must be drawn before the scissor changes.
        Flush();

        Renderer.Device.SetCapability(GraphicsCapability.ScissorTest, true);

        int x = (int)MathF.Round(clip.X);
        int w = (int)MathF.Round(clip.Z);
        int h = (int)MathF.Round(clip.W);
        // GL scissor is measured from the bottom-left; our clip is top-left.
        int y = (int)MathF.Round(s_screenHeight - (clip.Y + clip.W));

        Renderer.Device.SetScissor(x, y, (uint)Math.Max(0, w), (uint)Math.Max(0, h));
    }

    private static Vector4 Intersect(Vector4 a, Vector4 b)
    {
        float x0 = MathF.Max(a.X, b.X);
        float y0 = MathF.Max(a.Y, b.Y);
        float x1 = MathF.Min(a.X + a.Z, b.X + b.Z);
        float y1 = MathF.Min(a.Y + a.W, b.Y + b.W);
        return new Vector4(x0, y0, MathF.Max(0f, x1 - x0), MathF.Max(0f, y1 - y0));
    }
}

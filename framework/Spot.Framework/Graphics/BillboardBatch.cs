using System.Numerics;

namespace Spot.Framework.Graphics;

/// <summary>
/// How a <see cref="BillboardBatch"/> quad combines with what is already drawn.
/// </summary>
public enum BlendMode
{
    /// <summary>Standard alpha blending.</summary>
    Alpha,

    /// <summary>Additive blending (glow, fire, sparks).</summary>
    Additive,
}

/// <summary>
/// A batch of blended, camera-facing (or arbitrarily oriented) textured quads in 3D — the primitive particles,
/// world-space text and other transparent sprites are built on. Quads are tested against the depth buffer but
/// don't write to it, so they blend through each other while solid geometry still hides them.
/// </summary>
/// <remarks>
/// Draw between <see cref="Begin"/> and <see cref="End"/>. Quads batch while they share a texture and a blend mode;
/// <see cref="End"/> restores depth writes and turns blending off. Sort back-to-front yourself when alpha-blended
/// quads overlap.
/// </remarks>
public static class BillboardBatch
{
    private const int MaxQuads = 20_000;
    private const int MaxVertices = MaxQuads * 4;
    private const int MaxIndices = MaxQuads * 6;
    private const int FloatsPerVertex = 3 + 4 + 2; // position, color, texture coordinate

    private const string VertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec4 aColor;
        layout (location = 2) in vec2 aTexCoord;

        uniform mat4 uViewProjection;

        out vec4 vColor;
        out vec2 vTexCoord;

        void main()
        {
            vColor = aColor;
            vTexCoord = aTexCoord;
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
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
            vec4 c = texture(uTexture, vTexCoord) * vColor;
            if (c.a <= 0.0) discard;
            fragColor = c;
        }
        """;

    private static readonly Vector4 FullUv = new(0.0f, 0.0f, 1.0f, 1.0f);

    private static IGraphicsDevice? s_device;
    private static VertexArray? s_vao;
    private static VertexBuffer? s_vbo;
    private static IndexBuffer? s_ibo;
    private static Shader? s_shader;
    private static Texture2D? s_softDot;

    private static float[] s_vertices = Array.Empty<float>();
    private static int s_vertexCursor;
    private static uint s_indexCount;
    private static Texture2D? s_currentTexture;
    private static BlendMode s_currentBlend;
    private static Matrix4x4 s_viewProjection = Matrix4x4.Identity;
    private static bool s_active;

    /// <summary>
    /// Gets a soft round dot (white, with a smooth radial falloff): the texture quads use when none is given, so
    /// untextured particles look round instead of square.
    /// </summary>
    public static Texture2D SoftDotTexture
    {
        get
        {
            EnsureInitialized();
            return s_softDot!;
        }
    }

    /// <summary>
    /// Creates the batch resources on the current <see cref="Renderer.Device"/>. Happens automatically on first use.
    /// </summary>
    public static void Init()
    {
        s_device = Renderer.Device;
        s_vertices = new float[MaxVertices * FloatsPerVertex];

        s_vao = new VertexArray();
        s_vbo = new VertexBuffer((uint)s_vertices.Length, ShaderDataType.Float3, ShaderDataType.Float4, ShaderDataType.Float2);
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
        s_softDot = CreateSoftDotTexture(64);
    }

    /// <summary>Releases the batch resources. The next use recreates them.</summary>
    public static void Shutdown()
    {
        s_shader?.Dispose();
        s_softDot?.Dispose();
        s_vbo?.Dispose();
        s_ibo?.Dispose();
        s_vao?.Dispose();
        s_shader = null;
        s_softDot = null;
        s_vbo = null;
        s_ibo = null;
        s_vao = null;
        s_device = null;
        s_vertices = Array.Empty<float>();
    }

    /// <summary>
    /// Begins a batch drawn with the given view-projection.
    /// </summary>
    /// <param name="viewProjection">The camera's view-projection matrix.</param>
    public static void Begin(Matrix4x4 viewProjection)
    {
        EnsureInitialized();
        s_viewProjection = viewProjection;
        s_active = true;
        StartBatch();
    }

    /// <summary>
    /// Draws a quad spanning <c>center ± axisX ± axisY</c> — pass the camera's right and up vectors (scaled by half
    /// the size) for a camera-facing billboard, or any two axes for a fixed orientation.
    /// </summary>
    /// <param name="center">The quad's center.</param>
    /// <param name="axisX">Half the quad's width, as a direction.</param>
    /// <param name="axisY">Half the quad's height, as a direction.</param>
    /// <param name="color">The color, multiplied with the texture.</param>
    /// <param name="texture">The texture; <see langword="null"/> uses <see cref="SoftDotTexture"/>.</param>
    /// <param name="blend">How the quad blends.</param>
    /// <param name="uv">The texture rectangle as <c>(u0, v0, u1, v1)</c>, <c>v0</c> on the <c>+axisY</c> edge;
    /// <see langword="null"/> maps the whole texture with <c>v</c> increasing along <c>+axisY</c>.</param>
    public static void Draw(Vector3 center, Vector3 axisX, Vector3 axisY, Vector4 color, Texture2D? texture = null,
        BlendMode blend = BlendMode.Alpha, Vector4? uv = null)
    {
        EnsureInitialized();
        Texture2D tex = texture ?? s_softDot!;

        bool batchBreak = s_indexCount >= MaxIndices
            || (s_currentTexture is not null && s_currentTexture != tex)
            || (s_indexCount > 0 && blend != s_currentBlend);
        if (batchBreak)
        {
            Flush();
            StartBatch();
        }

        s_currentTexture = tex;
        s_currentBlend = blend;

        // A whole-texture quad maps v up the +axisY edge; an explicit rect puts its v0 (an atlas top) on +axisY.
        Vector4 r = uv ?? FullUv;
        float vBottom = uv is null ? r.Y : r.W;
        float vTop = uv is null ? r.W : r.Y;
        Emit(center - axisX - axisY, color, r.X, vBottom);
        Emit(center + axisX - axisY, color, r.Z, vBottom);
        Emit(center + axisX + axisY, color, r.Z, vTop);
        Emit(center - axisX + axisY, color, r.X, vTop);
        s_indexCount += 6;
    }

    /// <summary>
    /// Draws a camera-facing quad of the given size.
    /// </summary>
    /// <param name="center">The quad's center.</param>
    /// <param name="size">The quad's width and height.</param>
    /// <param name="cameraRight">The camera's right vector (see <see cref="CameraAxes"/>).</param>
    /// <param name="cameraUp">The camera's up vector.</param>
    /// <param name="color">The color, multiplied with the texture.</param>
    /// <param name="texture">The texture; <see langword="null"/> uses <see cref="SoftDotTexture"/>.</param>
    /// <param name="blend">How the quad blends.</param>
    public static void DrawFacing(Vector3 center, Vector2 size, Vector3 cameraRight, Vector3 cameraUp, Vector4 color,
        Texture2D? texture = null, BlendMode blend = BlendMode.Alpha) =>
        Draw(center, cameraRight * (size.X * 0.5f), cameraUp * (size.Y * 0.5f), color, texture, blend);

    /// <summary>
    /// Extracts the camera's right and up vectors (world space) from a view matrix, for camera-facing quads.
    /// </summary>
    /// <param name="view">The view matrix.</param>
    /// <param name="right">Receives the camera's right vector.</param>
    /// <param name="up">Receives the camera's up vector.</param>
    public static void CameraAxes(Matrix4x4 view, out Vector3 right, out Vector3 up)
    {
        right = Vector3.Normalize(new Vector3(view.M11, view.M21, view.M31));
        up = Vector3.Normalize(new Vector3(view.M12, view.M22, view.M32));
    }

    /// <summary>Draws what is batched and restores depth writes and opaque (non-blended) drawing.</summary>
    public static void End()
    {
        Flush();
        StartBatch();
        if (s_active)
        {
            Renderer.SetDepthWrite(true);
            Renderer.Device.SetCapability(GraphicsCapability.Blend, false);
            s_active = false;
        }
    }

    private static void EnsureInitialized()
    {
        if (s_vao is null || !ReferenceEquals(s_device, Renderer.Device))
        {
            Init();
        }
    }

    private static void Emit(Vector3 position, Vector4 color, float u, float v)
    {
        s_vertices[s_vertexCursor++] = position.X;
        s_vertices[s_vertexCursor++] = position.Y;
        s_vertices[s_vertexCursor++] = position.Z;
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

        Renderer.Device.SetCapability(GraphicsCapability.Blend, true);
        Renderer.Device.SetBlendFunc(
            BlendFactor.SrcAlpha,
            s_currentBlend == BlendMode.Additive ? BlendFactor.One : BlendFactor.OneMinusSrcAlpha);

        // Transparent quads must not write depth, or nearer ones would cull farther ones that should blend
        // through. The depth *test* stays as-is so solid geometry still occludes them.
        Renderer.SetDepthWrite(false);

        s_vbo.SetData(s_vertices.AsSpan(0, s_vertexCursor));
        s_currentTexture.Bind(0);
        s_shader.Use();
        s_shader.SetUniform("uViewProjection", s_viewProjection);
        s_shader.SetUniform("uTexture", 0);
        Renderer.DrawIndexed(s_vao, s_indexCount);
    }

    // A soft round dot: white with a smooth radial alpha falloff.
    private static Texture2D CreateSoftDotTexture(int size)
    {
        var pixels = new byte[size * size * 4];
        float center = (size - 1) * 0.5f;
        float radius = center;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / radius;
                float dy = (y - center) / radius;
                float d = MathF.Sqrt(dx * dx + dy * dy);

                // smoothstep(1, 0, d): opaque at the center, fading to nothing at the edge.
                float t = Math.Clamp(1.0f - d, 0.0f, 1.0f);
                float alpha = t * t * (3.0f - 2.0f * t);

                int i = (y * size + x) * 4;
                pixels[i + 0] = 255;
                pixels[i + 1] = 255;
                pixels[i + 2] = 255;
                pixels[i + 3] = (byte)(alpha * 255.0f);
            }
        }

        return new Texture2D((uint)size, (uint)size, pixels);
    }
}

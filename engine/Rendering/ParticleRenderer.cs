using System.Numerics;
using Silk.NET.OpenGL;

namespace Spot.Rendering;

/// <summary>How particle pixels combine with what's already on screen.</summary>
public enum ParticleBlend
{
    /// <summary>Standard transparency: <c>src.a</c> over the background. Good for smoke, dust, soft sprites.</summary>
    Alpha,

    /// <summary>Additive: pixels only ever brighten the background. Good for fire, sparks, magic, glow.</summary>
    Additive,
}

/// <summary>
/// A batched renderer for particle quads. Like <see cref="Renderer2D"/> it accumulates quads into one
/// dynamic buffer and draws them together, flushing whenever the texture or blend mode changes. Unlike
/// <see cref="Renderer2D"/>, each quad is submitted as an explicit center plus two half-axis vectors, so
/// the caller controls orientation — camera-facing billboards or flat 2D quads alike. It writes no depth
/// (so overlapping transparent particles don't fight the depth buffer) but honors the depth test that is
/// already bound, so solid geometry still occludes particles in a 3D scene.
/// </summary>
public static class ParticleRenderer
{
    private const int MaxQuads = 20_000;
    private const int MaxVertices = MaxQuads * 4;
    private const int MaxIndices = MaxQuads * 6;
    private const int FloatsPerVertex = 3 + 4 + 2; // position, color, texture coordinate

    private static readonly Vector2[] QuadTexCoords =
    {
        new Vector2(0.0f, 0.0f),
        new Vector2(1.0f, 0.0f),
        new Vector2(1.0f, 1.0f),
        new Vector2(0.0f, 1.0f),
    };

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

    private static VertexArray? s_vao;
    private static VertexBuffer? s_vbo;
    private static IndexBuffer? s_ibo;
    private static Shader? s_shader;
    private static Texture2D? s_defaultTexture;

    private static float[] s_vertices = Array.Empty<float>();
    private static int s_vertexCursor;
    private static uint s_indexCount;
    private static Texture2D? s_currentTexture;
    private static ParticleBlend s_currentBlend;
    private static Matrix4x4 s_viewProjection = Matrix4x4.Identity;
    private static bool s_active;

    /// <summary>
    /// Gets the built-in soft round particle texture, used when an emitter has no texture of its own.
    /// </summary>
    public static Texture2D DefaultTexture =>
        s_defaultTexture ?? throw new InvalidOperationException("ParticleRenderer has not been initialized.");

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
        s_defaultTexture = CreateSoftDotTexture(64);
    }

    /// <summary>Releases the shared batch resources. Called once by the application on shutdown.</summary>
    internal static void Shutdown()
    {
        s_shader?.Dispose();
        s_defaultTexture?.Dispose();
        s_vbo?.Dispose();
        s_ibo?.Dispose();
        s_vao?.Dispose();
    }

    /// <summary>Begins a batch of particles rendered with the given view-projection matrix.</summary>
    /// <param name="viewProjection">The view-projection matrix to use for this batch.</param>
    public static void BeginScene(Matrix4x4 viewProjection)
    {
        s_viewProjection = viewProjection;
        s_active = true;
        StartBatch();
    }

    /// <summary>
    /// Submits one particle quad. The quad is centered at <paramref name="center"/> and spans
    /// <c>center ± axisX ± axisY</c>, so <paramref name="axisX"/> and <paramref name="axisY"/> are the
    /// (already rotated and half-sized) edge vectors — pass camera right/up for billboards, or world
    /// X/Y for flat 2D quads.
    /// </summary>
    /// <param name="center">The quad center, in world space.</param>
    /// <param name="axisX">Half-width edge vector.</param>
    /// <param name="axisY">Half-height edge vector.</param>
    /// <param name="color">The RGBA tint multiplied with the texture.</param>
    /// <param name="texture">The texture to map onto the quad. When null, the built-in soft dot is used.</param>
    /// <param name="blend">How the quad's pixels combine with the scene.</param>
    public static void Submit(Vector3 center, Vector3 axisX, Vector3 axisY, Vector4 color, Texture2D? texture, ParticleBlend blend)
    {
        Texture2D tex = texture ?? DefaultTexture;

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

        Span<Vector3> corners = stackalloc Vector3[4]
        {
            center - axisX - axisY,
            center + axisX - axisY,
            center + axisX + axisY,
            center - axisX + axisY,
        };

        for (int i = 0; i < 4; i++)
        {
            Vector3 position = corners[i];
            Vector2 uv = QuadTexCoords[i];

            s_vertices[s_vertexCursor++] = position.X;
            s_vertices[s_vertexCursor++] = position.Y;
            s_vertices[s_vertexCursor++] = position.Z;
            s_vertices[s_vertexCursor++] = color.X;
            s_vertices[s_vertexCursor++] = color.Y;
            s_vertices[s_vertexCursor++] = color.Z;
            s_vertices[s_vertexCursor++] = color.W;
            s_vertices[s_vertexCursor++] = uv.X;
            s_vertices[s_vertexCursor++] = uv.Y;
        }

        s_indexCount += 6;
    }

    /// <summary>
    /// Submits one textured quad with an explicit atlas sub-rectangle — the world-text path. Like
    /// <see cref="Submit"/> the quad is centered at <paramref name="center"/> spanning <c>center ± axisX ± axisY</c>,
    /// but it samples <paramref name="uv"/> (as <c>(u0, v0, u1, v1)</c>) instead of the whole texture, so a
    /// single glyph atlas can supply every glyph. UVs are mapped so <c>v0</c> (the atlas top) lands on the
    /// <c>+axisY</c> edge, matching text laid out with y pointing down.
    /// </summary>
    /// <param name="center">The quad center, in world space.</param>
    /// <param name="axisX">Half-width edge vector.</param>
    /// <param name="axisY">Half-height edge vector (points to the glyph's top).</param>
    /// <param name="color">The RGBA tint multiplied with the texture.</param>
    /// <param name="texture">The texture to sample (a glyph atlas).</param>
    /// <param name="blend">How the quad's pixels combine with the scene.</param>
    /// <param name="uv">The atlas sub-rectangle as <c>(u0, v0, u1, v1)</c> in 0..1 texture space.</param>
    public static void SubmitTextured(Vector3 center, Vector3 axisX, Vector3 axisY, Vector4 color, Texture2D texture, ParticleBlend blend, Vector4 uv)
    {
        bool batchBreak = s_indexCount >= MaxIndices
            || (s_currentTexture is not null && s_currentTexture != texture)
            || (s_indexCount > 0 && blend != s_currentBlend);
        if (batchBreak)
        {
            Flush();
            StartBatch();
        }

        s_currentTexture = texture;
        s_currentBlend = blend;

        // Corners paired with UVs so the atlas top (uv.Y) sits on the +axisY corners (glyph top).
        Span<Vector3> corners = stackalloc Vector3[4]
        {
            center - axisX - axisY,
            center + axisX - axisY,
            center + axisX + axisY,
            center - axisX + axisY,
        };
        Span<Vector2> uvs = stackalloc Vector2[4]
        {
            new Vector2(uv.X, uv.W),
            new Vector2(uv.Z, uv.W),
            new Vector2(uv.Z, uv.Y),
            new Vector2(uv.X, uv.Y),
        };

        for (int i = 0; i < 4; i++)
        {
            Vector3 position = corners[i];
            Vector2 texCoord = uvs[i];

            s_vertices[s_vertexCursor++] = position.X;
            s_vertices[s_vertexCursor++] = position.Y;
            s_vertices[s_vertexCursor++] = position.Z;
            s_vertices[s_vertexCursor++] = color.X;
            s_vertices[s_vertexCursor++] = color.Y;
            s_vertices[s_vertexCursor++] = color.Z;
            s_vertices[s_vertexCursor++] = color.W;
            s_vertices[s_vertexCursor++] = texCoord.X;
            s_vertices[s_vertexCursor++] = texCoord.Y;
        }

        s_indexCount += 6;
    }

    /// <summary>Ends the current batch, drawing any particles submitted since <see cref="BeginScene"/>.</summary>
    public static void EndScene()
    {
        Flush();

        // Restore the state Flush changed, so the rest of the frame draws normally: depth writes back on,
        // blending off. The depth test is left exactly as the caller had it.
        if (s_active)
        {
            Renderer.Api.DepthMask(true);
            Renderer.Api.Disable(EnableCap.Blend);
            s_active = false;
        }
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

        Renderer.Api.Enable(EnableCap.Blend);
        Renderer.Api.BlendFunc(
            BlendingFactor.SrcAlpha,
            s_currentBlend == ParticleBlend.Additive ? BlendingFactor.One : BlendingFactor.OneMinusSrcAlpha);

        // Transparent particles must not write depth, or nearer particles would cull farther ones that
        // should blend through. The depth *test* stays as-is so solid geometry still occludes them.
        Renderer.Api.DepthMask(false);

        s_vbo.SetData(s_vertices.AsSpan(0, s_vertexCursor));

        s_currentTexture.Bind(0);
        s_shader.Use();
        s_shader.SetUniform("uViewProjection", s_viewProjection);
        s_shader.SetUniform("uTexture", 0);

        Renderer.DrawIndexed(s_vao, s_indexCount);
    }

    // Builds a soft round dot: white with a smooth radial alpha falloff, so an emitter with no texture of
    // its own still produces good-looking round particles instead of hard squares.
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

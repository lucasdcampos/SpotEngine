using System.Numerics;

namespace Spot.Rendering;

/// <summary>
/// A batched 2D renderer. Quads submitted between <see cref="BeginScene"/> and <see cref="EndScene"/>
/// are accumulated into a single buffer and drawn together, so many sprites cost few draw calls.
/// </summary>
/// <remarks>
/// This is the mid-level renderer: it consumes high-level draw requests and delegates the actual
/// draw calls to <see cref="Renderer"/>. Quads are batched while they share a texture; changing
/// texture (or filling the batch) triggers a flush. Colored quads use a built-in 1x1 white texture,
/// so they batch together too.
/// </remarks>
public static class Renderer2D
{
    private const int MaxQuads = 10_000;
    private const int MaxVertices = MaxQuads * 4;
    private const int MaxIndices = MaxQuads * 6;
    private const int FloatsPerVertex = 3 + 4 + 2; // position, color, texture coordinate

    private static readonly Vector4[] QuadPositions =
    {
        new Vector4(-0.5f, -0.5f, 0.0f, 1.0f),
        new Vector4(0.5f, -0.5f, 0.0f, 1.0f),
        new Vector4(0.5f, 0.5f, 0.0f, 1.0f),
        new Vector4(-0.5f, 0.5f, 0.0f, 1.0f),
    };

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
            fragColor = texture(uTexture, vTexCoord) * vColor;
        }
        """;

    private const string GridVertexShaderSource =
        """
        #version 330 core
        
        out vec2 vWorldPos;

        uniform mat4 uInverseViewProjection;

        void main() 
        {
            float x = -1.0 + float((gl_VertexID & 1) << 2);
            float y = -1.0 + float((gl_VertexID & 2) << 1);
            gl_Position = vec4(x, y, 0.0, 1.0);
            
            vec4 unprojectedPoint = uInverseViewProjection * vec4(x, y, 0.0, 1.0);
            vWorldPos = unprojectedPoint.xy / unprojectedPoint.w;
        }
        """;

    private const string GridFragmentShaderSource =
        """
        #version 330 core
        
        in vec2 vWorldPos;
        out vec4 fragColor;

        uniform float uZoom;

        vec4 grid(vec2 fragPos2D, float scale) {
            vec2 coord = fragPos2D * scale;
            vec2 derivative = max(fwidth(coord), vec2(1e-5));
            vec2 grid = abs(fract(coord - 0.5) - 0.5) / derivative;
            float line = min(grid.x, grid.y);
            vec4 color = vec4(0.3, 0.3, 0.3, 1.0 - min(line, 1.0));
            return color;
        }

        void main() {
            float logZoom = log(max(uZoom * 0.2, 0.001)) / log(10.0);
            float lod = floor(logZoom);
            float lodFade = fract(logZoom);
            
            float scale0 = 1.0 / pow(10.0, lod);
            float scale1 = 1.0 / pow(10.0, lod + 1.0);
            float scale2 = 1.0 / pow(10.0, lod + 2.0);
            
            vec4 grid0 = grid(vWorldPos, scale0);
            vec4 grid1 = grid(vWorldPos, scale1);
            vec4 grid2 = grid(vWorldPos, scale2);
            
            grid0.a *= (1.0 - lodFade);
            
            vec4 c = grid0;
            c = mix(c, grid1, grid1.a);
            c = mix(c, grid2, grid2.a);

            fragColor = c;
            if (fragColor.a <= 0.0) discard;
        }
        """;

    private static VertexArray? s_vao;
    private static VertexBuffer? s_vbo;
    private static IndexBuffer? s_ibo;
    private static Shader? s_shader;
    private static Texture2D? s_whiteTexture;
    private static Shader? s_gridShader;
    private static VertexArray? s_emptyVao;

    private static float[] s_vertices = Array.Empty<float>();
    private static int s_vertexCursor;
    private static uint s_indexCount;
    private static Texture2D? s_currentTexture;
    private static Matrix4x4 s_viewProjection = Matrix4x4.Identity;

    /// <summary>
    /// Creates the shared batch resources. Called once by the application after the renderer is ready.
    /// </summary>
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
        s_gridShader = new Shader(GridVertexShaderSource, GridFragmentShaderSource);
        s_emptyVao = new VertexArray();

        // A 1x1 white texture lets colored quads reuse the textured path: texture * color == color.
        ReadOnlySpan<byte> white = stackalloc byte[] { 255, 255, 255, 255 };
        s_whiteTexture = new Texture2D(1, 1, white);
    }

    /// <summary>
    /// Releases the shared batch resources. Called once by the application on shutdown.
    /// </summary>
    internal static void Shutdown()
    {
        s_shader?.Dispose();
        s_gridShader?.Dispose();
        s_whiteTexture?.Dispose();
        s_vbo?.Dispose();
        s_ibo?.Dispose();
        s_vao?.Dispose();
        s_emptyVao?.Dispose();
    }

    /// <summary>
    /// Begins a batch of 2D geometry rendered with the given view-projection matrix.
    /// </summary>
    /// <param name="viewProjection">The view-projection matrix to use for this batch.</param>
    public static void BeginScene(Matrix4x4 viewProjection)
    {
        s_viewProjection = viewProjection;
        StartBatch();
    }

    /// <summary>
    /// Draws an infinite 2D grid plane for the editor.
    /// </summary>
    public static void DrawEditorGrid(float zoom)
    {
        Flush(); // Ensure previous geometry is drawn

        if (s_gridShader == null || s_emptyVao == null) return;

        Matrix4x4.Invert(s_viewProjection, out Matrix4x4 invViewProj);
        s_gridShader.Use();
        s_gridShader.SetUniform("uInverseViewProjection", invViewProj);
        s_gridShader.SetUniform("uZoom", zoom);

        Renderer.Api.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        Renderer.Api.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);

        Renderer.DrawArrays(s_emptyVao, 3);
    }

    /// <summary>
    /// Ends the current batch, drawing any quads submitted since <see cref="BeginScene"/>.
    /// </summary>
    public static void EndScene() => Flush();

    /// <summary>
    /// Draws a solid-colored quad at the given position and size.
    /// </summary>
    /// <param name="position">The center of the quad, in world units.</param>
    /// <param name="size">The width and height of the quad, in world units.</param>
    /// <param name="color">The RGBA color.</param>
    public static void DrawQuad(Vector2 position, Vector2 size, Vector4 color) =>
        DrawQuad(TransformFor(position, size), color);

    /// <summary>
    /// Draws a textured quad at the given position and size.
    /// </summary>
    /// <param name="position">The center of the quad, in world units.</param>
    /// <param name="size">The width and height of the quad, in world units.</param>
    /// <param name="texture">The texture to map onto the quad.</param>
    public static void DrawQuad(Vector2 position, Vector2 size, Texture2D texture) =>
        DrawQuad(TransformFor(position, size), texture);

    /// <summary>
    /// Draws a solid-colored quad with the given transform.
    /// </summary>
    /// <param name="transform">The model matrix applied to a unit quad.</param>
    /// <param name="color">The RGBA color.</param>
    public static void DrawQuad(Matrix4x4 transform, Vector4 color) => DrawQuad(transform, WhiteTexture, color);

    /// <summary>
    /// Draws a textured quad with the given transform.
    /// </summary>
    /// <param name="transform">The model matrix applied to a unit quad.</param>
    /// <param name="texture">The texture to map onto the quad.</param>
    public static void DrawQuad(Matrix4x4 transform, Texture2D texture) => DrawQuad(transform, texture, Vector4.One);

    /// <summary>
    /// Draws a textured quad with the given transform and color tint.
    /// </summary>
    /// <param name="transform">The model matrix applied to a unit quad.</param>
    /// <param name="texture">The texture to map onto the quad.</param>
    /// <param name="tint">The color multiplied with the sampled texture.</param>
    public static void DrawQuad(Matrix4x4 transform, Texture2D texture, Vector4 tint)
    {
        if (s_indexCount >= MaxIndices || (s_currentTexture is not null && s_currentTexture != texture))
        {
            Flush();
            StartBatch();
        }

        s_currentTexture = texture;

        for (int i = 0; i < 4; i++)
        {
            Vector4 position = Vector4.Transform(QuadPositions[i], transform);
            Vector2 uv = QuadTexCoords[i];

            s_vertices[s_vertexCursor++] = position.X;
            s_vertices[s_vertexCursor++] = position.Y;
            s_vertices[s_vertexCursor++] = position.Z;
            s_vertices[s_vertexCursor++] = tint.X;
            s_vertices[s_vertexCursor++] = tint.Y;
            s_vertices[s_vertexCursor++] = tint.Z;
            s_vertices[s_vertexCursor++] = tint.W;
            s_vertices[s_vertexCursor++] = uv.X;
            s_vertices[s_vertexCursor++] = uv.Y;
        }

        s_indexCount += 6;
    }

    /// <summary>
    /// Draws a hollow rectangle (wireframe) at the given position and size using 4 thin quads.
    /// </summary>
    /// <param name="position">The center of the rectangle, in world units.</param>
    /// <param name="size">The width and height of the rectangle, in world units.</param>
    /// <param name="color">The RGBA color.</param>
    /// <param name="thickness">The thickness of the lines.</param>
    public static void DrawRect(Vector2 position, Vector2 size, Vector4 color, float thickness = 0.05f)
    {
        // Top
        DrawQuad(position + new Vector2(0, size.Y / 2), new Vector2(size.X + thickness, thickness), color);
        // Bottom
        DrawQuad(position - new Vector2(0, size.Y / 2), new Vector2(size.X + thickness, thickness), color);
        // Left
        DrawQuad(position - new Vector2(size.X / 2, 0), new Vector2(thickness, size.Y + thickness), color);
        // Right
        DrawQuad(position + new Vector2(size.X / 2, 0), new Vector2(thickness, size.Y + thickness), color);
    }

    /// <summary>
    /// Draws a line between two points in 3D space using a stretched quad.
    /// </summary>
    public static void DrawLine(Vector3 p0, Vector3 p1, Vector4 color, float thickness = 0.05f)
    {
        Vector3 dir = p1 - p0;
        float length = dir.Length();
        if (length == 0.0f) return;
        dir /= length;

        Vector3 center = (p0 + p1) * 0.5f;

        Vector3 right = Vector3.UnitX;
        float dot = Vector3.Dot(right, dir);
        
        Matrix4x4 rotation;
        if (dot > 0.9999f)
            rotation = Matrix4x4.Identity;
        else if (dot < -0.9999f)
            rotation = Matrix4x4.CreateRotationZ(MathF.PI);
        else
        {
            Vector3 axis = Vector3.Normalize(Vector3.Cross(right, dir));
            float angle = MathF.Acos(dot);
            rotation = Matrix4x4.CreateFromAxisAngle(axis, angle);
        }

        Matrix4x4 transform1 = Matrix4x4.CreateScale(length, thickness, thickness) * rotation * Matrix4x4.CreateTranslation(center);
        DrawQuad(transform1, color);

        Matrix4x4 transform2 = Matrix4x4.CreateScale(length, thickness, thickness) * Matrix4x4.CreateRotationX(MathF.PI / 2.0f) * rotation * Matrix4x4.CreateTranslation(center);
        DrawQuad(transform2, color);
    }

    private static Texture2D WhiteTexture =>
        s_whiteTexture ?? throw new InvalidOperationException("Renderer2D has not been initialized.");

    private static Matrix4x4 TransformFor(Vector2 position, Vector2 size) =>
        Matrix4x4.CreateScale(size.X, size.Y, 1.0f)
        * Matrix4x4.CreateTranslation(position.X, position.Y, 0.0f);

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
        s_shader.SetUniform("uViewProjection", s_viewProjection);
        s_shader.SetUniform("uTexture", 0);

        Renderer.DrawIndexed(s_vao, s_indexCount);
    }
}


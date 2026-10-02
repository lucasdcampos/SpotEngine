using System.Numerics;
using System.Runtime.InteropServices;

namespace Spot.Framework.Graphics;

/// <summary>
/// How <see cref="BasicRenderer3D"/> draws a mesh: a color, an optional texture, whether it is lit, and optionally
/// your own shader.
/// </summary>
public sealed class BasicMaterial
{
    /// <summary>Gets or sets the color, multiplied with the texture. Defaults to white.</summary>
    public Vector4 Color { get; set; } = Vector4.One;

    /// <summary>Gets or sets the texture, or <see langword="null"/> for a solid color.</summary>
    public Texture2D? Texture { get; set; }

    /// <summary>Gets or sets whether the directional light and ambient term shade the surface. Defaults to on.</summary>
    public bool Lit { get; set; } = true;

    /// <summary>
    /// Gets or sets a custom shader to draw with instead of the built-in one. It receives the same inputs: vertex
    /// attributes at locations 0 (position), 1 (normal), 2 (uv) — plus 3-6 (model matrix) and 7 (color) when
    /// instanced, or 3 (bone indices) and 4 (bone weights) when skinned — and the uniforms <c>uViewProjection</c>,
    /// <c>uModel</c>, <c>uColor</c>, <c>uTexture</c>, <c>uLit</c>, <c>uLightDirection</c>, <c>uLightColor</c>,
    /// <c>uAmbient</c> and, when skinned, <c>uBones</c>.
    /// </summary>
    public Shader? Shader { get; set; }
}

/// <summary>
/// A small immediate-mode 3D renderer: meshes and models with a single directional light and an ambient term,
/// unlit or textured, instanced or skinned, or through your own shader. Deliberately basic — the engine's lit
/// renderer adds shadows, many lights and post-processing on top of the same <see cref="Mesh"/> and
/// <see cref="Model"/> types.
/// </summary>
/// <remarks>
/// Draw between <see cref="BeginScene(Matrix4x4, Vector3?, Vector3?, float)"/> and <see cref="EndScene"/>. The scene
/// enables depth testing and back-face culling and <see cref="EndScene"/> turns both off again, so 2D drawn
/// afterwards is unaffected.
/// </remarks>
public static class BasicRenderer3D
{
    /// <summary>The most bones a skinned draw can use; larger palettes are truncated.</summary>
    public const int MaxBones = 128;

    // Instances uploaded per draw before the buffer is refilled; fixed so the buffer's handle stays stable for the
    // per-mesh instanced vertex arrays that record it.
    private const int MaxInstancesPerBatch = 512;
    private const int InstanceFloats = 20; // mat4 model + vec4 color

    private const string FragmentSource =
        """
        #version 330 core
        in vec3 vNormal;
        in vec2 vTexCoord;
        in vec4 vColor;

        uniform sampler2D uTexture;
        uniform int uLit;
        uniform vec3 uLightDirection;
        uniform vec3 uLightColor;
        uniform float uAmbient;

        out vec4 fragColor;

        void main()
        {
            vec4 color = texture(uTexture, vTexCoord) * vColor;
            if (uLit == 1)
            {
                vec3 n = normalize(vNormal);
                float diffuse = max(dot(n, -uLightDirection), 0.0);
                color.rgb *= uLightColor * diffuse + vec3(uAmbient);
            }

            fragColor = color;
        }
        """;

    private const string RigidVertexSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;

        uniform mat4 uViewProjection;
        uniform mat4 uModel;
        uniform vec4 uColor;

        out vec3 vNormal;
        out vec2 vTexCoord;
        out vec4 vColor;

        void main()
        {
            vNormal = mat3(transpose(inverse(uModel))) * aNormal;
            vTexCoord = aTexCoord;
            vColor = uColor;
            gl_Position = uViewProjection * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string InstancedVertexSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;
        layout (location = 3) in mat4 iModel;
        layout (location = 7) in vec4 iColor;

        uniform mat4 uViewProjection;

        out vec3 vNormal;
        out vec2 vTexCoord;
        out vec4 vColor;

        void main()
        {
            vNormal = mat3(transpose(inverse(iModel))) * aNormal;
            vTexCoord = aTexCoord;
            vColor = iColor;
            gl_Position = uViewProjection * iModel * vec4(aPosition, 1.0);
        }
        """;

    private const string SkinnedVertexSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;
        layout (location = 3) in vec4 aBoneIndices;
        layout (location = 4) in vec4 aBoneWeights;

        const int MAX_BONES = 128;
        uniform mat4 uBones[MAX_BONES];
        uniform mat4 uViewProjection;
        uniform vec4 uColor;

        out vec3 vNormal;
        out vec2 vTexCoord;
        out vec4 vColor;

        void main()
        {
            // Each bone matrix already maps to world space (InverseBind * boneGlobal * world).
            mat4 skin =
                uBones[int(aBoneIndices.x)] * aBoneWeights.x +
                uBones[int(aBoneIndices.y)] * aBoneWeights.y +
                uBones[int(aBoneIndices.z)] * aBoneWeights.z +
                uBones[int(aBoneIndices.w)] * aBoneWeights.w;
            float total = aBoneWeights.x + aBoneWeights.y + aBoneWeights.z + aBoneWeights.w;
            skin = total > 0.0001 ? skin * (1.0 / total) : mat4(1.0);

            vNormal = mat3(skin) * aNormal;
            vTexCoord = aTexCoord;
            vColor = uColor;
            gl_Position = uViewProjection * skin * vec4(aPosition, 1.0);
        }
        """;

    private static readonly BasicMaterial s_defaultMaterial = new();

    private static IGraphicsDevice? s_device;
    private static Shader? s_rigid;
    private static Shader? s_instanced;
    private static Shader? s_skinned;
    private static VertexBuffer? s_instanceBuffer;
    private static float[] s_instanceData = Array.Empty<float>();

    private static Matrix4x4 s_viewProjection = Matrix4x4.Identity;
    private static Vector3 s_lightDirection = new(0.0f, -1.0f, 0.0f);
    private static Vector3 s_lightColor = Vector3.One;
    private static float s_ambient = 0.25f;

    /// <summary>Gets the view-projection of the current scene.</summary>
    public static Matrix4x4 ViewProjection => s_viewProjection;

    /// <summary>Gets the normalized direction the light travels in the current scene.</summary>
    public static Vector3 LightDirection => s_lightDirection;

    /// <summary>
    /// Begins a scene: sets the camera and the light, and enables depth testing and back-face culling.
    /// </summary>
    /// <param name="viewProjection">The camera's view-projection matrix.</param>
    /// <param name="lightDirection">The direction the light travels (normalized for you). Defaults to straight down
    /// and slightly forward.</param>
    /// <param name="lightColor">The light's color and intensity. Defaults to white.</param>
    /// <param name="ambient">The ambient light added to every lit surface, 0..1.</param>
    public static void BeginScene(Matrix4x4 viewProjection, Vector3? lightDirection = null, Vector3? lightColor = null,
        float ambient = 0.25f)
    {
        EnsureInitialized();
        s_viewProjection = viewProjection;
        Vector3 direction = lightDirection ?? new Vector3(-0.3f, -1.0f, -0.4f);
        s_lightDirection = direction.LengthSquared() > 1e-12f ? Vector3.Normalize(direction) : -Vector3.UnitY;
        s_lightColor = lightColor ?? Vector3.One;
        s_ambient = Math.Clamp(ambient, 0.0f, 1.0f);

        IGraphicsDevice device = Renderer.Device;
        device.SetCapability(GraphicsCapability.DepthTest, true);
        device.SetCapability(GraphicsCapability.CullFace, true);
        device.SetDepthWrite(true);
    }

    /// <summary>
    /// Begins a scene seen through a camera.
    /// </summary>
    /// <param name="camera">The camera.</param>
    /// <param name="lightDirection">The direction the light travels.</param>
    /// <param name="lightColor">The light's color and intensity.</param>
    /// <param name="ambient">The ambient light, 0..1.</param>
    public static void BeginScene(Camera3D camera, Vector3? lightDirection = null, Vector3? lightColor = null, float ambient = 0.25f)
    {
        ArgumentNullException.ThrowIfNull(camera);
        BeginScene(camera.ViewProjection, lightDirection, lightColor, ambient);
    }

    /// <summary>Ends the scene, turning depth testing and face culling off again.</summary>
    public static void EndScene()
    {
        IGraphicsDevice device = Renderer.Device;
        device.SetCapability(GraphicsCapability.DepthTest, false);
        device.SetCapability(GraphicsCapability.CullFace, false);
    }

    /// <summary>
    /// Draws a mesh with a world transform.
    /// </summary>
    /// <param name="mesh">The mesh.</param>
    /// <param name="transform">Its world transform.</param>
    /// <param name="material">How to draw it; <see langword="null"/> for lit white.</param>
    public static void DrawMesh(Mesh mesh, Matrix4x4 transform, BasicMaterial? material = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        BasicMaterial m = material ?? s_defaultMaterial;
        Shader shader = Prepare(m, Pass.Rigid);
        shader.SetUniform("uModel", transform);
        Renderer.DrawIndexed(mesh.VertexArray, mesh.IndexCount);
    }

    /// <summary>
    /// Draws every submesh of a model with one transform and material (skinned submeshes in their rest pose; see
    /// <see cref="DrawSkinnedMesh"/> to animate them).
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="transform">Its world transform.</param>
    /// <param name="material">How to draw it; <see langword="null"/> for lit white.</param>
    public static void DrawModel(Model model, Matrix4x4 transform, BasicMaterial? material = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        foreach (Mesh mesh in model.Meshes)
        {
            DrawMesh(mesh, transform, material);
        }
    }

    /// <summary>
    /// Draws many copies of a rigid mesh in as few calls as possible, one per transform.
    /// </summary>
    /// <param name="mesh">The mesh (rigid).</param>
    /// <param name="transforms">One world transform per copy.</param>
    /// <param name="material">How to draw them; the color applies to every copy.</param>
    public static void DrawMeshInstanced(Mesh mesh, ReadOnlySpan<Matrix4x4> transforms, BasicMaterial? material = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (transforms.IsEmpty)
        {
            return;
        }

        BasicMaterial m = material ?? s_defaultMaterial;
        Prepare(m, Pass.Instanced);
        VertexArray vao = mesh.GetInstancedVertexArray(s_instanceBuffer!);

        for (int start = 0; start < transforms.Length; start += MaxInstancesPerBatch)
        {
            int count = Math.Min(MaxInstancesPerBatch, transforms.Length - start);
            for (int i = 0; i < count; i++)
            {
                Span<float> slot = s_instanceData.AsSpan(i * InstanceFloats, InstanceFloats);
                MemoryMarshal.Cast<Matrix4x4, float>(transforms.Slice(start + i, 1)).CopyTo(slot);
                slot[16] = m.Color.X;
                slot[17] = m.Color.Y;
                slot[18] = m.Color.Z;
                slot[19] = m.Color.W;
            }

            s_instanceBuffer!.SetData(s_instanceData.AsSpan(0, count * InstanceFloats));
            Renderer.DrawIndexedInstanced(vao, mesh.IndexCount, (uint)count);
        }
    }

    /// <summary>
    /// Draws a skinned mesh posed by a bone palette (see <see cref="Spot.Framework.Animation.Skeleton.ComputeSkinningPalette"/>).
    /// The palette's matrices are in world space, so there is no separate transform.
    /// </summary>
    /// <param name="mesh">The skinned mesh.</param>
    /// <param name="palette">One world-space matrix per bone (at most <see cref="MaxBones"/> are used).</param>
    /// <param name="material">How to draw it; <see langword="null"/> for lit white.</param>
    public static void DrawSkinnedMesh(Mesh mesh, ReadOnlySpan<Matrix4x4> palette, BasicMaterial? material = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        BasicMaterial m = material ?? s_defaultMaterial;
        Shader shader = Prepare(m, Pass.Skinned);
        shader.SetUniform("uBones", palette.Length > MaxBones ? palette[..MaxBones] : palette);
        Renderer.DrawIndexed(mesh.VertexArray, mesh.IndexCount);
    }

    /// <summary>Releases the renderer's GPU resources. The next scene recreates them.</summary>
    public static void Shutdown()
    {
        s_rigid?.Dispose();
        s_instanced?.Dispose();
        s_skinned?.Dispose();
        s_instanceBuffer?.Dispose();
        s_rigid = s_instanced = s_skinned = null;
        s_instanceBuffer = null;
        s_device = null;
    }

    private enum Pass
    {
        Rigid,
        Instanced,
        Skinned,
    }

    // Binds the material's (or the built-in) shader with the scene and material uniforms.
    private static Shader Prepare(BasicMaterial material, Pass pass)
    {
        EnsureInitialized();
        Shader shader = material.Shader ?? pass switch
        {
            Pass.Instanced => s_instanced!,
            Pass.Skinned => s_skinned!,
            _ => s_rigid!,
        };
        shader.Use();
        shader.SetUniform("uViewProjection", s_viewProjection);
        shader.SetUniform("uColor", material.Color);
        shader.SetUniform("uLit", material.Lit ? 1 : 0);
        shader.SetUniform("uLightDirection", s_lightDirection);
        shader.SetUniform("uLightColor", s_lightColor);
        shader.SetUniform("uAmbient", s_ambient);
        shader.SetUniform("uTexture", 0);
        (material.Texture ?? Renderer2D.WhiteTexture).Bind(0);
        return shader;
    }

    // Creates the shaders and instance buffer on the current device, again whenever a new device is installed.
    private static void EnsureInitialized()
    {
        if (s_rigid is not null && ReferenceEquals(s_device, Renderer.Device))
        {
            return;
        }

        s_device = Renderer.Device;
        s_rigid = new Shader(RigidVertexSource, FragmentSource);
        s_instanced = new Shader(InstancedVertexSource, FragmentSource);
        s_skinned = new Shader(SkinnedVertexSource, FragmentSource);
        s_instanceData = new float[MaxInstancesPerBatch * InstanceFloats];
        s_instanceBuffer = new VertexBuffer(
            (uint)s_instanceData.Length,
            ShaderDataType.Float4, ShaderDataType.Float4, ShaderDataType.Float4, ShaderDataType.Float4, // model
            ShaderDataType.Float4); // color
    }
}

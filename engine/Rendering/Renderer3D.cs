using System.Numerics;

namespace Spot.Rendering;

/// <summary>
/// A simple 3D mesh renderer. Draws individual <see cref="Mesh"/> instances through a camera, with a
/// default shader that applies a fixed directional light so a model's shape reads clearly.
/// </summary>
/// <remarks>
/// This is the mid-level counterpart to <see cref="Renderer2D"/>: it consumes high-level draw
/// requests and delegates the actual draw calls to <see cref="Renderer"/>. It does not batch — each
/// mesh is a single draw call — which is plenty while materials and lighting are still to come. For
/// full control, build a <see cref="Mesh"/> and drive it here, or drop down to <see cref="Renderer"/>
/// / <see cref="Renderer.Api"/> yourself.
/// </remarks>
public static class Renderer3D
{
    private const string VertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;

        uniform mat4 uViewProjection;
        uniform mat4 uModel;

        out vec3 vNormal;

        void main()
        {
            vNormal = mat3(uModel) * aNormal;
            gl_Position = uViewProjection * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource =
        """
        #version 330 core
        in vec3 vNormal;

        uniform vec4 uColor;

        out vec4 fragColor;

        void main()
        {
            vec3 normal = normalize(vNormal);
            vec3 lightDir = normalize(vec3(0.4, 1.0, 0.6));
            float diffuse = max(dot(normal, lightDir), 0.0);
            float shade = 0.3 + 0.7 * diffuse; // fixed ambient + directional diffuse
            fragColor = vec4(uColor.rgb * shade, uColor.a);
        }
        """;

    private static Shader? s_shader;
    private static Matrix4x4 s_viewProjection = Matrix4x4.Identity;

    /// <summary>
    /// Creates the shared shader. Called once by the application after the renderer is ready.
    /// </summary>
    internal static void Init()
    {
        s_shader = new Shader(VertexShaderSource, FragmentShaderSource);
    }

    /// <summary>
    /// Begins a 3D scene. Meshes drawn until <see cref="EndScene"/> use this view-projection.
    /// </summary>
    /// <param name="viewProjection">The camera view-projection matrix.</param>
    public static void BeginScene(Matrix4x4 viewProjection) => s_viewProjection = viewProjection;

    /// <summary>
    /// Draws a mesh with the given world transform and color tint.
    /// </summary>
    /// <param name="model">The mesh's world (model) matrix.</param>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="color">A color multiplied into the shaded result.</param>
    public static void DrawMesh(Matrix4x4 model, Mesh mesh, Vector4 color)
    {
        if (s_shader is null)
        {
            return;
        }

        s_shader.Use();
        s_shader.SetUniform("uViewProjection", s_viewProjection);
        s_shader.SetUniform("uModel", model);
        s_shader.SetUniform("uColor", color);

        Renderer.DrawIndexed(mesh.VertexArray, mesh.IndexCount);
    }

    /// <summary>
    /// Ends the current 3D scene. Present for symmetry with <see cref="Renderer2D"/>; currently a no-op.
    /// </summary>
    public static void EndScene()
    {
    }
}

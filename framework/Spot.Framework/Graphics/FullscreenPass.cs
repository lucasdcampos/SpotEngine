namespace Spot.Framework.Graphics;

/// <summary>
/// Draws a fragment shader over the whole viewport — the building block for post-processing, screen-space effects
/// and blits. Write only the fragment stage: <see cref="CreateShader"/> pairs it with a vertex stage that covers the
/// screen with a single triangle and passes <c>vTexCoord</c> (0..1 across the viewport).
/// </summary>
/// <example>
/// <code>
/// var grayscale = FullscreenPass.CreateShader("""
///     #version 330 core
///     in vec2 vTexCoord;
///     uniform sampler2D uSource;
///     out vec4 fragColor;
///     void main() { float l = dot(texture(uSource, vTexCoord).rgb, vec3(0.299, 0.587, 0.114)); fragColor = vec4(vec3(l), 1.0); }
///     """);
/// scene.Unbind();
/// FullscreenPass.Draw(grayscale, scene.ColorTexture);
/// </code>
/// </example>
public static class FullscreenPass
{
    /// <summary>
    /// The vertex stage every fullscreen shader uses: no vertex buffer, three vertices from <c>gl_VertexID</c>
    /// forming a triangle that covers clip space, and <c>vTexCoord</c> spanning 0..1 over the viewport.
    /// </summary>
    public const string VertexShaderSource =
        """
        #version 330 core
        out vec2 vTexCoord;

        void main()
        {
            vec2 position = vec2(float((gl_VertexID & 1) << 2) - 1.0, float((gl_VertexID & 2) << 1) - 1.0);
            vTexCoord = position * 0.5 + 0.5;
            gl_Position = vec4(position, 0.0, 1.0);
        }
        """;

    private static IGraphicsDevice? s_device;
    private static VertexArray? s_emptyVao;

    /// <summary>
    /// Builds a fullscreen shader from a fragment stage (which reads <c>in vec2 vTexCoord</c>).
    /// </summary>
    /// <param name="fragmentSource">The fragment shader source.</param>
    /// <returns>The shader; check <see cref="Shader.IsValid"/> for compile errors.</returns>
    public static Shader CreateShader(string fragmentSource) => new(VertexShaderSource, fragmentSource);

    /// <summary>
    /// Runs a fullscreen shader over the current viewport. Bind its inputs (textures, uniforms) first.
    /// </summary>
    /// <param name="shader">A shader made with <see cref="CreateShader"/>.</param>
    public static void Draw(Shader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);
        if (s_emptyVao is null || !ReferenceEquals(s_device, Renderer.Device))
        {
            s_device = Renderer.Device;
            s_emptyVao = new VertexArray();
        }

        shader.Use();
        Renderer.DrawArrays(s_emptyVao, 3);
    }

    /// <summary>
    /// Runs a fullscreen shader with a source texture bound to unit 0 as <c>uSource</c> — the shape of most
    /// post-processing passes (sample the rendered scene, write the processed result).
    /// </summary>
    /// <param name="shader">A shader made with <see cref="CreateShader"/>.</param>
    /// <param name="source">The texture to sample (for example <see cref="Framebuffer.ColorTexture"/>).</param>
    public static void Draw(Shader shader, TextureHandle source)
    {
        ArgumentNullException.ThrowIfNull(shader);
        Renderer.Device.BindTexture(0, source);
        shader.Use();
        shader.SetUniform("uSource", 0);
        Draw(shader);
    }
}

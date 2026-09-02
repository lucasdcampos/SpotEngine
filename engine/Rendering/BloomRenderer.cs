using Silk.NET.OpenGL;

namespace Spot.Rendering;

/// <summary>
/// Generates a bloom texture from an HDR scene: a soft-knee bright-pass isolates the pixels above a
/// threshold, then a separable Gaussian is ping-ponged at half resolution to spread them into a glow.
/// The result is an HDR texture the post-processing composite adds back over the scene.
/// </summary>
/// <remarks>
/// Kept as a distinct pass from <see cref="PostProcessingRenderer"/> so the composite stays a single
/// full-screen draw and bloom can be skipped entirely (no framebuffers touched) when disabled. All
/// intermediate targets are <see cref="FramebufferFormat.RGBA16F"/> so bright values survive the blur
/// instead of clipping to white.
/// </remarks>
public static class BloomRenderer
{
    private const string VertexShaderSource = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoords;
out vec2 TexCoords;
void main()
{
    TexCoords = aTexCoords;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";

    private const string BrightFragmentSource = @"#version 330 core
out vec4 FragColor;
in vec2 TexCoords;

uniform sampler2D uScene;
uniform float uThreshold;
uniform float uKnee;

void main()
{
    vec3 c = texture(uScene, TexCoords).rgb;
    float brightness = max(c.r, max(c.g, c.b));

    // Quadratic soft knee (Jimenez): fade contribution in over a [threshold-knee, threshold+knee]
    // band so there is no hard on/off edge where a surface crosses the threshold.
    float knee = uThreshold * uKnee + 1e-5;
    float soft = clamp(brightness - uThreshold + knee, 0.0, 2.0 * knee);
    soft = (soft * soft) / (4.0 * knee + 1e-5);
    float contribution = max(soft, brightness - uThreshold) / max(brightness, 1e-5);

    FragColor = vec4(c * contribution, 1.0);
}";

    private const string BlurFragmentSource = @"#version 330 core
out vec4 FragColor;
in vec2 TexCoords;

uniform sampler2D uImage;
uniform vec2 uDirection; // one texel step along the blur axis

void main()
{
    float weight[5] = float[](0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);
    vec3 result = texture(uImage, TexCoords).rgb * weight[0];
    for (int i = 1; i < 5; ++i)
    {
        result += texture(uImage, TexCoords + uDirection * float(i)).rgb * weight[i];
        result += texture(uImage, TexCoords - uDirection * float(i)).rgb * weight[i];
    }
    FragColor = vec4(result, 1.0);
}";

    // The bright-pass knee width, as a fraction of the threshold. Fixed rather than exposed: it shapes
    // the falloff, not the look, and the artist-facing controls are threshold and intensity.
    private const float KneeFraction = 0.5f;

    private static Shader? s_brightShader;
    private static Shader? s_blurShader;
    private static VertexArray? s_quad;
    // Bloom's intermediate targets (bright pass + two ping-pong blur buffers), cached per render size in a
    // tiny most-recently-used list. Like the HDR capture target, these must not be reallocated when the
    // editor renders several differently-sized views in one frame — that churn was a large part of the
    // editor's per-frame cost. The cap bounds VRAM and evicts sizes that stop being drawn.
    private static readonly List<(uint W, uint H, Framebuffer Bright, Framebuffer PingA, Framebuffer PingB)> s_targets = new();
    private const int MaxBloomTargets = 4;

    /// <summary>Creates the bloom shaders and full-screen quad. Called once after the renderer is ready.</summary>
    public static void Init()
    {
        s_brightShader = new Shader(VertexShaderSource, BrightFragmentSource);
        s_blurShader = new Shader(VertexShaderSource, BlurFragmentSource);

        float[] quadVertices =
        {
            -1.0f,  1.0f, 0.0f, 1.0f,
            -1.0f, -1.0f, 0.0f, 0.0f,
             1.0f, -1.0f, 1.0f, 0.0f,
            -1.0f,  1.0f, 0.0f, 1.0f,
             1.0f, -1.0f, 1.0f, 0.0f,
             1.0f,  1.0f, 1.0f, 1.0f,
        };

        var vbo = new VertexBuffer(quadVertices, ShaderDataType.Float2, ShaderDataType.Float2);
        s_quad = new VertexArray();
        s_quad.AddVertexBuffer(vbo);
    }

    /// <summary>
    /// Produces a blurred HDR bloom texture from the given scene color texture.
    /// </summary>
    /// <param name="sceneHdrTexture">The HDR scene color attachment to extract bright regions from.</param>
    /// <param name="width">The scene render width in pixels.</param>
    /// <param name="height">The scene render height in pixels.</param>
    /// <param name="threshold">The luminance above which pixels contribute to bloom.</param>
    /// <param name="iterations">The number of horizontal+vertical blur pairs (larger = wider glow).</param>
    /// <returns>The OpenGL texture handle of the blurred bloom, or 0 if it could not be produced.</returns>
    public static uint Generate(uint sceneHdrTexture, int width, int height, float threshold, int iterations)
    {
        if (s_brightShader is null || s_blurShader is null || s_quad is null || width <= 0 || height <= 0)
        {
            return 0;
        }

        // Half resolution: bloom is a low-frequency glow, so it costs nothing visually and a quarter of
        // the fill rate. Clamp to at least 1 so a sliver-thin viewport can't create a zero-size target.
        uint w = (uint)System.Math.Max(1, width / 2);
        uint h = (uint)System.Math.Max(1, height / 2);

        (Framebuffer bright, Framebuffer pingA, Framebuffer pingB) = AcquireTargets(w, h);

        GL gl = Renderer.Gl;
        bool depthTest = gl.IsEnabled(EnableCap.DepthTest);
        gl.Disable(EnableCap.DepthTest);
        gl.DepthMask(false);
        gl.Disable(EnableCap.Blend);

        s_quad.Bind();

        // Bright-pass: scene HDR -> bright.
        bright.Bind();
        s_brightShader.Use();
        s_brightShader.SetUniform("uThreshold", threshold);
        s_brightShader.SetUniform("uKnee", KneeFraction);
        s_brightShader.SetUniform("uScene", 0);
        BindTexture(gl, sceneHdrTexture);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

        // Separable Gaussian, ping-ponged. Each pass reads the previous target and writes the other, so
        // horizontal/vertical alternate and no framebuffer is ever read and written in the same pass.
        s_blurShader.Use();
        s_blurShader.SetUniform("uImage", 0);

        uint sourceTexture = bright.ColorAttachment;
        bool horizontal = true;
        int passes = System.Math.Max(1, iterations) * 2;
        for (int i = 0; i < passes; i++)
        {
            Framebuffer target = horizontal ? pingA : pingB;
            target.Bind();
            s_blurShader.SetUniform("uDirection", horizontal
                ? new System.Numerics.Vector2(1.0f / w, 0.0f)
                : new System.Numerics.Vector2(0.0f, 1.0f / h));
            BindTexture(gl, sourceTexture);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

            sourceTexture = target.ColorAttachment;
            horizontal = !horizontal;
        }

        gl.BindTexture(TextureTarget.Texture2D, 0);
        gl.DepthMask(true);
        if (depthTest) gl.Enable(EnableCap.DepthTest);

        return sourceTexture;
    }

    // Returns the bright/ping-pong triple for the requested half-resolution size, reusing a cached set when
    // possible and creating (and, past the cap, evicting the least-recently-used) only on a new size. Reused
    // sets move to the front so sizes drawn every frame stay resident and transient ones fall off.
    private static (Framebuffer Bright, Framebuffer PingA, Framebuffer PingB) AcquireTargets(uint width, uint height)
    {
        for (int i = 0; i < s_targets.Count; i++)
        {
            (uint w, uint h, Framebuffer bright, Framebuffer pingA, Framebuffer pingB) = s_targets[i];
            if (w == width && h == height)
            {
                if (i != 0)
                {
                    s_targets.RemoveAt(i);
                    s_targets.Insert(0, (w, h, bright, pingA, pingB));
                }

                return (bright, pingA, pingB);
            }
        }

        var entry = (width, height,
            new Framebuffer(width, height, FramebufferFormat.RGBA16F),
            new Framebuffer(width, height, FramebufferFormat.RGBA16F),
            new Framebuffer(width, height, FramebufferFormat.RGBA16F));
        s_targets.Insert(0, entry);
        while (s_targets.Count > MaxBloomTargets)
        {
            var evicted = s_targets[^1];
            evicted.Bright.Dispose();
            evicted.PingA.Dispose();
            evicted.PingB.Dispose();
            s_targets.RemoveAt(s_targets.Count - 1);
        }

        return (entry.Item3, entry.Item4, entry.Item5);
    }

    private static void BindTexture(GL gl, uint texture)
    {
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, texture);
    }
}

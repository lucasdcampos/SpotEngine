using System;
using Silk.NET.OpenGL;
using Spot.Scenes;

namespace Spot.Rendering;

public static class PostProcessingRenderer
{
    private static Shader? s_shader;
    private static VertexArray? s_quadVAO;
    private static VertexBuffer? s_quadVBO;

    private const string VertexShaderSource = @"#version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoords;

out vec2 TexCoords;

void main()
{
    TexCoords = aTexCoords;
    gl_Position = vec4(aPos.x, aPos.y, 0.0, 1.0);
}";

    private const string FragmentShaderSource = @"#version 330 core
out vec4 FragColor;

in vec2 TexCoords;

uniform sampler2D uScreenTexture;
uniform sampler2D uBloom;
uniform int uEnableBloom;
uniform float uBloomIntensity;
uniform float uExposure;
uniform float uGamma;
uniform int uTonemap;
uniform int uEnableVignette;
uniform float uVignetteIntensity;

// Cheap, sine-free per-pixel hash in [0,1). Used for dithering.
float hash12(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// Narkowicz's ACES filmic approximation: a cheap fit of the ACES RRT+ODT that gives film-like
// contrast and a graceful highlight roll-off. Operates on already-exposed linear color.
vec3 tonemapAces(vec3 x)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main()
{
    vec3 color = texture(uScreenTexture, TexCoords).rgb;

    // Add the HDR bloom glow while still in linear space, before exposure/tone mapping, so it is
    // compressed by the same curve as the rest of the scene instead of clipping to flat white.
    if (uEnableBloom == 1)
    {
        color += texture(uBloom, TexCoords).rgb * uBloomIntensity;
    }

    // Apply exposure in linear space, then compress HDR -> LDR with the selected operator.
    color *= uExposure;

    vec3 mapped;
    if (uTonemap == 2)       mapped = tonemapAces(color);          // ACES filmic
    else if (uTonemap == 1)  mapped = color / (color + vec3(1.0)); // Reinhard
    else                     mapped = clamp(color, 0.0, 1.0);      // None (clamp)

    // Gamma correction
    mapped = pow(mapped, vec3(1.0 / uGamma));

    // Vignette
    if (uEnableVignette == 1)
    {
        vec2 uv = TexCoords;
        uv *=  1.0 - uv.yx;
        float vig = uv.x*uv.y * 15.0; // multiply with sth for intensity
        vig = pow(vig, uVignetteIntensity);
        mapped *= vig;
    }

    // Dithering. Smooth gradients (most visibly the sky) show onion-ring banding once quantized to the
    // 8-bit output. Add ~1 LSB of triangular-PDF noise in display space, right before quantization,
    // to spread each channel's rounding across the band edge so the steps read as imperceptible noise.
    // Two uniform hashes summed give a triangular distribution (flat noise variance, no visible pattern).
    float d1 = hash12(gl_FragCoord.xy);
    float d2 = hash12(gl_FragCoord.xy + 17.0);
    mapped += (d1 + d2 - 1.0) / 255.0;

    FragColor = vec4(mapped, 1.0);
}";

    public static void Init()
    {
        s_shader = new Shader(VertexShaderSource, FragmentShaderSource);

        float[] quadVertices = { 
            // positions   // texCoords
            -1.0f,  1.0f,  0.0f, 1.0f,
            -1.0f, -1.0f,  0.0f, 0.0f,
             1.0f, -1.0f,  1.0f, 0.0f,

            -1.0f,  1.0f,  0.0f, 1.0f,
             1.0f, -1.0f,  1.0f, 0.0f,
             1.0f,  1.0f,  1.0f, 1.0f
        };

        s_quadVBO = new VertexBuffer(quadVertices, ShaderDataType.Float2, ShaderDataType.Float2);

        s_quadVAO = new VertexArray();
        s_quadVAO.AddVertexBuffer(s_quadVBO);
    }

    /// <summary>
    /// Composites the HDR scene into the currently bound target: adds bloom, applies exposure, tone
    /// mapping, gamma, vignette and dithering in a single full-screen pass.
    /// </summary>
    /// <param name="screenTexture">The HDR scene color texture.</param>
    /// <param name="config">The post-processing parameters.</param>
    /// <param name="bloomTexture">The blurred bloom texture from <see cref="BloomRenderer"/>, or 0 for none.</param>
    public static void Draw(uint screenTexture, PostProcessingComponent config, uint bloomTexture = 0)
    {
        if (s_shader == null || s_quadVAO == null) return;

        bool bloomEnabled = config.EnableBloom && bloomTexture != 0;

        // This is a screen-space composite of the already-lit scene, so it must not test or write
        // depth. If it did, the full-screen quad (clip z = 0) would stamp its depth across the whole
        // target buffer and anything drawn afterwards into the same buffer that sits behind that
        // depth — most visibly the editor grid and world axes — would fail the depth test and vanish.
        bool depthTest = Renderer.Gl.IsEnabled(EnableCap.DepthTest);
        Renderer.Gl.Disable(EnableCap.DepthTest);
        Renderer.Gl.DepthMask(false);

        s_shader.Use();
        s_shader.SetUniform("uExposure", config.Exposure);
        s_shader.SetUniform("uGamma", config.Gamma);
        s_shader.SetUniform("uTonemap", (int)config.Tonemap);
        s_shader.SetUniform("uEnableVignette", config.EnableVignette ? 1 : 0);
        s_shader.SetUniform("uVignetteIntensity", config.VignetteIntensity);
        s_shader.SetUniform("uEnableBloom", bloomEnabled ? 1 : 0);
        s_shader.SetUniform("uBloomIntensity", config.BloomIntensity);

        s_shader.SetUniform("uScreenTexture", 0);
        Renderer.Gl.ActiveTexture(TextureUnit.Texture0);
        Renderer.Gl.BindTexture(TextureTarget.Texture2D, screenTexture);

        s_shader.SetUniform("uBloom", 1);
        Renderer.Gl.ActiveTexture(TextureUnit.Texture1);
        Renderer.Gl.BindTexture(TextureTarget.Texture2D, bloomEnabled ? bloomTexture : 0u);
        Renderer.Gl.ActiveTexture(TextureUnit.Texture0);

        s_quadVAO.Bind();
        Renderer.Gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

        Renderer.Gl.BindTexture(TextureTarget.Texture2D, 0);

        // Restore the caller's depth state; depth writes default to on, so re-enable the mask.
        Renderer.Gl.DepthMask(true);
        if (depthTest) Renderer.Gl.Enable(EnableCap.DepthTest);
    }
}

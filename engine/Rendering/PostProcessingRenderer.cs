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
uniform float uExposure;
uniform float uGamma;
uniform int uEnableVignette;
uniform float uVignetteIntensity;

void main()
{
    vec3 color = texture(uScreenTexture, TexCoords).rgb;
    
    // Exposure tone mapping
    vec3 mapped = vec3(1.0) - exp(-color * uExposure);
    
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

    public static void Draw(uint screenTexture, PostProcessingComponent config)
    {
        if (s_shader == null || s_quadVAO == null) return;

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
        s_shader.SetUniform("uEnableVignette", config.EnableVignette ? 1 : 0);
        s_shader.SetUniform("uVignetteIntensity", config.VignetteIntensity);

        s_shader.SetUniform("uScreenTexture", 0);
        Renderer.Gl.ActiveTexture(TextureUnit.Texture0);
        Renderer.Gl.BindTexture(TextureTarget.Texture2D, screenTexture);

        s_quadVAO.Bind();
        Renderer.Gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

        Renderer.Gl.BindTexture(TextureTarget.Texture2D, 0);

        // Restore the caller's depth state; depth writes default to on, so re-enable the mask.
        Renderer.Gl.DepthMask(true);
        if (depthTest) Renderer.Gl.Enable(EnableCap.DepthTest);
    }
}

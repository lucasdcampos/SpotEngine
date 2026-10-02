using System.Numerics;

namespace Spot.Rendering;

/// <summary>
/// The editor's infinite 2D grid: a full-screen pass that draws world-space lines at three zoom-dependent
/// levels of detail, fading between them. Drawn inside a <see cref="Renderer2D"/> scene, under whatever
/// view-projection the batch uses.
/// </summary>
public static class EditorGrid
{
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

    private static Shader? s_shader;
    private static VertexArray? s_emptyVao;
    private static IGraphicsDevice? s_device;

    /// <summary>
    /// Draws the grid under the current <see cref="Renderer2D"/> batch's view-projection, flushing the quads
    /// submitted so far first so the grid layers correctly.
    /// </summary>
    /// <param name="zoom">The camera zoom level, which picks the grid's level of detail.</param>
    public static void Draw2D(float zoom)
    {
        Renderer2D.Flush();

        if (s_shader is null || s_emptyVao is null || !ReferenceEquals(s_device, Renderer.Device))
        {
            s_device = Renderer.Device;
            s_shader = new Shader(GridVertexShaderSource, GridFragmentShaderSource);
            s_emptyVao = new VertexArray();
        }

        Matrix4x4.Invert(Renderer2D.ViewProjection, out Matrix4x4 inverseViewProjection);
        s_shader.Use();
        s_shader.SetUniform("uInverseViewProjection", inverseViewProjection);
        s_shader.SetUniform("uZoom", zoom);

        Renderer.Device.SetCapability(GraphicsCapability.Blend, true);
        Renderer.Device.SetBlendFunc(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);

        Renderer.DrawArrays(s_emptyVao, 3);
    }
}

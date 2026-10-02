using System.Numerics;
using Spot.Framework.Graphics;

namespace Spot.Engine.Rendering;

/// <summary>
/// The editor's infinite grid, with the world axes drawn as part of it: on the XY plane for 2D views
/// (<see cref="Draw2D()"/>) and on the ground plane y = 0 for 3D views (<see cref="Draw3D(Matrix4x4, Vector3)"/>).
/// </summary>
/// <remarks>
/// Each grid is a single full-screen triangle that rebuilds every pixel's world position from the inverse
/// view-projection, so it has no edges and its lines keep a constant pixel width at any distance or zoom. The
/// cell size is picked per pixel from that pixel's world footprint, in powers of ten: the finest lines fade out
/// as they crowd together and every tenth line is emphasized, so a 3D view shows finer cells near the camera and
/// coarser ones in the distance. The X/Z (3D) or X/Y (2D) axes replace the grid's own lines through the origin;
/// in 3D the Y axis is a vertical line in the same style, depth-tested against the scene.
/// </remarks>
public static class EditorGrid
{
    // Shared by both grids: the line colors, and the per-pixel level-of-detail grid with its two axes. Colors are
    // composited premultiplied so that partially covered lines blend without dark fringes.
    private const string GridFunctionsSource =
        """
        uniform vec4 uMinorLineColor;
        uniform vec4 uMajorLineColor;
        uniform vec4 uAxisColorU;   // the axis running along the plane's first coordinate (the line v = 0)
        uniform vec4 uAxisColorV;   // the axis running along the plane's second coordinate (the line u = 0)

        const float MIN_LINE_SPACING = 8.0;   // pixels between a level's lines when it has faded out entirely
        const float MIN_LEVEL = -2.0;         // the finest cells are 10^MIN_LEVEL world units
        const float LINE_WIDTH = 1.0;         // pixels
        const float AXIS_WIDTH = 1.75;        // pixels
        const float ALPHA_CUTOFF = 1.0 / 512.0;
        const float INV_LN10 = 0.4342944819;

        // Anti-aliased coverage of a line of the given width, from the distance (in pixels) to its center.
        float lineCoverage(float distancePx, float widthPx)
        {
            return clamp(widthPx * 0.5 + 0.5 - distancePx, 0.0, 1.0);
        }

        // World units per pixel along each plane coordinate. A derivative: call it before any discard.
        vec2 pixelFootprint(vec2 pos)
        {
            vec2 size = vec2(length(vec2(dFdx(pos.x), dFdy(pos.x))), length(vec2(dFdx(pos.y), dFdy(pos.y))));
            return max(size, vec2(1e-7));
        }

        // Coverage of the lines of a grid with the given cell size.
        float gridLines(vec2 pos, vec2 pixelSize, float cellSize)
        {
            vec2 distancePx = abs(fract(pos / cellSize + 0.5) - 0.5) * cellSize / pixelSize;
            return max(lineCoverage(distancePx.x, LINE_WIDTH), lineCoverage(distancePx.y, LINE_WIDTH));
        }

        vec4 premultiplied(vec4 color)
        {
            return vec4(color.rgb * color.a, color.a);
        }

        vec4 unpremultiplied(vec4 color)
        {
            return vec4(color.rgb / max(color.a, 1e-6), color.a);
        }

        // The grid at a point (u, v) of its plane, premultiplied. Three decades of lines: the finest whose lines
        // are at least MIN_LINE_SPACING pixels apart here fades in as they open up to ten times that, the next
        // grows from minor to major meanwhile, and the one above is major. Each decade takes over its successor's
        // look exactly as the level changes, so zooming never pops. Coarser lines replace finer ones (rather than
        // stacking on them) and the axes replace the lines through the origin.
        vec4 gridColor(vec2 pos, vec2 pixelSize)
        {
            float lod = log(max(pixelSize.x, pixelSize.y) * MIN_LINE_SPACING) * INV_LN10;
            float level = max(ceil(lod), MIN_LEVEL);
            float blend = clamp(level - lod, 0.0, 1.0);
            float cellSize = pow(10.0, level);

            vec4 minor = premultiplied(uMinorLineColor);
            vec4 major = premultiplied(uMajorLineColor);
            vec4 color = minor * (gridLines(pos, pixelSize, cellSize) * blend);
            color = mix(color, mix(minor, major, blend), gridLines(pos, pixelSize, cellSize * 10.0));
            color = mix(color, major, gridLines(pos, pixelSize, cellSize * 100.0));

            vec2 axisDistancePx = abs(pos) / pixelSize;
            color = mix(color, premultiplied(uAxisColorV), lineCoverage(axisDistancePx.x, AXIS_WIDTH));
            color = mix(color, premultiplied(uAxisColorU), lineCoverage(axisDistancePx.y, AXIS_WIDTH));
            return color;
        }
        """;

    private const string Grid2DVertexShaderSource =
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

    private const string Grid2DFragmentShaderSource =
        """
        #version 330 core

        in vec2 vWorldPos;
        out vec4 fragColor;

        """ + "\n" + GridFunctionsSource + "\n" +
        """

        void main()
        {
            vec4 color = gridColor(vWorldPos, pixelFootprint(vWorldPos));
            if (color.a < ALPHA_CUTOFF) discard;
            fragColor = unpremultiplied(color);
        }
        """;

    // Both 3D passes unproject each pixel's view ray: from the near plane (NDC z = 0 with System.Numerics
    // projections) to the far plane (z = 1).
    private const string Grid3DVertexShaderSource =
        """
        #version 330 core

        out vec3 vNearPoint;
        out vec3 vFarPoint;

        uniform mat4 uInverseViewProjection;

        vec3 unprojectPoint(float x, float y, float z)
        {
            vec4 unprojectedPoint = uInverseViewProjection * vec4(x, y, z, 1.0);
            return unprojectedPoint.xyz / unprojectedPoint.w;
        }

        void main()
        {
            float x = -1.0 + float((gl_VertexID & 1) << 2);
            float y = -1.0 + float((gl_VertexID & 2) << 1);
            gl_Position = vec4(x, y, 0.0, 1.0);

            vNearPoint = unprojectPoint(x, y, 0.0);
            vFarPoint = unprojectPoint(x, y, 1.0);
        }
        """;

    private const string Grid3DFunctionsSource =
        """
        in vec3 vNearPoint;
        in vec3 vFarPoint;
        out vec4 fragColor;

        uniform mat4 uViewProjection;
        uniform vec3 uCameraPos;

        // The grid fades out between these multiples of the camera's height over the plane (at least one unit):
        // a higher camera sees further, while a low one is spared the shallow angles near the horizon where the
        // grid would only be noise. It never reaches the far plane, so the grid has no hard edge.
        const float FADE_START = 5.0;
        const float FADE_END = 20.0;

        float distanceFade(float cameraDistance)
        {
            float farDistance = length(vFarPoint - uCameraPos);
            float fadeEnd = min(max(abs(uCameraPos.y), 1.0) * FADE_END, farDistance);
            return 1.0 - smoothstep(fadeEnd * (FADE_START / FADE_END), fadeEnd, cameraDistance);
        }

        // The window-space depth of a world position, as the depth test sees it.
        float depthOf(vec3 worldPos)
        {
            vec4 clip = uViewProjection * vec4(worldPos, 1.0);
            return clip.z / clip.w * 0.5 + 0.5;
        }
        """;

    private const string Grid3DFragmentShaderSource =
        """
        #version 330 core

        """ + "\n" + GridFunctionsSource + "\n" + Grid3DFunctionsSource + "\n" +
        """

        // Eight steps of a 24-bit depth buffer.
        const float DEPTH_BIAS = 8.0 / 16777216.0;

        void main()
        {
            // Where this pixel's view ray meets the ground plane y = 0.
            vec3 ray = vFarPoint - vNearPoint;
            float t = -vNearPoint.y / ray.y;
            vec3 pos = vNearPoint + t * ray;

            // Derivatives first, while every pixel of the quad is still running.
            vec2 pixelSize = pixelFootprint(pos.xz);
            float depth = depthOf(pos);
            float depthSlope = fwidth(depth);

            // The plane is behind the camera, past the far plane, or seen edge-on (t is not a number). Points
            // between the camera and the near plane (t < 0) are kept, so a camera skimming the ground still sees
            // the grid run to the bottom of the view instead of stopping where the near plane cuts it; their
            // depth clamps to the front of the buffer, where nothing else can be.
            if (!(t <= 1.0 && dot(pos - uCameraPos, ray) > 0.0)) discard;

            vec4 color = gridColor(pos.xz, pixelSize) * distanceFade(length(pos - uCameraPos));
            if (color.a < ALPHA_CUTOFF) discard;
            fragColor = unpremultiplied(color);

            // Sit a hair in front of the plane's true depth so that the grid stays visible on a surface lying on
            // it (a floor at y = 0, the top of a ground box) rather than z-fighting with it: at the exact depth
            // the two differ only by rounding, whose sign flips from pixel to pixel and frame to frame. The slope
            // term is what polygon offset adds for planes seen at a glancing angle. Geometry standing on the
            // plane still hides the grid behind it; only a sliver under a pixel tall at its base is overdrawn.
            gl_FragDepth = depth - depthSlope - DEPTH_BIAS;
        }
        """;

    private const string YAxisFragmentShaderSource =
        """
        #version 330 core

        """ + "\n" + GridFunctionsSource + "\n" + Grid3DFunctionsSource + "\n" +
        """

        uniform vec4 uAxisColorY;

        // The part of the axis on the far side of the grid from the camera reads as being behind it.
        const float HIDDEN_AXIS_OPACITY = 0.35;

        void main()
        {
            vec3 ray = vFarPoint - vNearPoint;
            vec3 viewDir = normalize(ray);
            // The angle a pixel spans, to turn the world gap between the view ray and the axis into pixels.
            float pixelAngle = max(length(dFdx(viewDir)), length(dFdy(viewDir)));

            // The point of the view ray closest to the Y axis (the line x = z = 0), and the axis point facing it.
            float t = -dot(vNearPoint.xz, ray.xz) / max(dot(ray.xz, ray.xz), 1e-12);
            vec3 rayPoint = vNearPoint + t * ray;
            vec3 axisPoint = vec3(0.0, rayPoint.y, 0.0);
            float cameraDistance = length(axisPoint - uCameraPos);
            float distancePx = length(rayPoint.xz) / max(cameraDistance * pixelAngle, 1e-12);

            // The nearest approach is behind the camera or past the far plane.
            if (!(t <= 1.0 && dot(rayPoint - uCameraPos, ray) > 0.0)) discard;

            float alpha = uAxisColorY.a * lineCoverage(distancePx, AXIS_WIDTH) * distanceFade(cameraDistance);
            if (axisPoint.y * uCameraPos.y < 0.0) alpha *= HIDDEN_AXIS_OPACITY;
            if (alpha < ALPHA_CUTOFF) discard;

            fragColor = vec4(uAxisColorY.rgb, alpha);
            gl_FragDepth = depthOf(axisPoint);
        }
        """;

    private static Shader? s_shader2D;
    private static Shader? s_shader3D;
    private static Shader? s_yAxisShader;
    private static VertexArray? s_emptyVao;
    private static IGraphicsDevice? s_device;

    // The Y axis fades out as |forward.y| goes from the first value (a view pitched ~53 degrees) to the second (~72).
    private const float YAxisFadeStart = 0.8f;
    private const float YAxisFadeEnd = 0.95f;

    /// <summary>
    /// Draws the 2D grid with the <see cref="EditorGridStyle.Default"/> colors. See <see cref="Draw2D(EditorGridStyle)"/>.
    /// </summary>
    public static void Draw2D() => Draw2D(EditorGridStyle.Default);

    /// <summary>
    /// Draws the 2D grid on the XY plane, with the X and Y axes, under the current <see cref="Renderer2D"/>
    /// batch's view-projection. Flushes the quads submitted so far first so the grid layers over them.
    /// </summary>
    /// <param name="style">The colors to draw with.</param>
    public static void Draw2D(EditorGridStyle style)
    {
        Renderer2D.Flush();

        if (!Matrix4x4.Invert(Renderer2D.ViewProjection, out Matrix4x4 inverseViewProjection))
        {
            return;
        }

        EnsureResources();

        s_shader2D!.Use();
        s_shader2D.SetUniform("uInverseViewProjection", inverseViewProjection);
        SetLineColors(s_shader2D, style, axisU: style.AxisXColor, axisV: style.AxisYColor);

        Renderer.Device.SetCapability(GraphicsCapability.Blend, true);
        Renderer.Device.SetBlendFunc(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);

        Renderer.DrawArrays(s_emptyVao!, 3);
    }

    /// <summary>
    /// Draws the 3D grid with the <see cref="EditorGridStyle.Default"/> colors.
    /// See <see cref="Draw3D(Matrix4x4, Vector3, EditorGridStyle)"/>.
    /// </summary>
    /// <param name="viewProjection">The camera's view-projection.</param>
    /// <param name="cameraPosition">The camera's world position.</param>
    public static void Draw3D(Matrix4x4 viewProjection, Vector3 cameraPosition) =>
        Draw3D(viewProjection, cameraPosition, EditorGridStyle.Default);

    /// <summary>
    /// Draws the 3D grid on the ground plane y = 0 with the X and Z axes, then the vertical Y axis (which fades
    /// out in views looking down or up along it, where it would only slant across the screen). Draw it after
    /// the scene, into a target holding the scene's depth: both are depth-tested so geometry in front hides them,
    /// while a surface lying on the plane (a floor at y = 0) shows the grid over it steadily instead of
    /// z-fighting with it. The grid reaches the camera even inside the near plane. Neither writes depth.
    /// Leaves depth testing and blending enabled.
    /// </summary>
    /// <param name="viewProjection">The camera's view-projection.</param>
    /// <param name="cameraPosition">The camera's world position, which sets how far the grid reaches.</param>
    /// <param name="style">The colors to draw with.</param>
    public static void Draw3D(Matrix4x4 viewProjection, Vector3 cameraPosition, EditorGridStyle style)
    {
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 inverseViewProjection))
        {
            return;
        }

        EnsureResources();

        Renderer.SetDepthTest(true);
        Renderer.SetDepthWrite(false);
        Renderer.Device.SetCapability(GraphicsCapability.Blend, true);
        Renderer.Device.SetBlendFunc(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);

        s_shader3D!.Use();
        SetCamera(s_shader3D, viewProjection, inverseViewProjection, cameraPosition);
        SetLineColors(s_shader3D, style, axisU: style.AxisXColor, axisV: style.AxisZColor);
        Renderer.DrawArrays(s_emptyVao!, 3);

        float yAxisOpacity = YAxisOpacity(inverseViewProjection);
        if (yAxisOpacity > 0.0f)
        {
            s_yAxisShader!.Use();
            SetCamera(s_yAxisShader, viewProjection, inverseViewProjection, cameraPosition);
            s_yAxisShader.SetUniform("uAxisColorY", style.AxisYColor with { W = style.AxisYColor.W * yAxisOpacity });
            Renderer.DrawArrays(s_emptyVao!, 3);
        }

        Renderer.SetDepthWrite(true);
    }

    // Seen end-on, from a camera looking down at the grid (or up at it), the Y axis is no use as a guide: all
    // that shows is the stretch rising past the camera, a stray line slanting across the view. So it fades out
    // as the view direction turns from level toward vertical.
    private static float YAxisOpacity(Matrix4x4 inverseViewProjection)
    {
        Vector4 near = Vector4.Transform(new Vector4(0, 0, 0, 1), inverseViewProjection);
        Vector4 far = Vector4.Transform(new Vector4(0, 0, 1, 1), inverseViewProjection);
        Vector3 forward = far.AsVector3() / far.W - near.AsVector3() / near.W;
        if (!(forward.LengthSquared() > 0.0f))
        {
            return 0.0f;
        }

        float alongY = MathF.Abs(Vector3.Normalize(forward).Y);
        float t = Math.Clamp((alongY - YAxisFadeStart) / (YAxisFadeEnd - YAxisFadeStart), 0.0f, 1.0f);
        return 1.0f - t * t * (3.0f - 2.0f * t);
    }

    // (Re)creates the shaders for the current device: the editor's device can be replaced (e.g. by tests).
    private static void EnsureResources()
    {
        if (s_shader2D is not null && ReferenceEquals(s_device, Renderer.Device))
        {
            return;
        }

        s_device = Renderer.Device;
        s_shader2D = new Shader(Grid2DVertexShaderSource, Grid2DFragmentShaderSource);
        s_shader3D = new Shader(Grid3DVertexShaderSource, Grid3DFragmentShaderSource);
        s_yAxisShader = new Shader(Grid3DVertexShaderSource, YAxisFragmentShaderSource);
        s_emptyVao = new VertexArray();
    }

    private static void SetCamera(Shader shader, Matrix4x4 viewProjection, Matrix4x4 inverseViewProjection, Vector3 cameraPosition)
    {
        shader.SetUniform("uViewProjection", viewProjection);
        shader.SetUniform("uInverseViewProjection", inverseViewProjection);
        shader.SetUniform("uCameraPos", cameraPosition);
    }

    private static void SetLineColors(Shader shader, EditorGridStyle style, Vector4 axisU, Vector4 axisV)
    {
        shader.SetUniform("uMinorLineColor", style.MinorLineColor);
        shader.SetUniform("uMajorLineColor", style.MajorLineColor);
        shader.SetUniform("uAxisColorU", axisU);
        shader.SetUniform("uAxisColorV", axisV);
    }
}

using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Engine;
using Spot.Engine.Graphics;

namespace SolarSystem;

/// <summary>
/// Draws what the engine has no component for, from custom render passes inside its HDR frame — so all of it is
/// tone-mapped and blooms with the rest of the scene:
/// <list type="bullet">
/// <item><see cref="RenderStage.BeforeOpaque"/>: the starfield and the Milky Way, one fullscreen shader.</item>
/// <item><see cref="RenderStage.AfterOpaque"/>: the Sun and every <see cref="CelestialBody"/>, with procedural
/// shaders, depth-tested against the engine-drawn asteroids.</item>
/// <item><see cref="RenderStage.AfterTransparent"/>: orbit trails (the framework's <see cref="BillboardBatch"/>),
/// then atmospheres, the corona and the rings, blended over the bodies.</item>
/// </list>
/// The Sun's <see cref="Light"/> lights the bodies, and the scene's directional light supplies the faint
/// ambient fill, so the same lights drive these shaders and the engine's own lit renderer.
/// </summary>
public sealed class SpaceRenderer : Component
{
    private const int OrbitSegments = 256;

    public bool ShowOrbits { get; set; } = true;

    /// <summary>Gets or sets how bright the Sun's disc is; above 1 it blooms.</summary>
    [InspectorRange(0.0f, 20.0f, 0.05f)]
    public float SunBrightness { get; set; } = 1.6f;

    [InspectorRange(0.0f, 10.0f, 0.05f)]
    public float CoronaBrightness { get; set; } = 0.75f;

    /// <summary>Gets or sets the width of the orbit lines, in pixels.</summary>
    [InspectorRange(0.5f, 8.0f, 0.1f)]
    public float OrbitWidth { get; set; } = 2.4f;

    /// <summary>Gets or sets how visible an orbit is away from its planet's bright trail.</summary>
    [InspectorRange(0.0f, 1.0f, 0.01f)]
    public float OrbitOpacity { get; set; } = 0.1f;

    /// <summary>Gets or sets the body whose orbit is drawn brighter — the one under the pointer.</summary>
    public CelestialBody? Highlighted { get; set; }

    private readonly List<IRenderPass> _passes = new();
    private Mesh? _sphere;
    private Mesh? _plane;
    private Mesh? _quad;
    private Shader? _planetShader;
    private Shader? _sunShader;
    private Shader? _ringShader;
    private Shader? _atmosphereShader;
    private Shader? _coronaShader;
    private Shader? _skyShader;
    private Texture2D? _lineTexture;
    private float _time;

    public override void OnStart()
    {
        try
        {
            _sphere = ToMesh(PrimitiveMeshes.Sphere(1.0f, 128, 64));
            _plane = ToMesh(PrimitiveMeshes.Plane(new Vector2(2.0f, 2.0f), 1));
            _quad = ToMesh(PrimitiveMeshes.Quad(Vector2.One));
            _planetShader = Compile("planet", SpaceShaders.BodyVertex, SpaceShaders.PlanetFragment);
            _sunShader = Compile("sun", SpaceShaders.BodyVertex, SpaceShaders.SunFragment);
            _ringShader = Compile("ring", SpaceShaders.BodyVertex, SpaceShaders.RingFragment);
            _atmosphereShader = Compile("atmosphere", SpaceShaders.BillboardVertex, SpaceShaders.AtmosphereFragment);
            _coronaShader = Compile("corona", SpaceShaders.BillboardVertex, SpaceShaders.CoronaFragment);
            _skyShader = Compile("sky", FullscreenPass.VertexShaderSource, SpaceShaders.SkyFragment);
            _lineTexture = UITextures.LineProfile();
        }
        catch (Exception ex)
        {
            Log.Error("Space renderer: could not create its GPU resources: {0}", ex.Message);
            return;
        }

        _passes.Add(new DelegateRenderPass(RenderStage.BeforeOpaque, DrawSky, name: "Starfield"));
        _passes.Add(new DelegateRenderPass(RenderStage.AfterOpaque, DrawBodies, name: "Celestial Bodies"));
        _passes.Add(new DelegateRenderPass(RenderStage.AfterTransparent, DrawOrbits, order: 0, name: "Orbits"));
        _passes.Add(new DelegateRenderPass(RenderStage.AfterTransparent, DrawGlows, order: 10, name: "Atmospheres & Rings"));
        foreach (IRenderPass pass in _passes)
        {
            Scene.AddRenderPass(pass);
        }
    }

    public override void OnUpdate(float deltaTime) => _time += deltaTime;

    public override void OnDestroy()
    {
        foreach (IRenderPass pass in _passes)
        {
            Scene.RemoveRenderPass(pass);
        }

        _passes.Clear();
        _sphere?.Dispose();
        _plane?.Dispose();
        _quad?.Dispose();
        _planetShader?.Dispose();
        _sunShader?.Dispose();
        _ringShader?.Dispose();
        _atmosphereShader?.Dispose();
        _coronaShader?.Dispose();
        _skyShader?.Dispose();
        _lineTexture?.Dispose();
    }

    private void DrawSky(RenderContext context)
    {
        if (_skyShader is null)
        {
            return;
        }

        Matrix4x4.Invert(context.ViewProjection, out Matrix4x4 inverse);
        _skyShader.Use();
        _skyShader.SetUniform("uInverseViewProjection", inverse);
        _skyShader.SetUniform("uCameraPos", context.CameraPosition);
        _skyShader.SetUniform("uPixelAngle", PixelAngle(context));
        _skyShader.SetUniform("uTime", _time);

        // The sky is infinitely far away: it neither tests nor writes depth, so everything draws over it.
        Renderer.SetDepthTest(false);
        Renderer.SetDepthWrite(false);
        FullscreenPass.Draw(_skyShader);
        Renderer.SetDepthWrite(true);
        Renderer.SetDepthTest(true);
    }

    private void DrawBodies(RenderContext context)
    {
        if (Simulation.Current is not { } simulation || _sphere is null)
        {
            return;
        }

        SunLight(simulation, out Vector3 sunPosition, out Vector3 sunColor);
        Vector3 ambient = AmbientLight();

        Renderer.SetDepthTest(true);
        Renderer.SetDepthWrite(true);
        Renderer.SetFaceCulling(true);

        foreach (CelestialBody body in simulation.Bodies)
        {
            if (!body.Entity.IsActiveInHierarchy())
            {
                continue;
            }

            if (body.Kind == BodyKind.Star)
            {
                if (_sunShader is null) continue;
                Shader sun = _sunShader;
                sun.Use();
                sun.SetUniform("uViewProjection", context.ViewProjection);
                sun.SetUniform("uModel", body.World);
                sun.SetUniform("uCenter", body.Position);
                sun.SetUniform("uCameraPos", context.CameraPosition);
                sun.SetUniform("uColorA", Linear(body.ColorA));
                sun.SetUniform("uColorB", Linear(body.ColorB));
                sun.SetUniform("uColorC", Linear(body.ColorC));
                sun.SetUniform("uSeed", body.Seed);
                sun.SetUniform("uTime", _time);
                sun.SetUniform("uIntensity", SunBrightness);
                Renderer.DrawIndexed(_sphere.VertexArray, _sphere.IndexCount);
                continue;
            }

            if (_planetShader is null) continue;
            Shader shader = _planetShader;
            shader.Use();
            shader.SetUniform("uViewProjection", context.ViewProjection);
            shader.SetUniform("uModel", body.World);
            shader.SetUniform("uCenter", body.Position);
            shader.SetUniform("uRadius", body.Radius);
            shader.SetUniform("uCameraPos", context.CameraPosition);
            shader.SetUniform("uSunPos", sunPosition);
            shader.SetUniform("uSunColor", sunColor);
            shader.SetUniform("uAmbient", ambient);
            shader.SetUniform("uSurface", (int)body.Surface);
            shader.SetUniform("uColorA", Linear(body.ColorA));
            shader.SetUniform("uColorB", Linear(body.ColorB));
            shader.SetUniform("uColorC", Linear(body.ColorC));
            shader.SetUniform("uSeed", body.Seed);
            shader.SetUniform("uTime", _time);
            shader.SetUniform("uBandFrequency", body.BandFrequency);
            shader.SetUniform("uTurbulence", body.Turbulence);
            shader.SetUniform("uStorm", Storm(body));
            shader.SetUniform("uStormColor", Linear(body.StormColor));
            shader.SetUniform("uAtmosphere", Atmosphere(body));
            shader.SetUniform("uPole", body.PoleAxis);
            shader.SetUniform("uRing", body.RingOuter > 0.0f ? new Vector2(body.RingInner, body.RingOuter) : Vector2.Zero);
            shader.SetUniform("uRingStyle", body.NarrowRings ? 1 : 0);
            shader.SetUniform("uRingColor", RingColor(body));
            Renderer.DrawIndexed(_sphere.VertexArray, _sphere.IndexCount);
        }
    }

    private void DrawOrbits(RenderContext context)
    {
        if (!ShowOrbits || Simulation.Current is not { } simulation || _lineTexture is null)
        {
            return;
        }

        Vector3 camera = context.CameraPosition;
        float pixel = PixelAngle(context);
        const float step = MathF.Tau / OrbitSegments;

        // The ribbons are flat quads seen from either side, so they must not be culled.
        Renderer.SetFaceCulling(false);
        BillboardBatch.Begin(context.ViewProjection);
        foreach (CelestialBody body in simulation.Bodies)
        {
            if (body.OrbitRadius <= 0.0f || !body.Entity.IsActiveInHierarchy())
            {
                continue;
            }

            Vector3 center = body.Primary?.Position ?? Vector3.Zero;

            // A moon's orbit only shows once the camera is near enough for it not to clutter the overview.
            float fade = 1.0f;
            if (body.Primary is not null)
            {
                float distance = Vector3.Distance(camera, center) / body.OrbitRadius;
                fade = 1.0f - SmoothStep(18.0f, 40.0f, distance);
                if (fade <= 0.01f) continue;
            }

            float highlight = body == Highlighted ? 2.0f : 1.0f;
            Vector3 tint = Linear(body.AccentColor);
            float radius = body.OrbitRadius;

            // Walk backwards from the body: a bright trail behind it, fading to a faint full circle.
            for (int i = 0; i < OrbitSegments; i++)
            {
                float a0 = body.OrbitAngle - i * step;
                float a1 = a0 - step;
                Vector3 p0 = center + new Vector3(MathF.Cos(a0), 0.0f, -MathF.Sin(a0)) * radius;
                Vector3 p1 = center + new Vector3(MathF.Cos(a1), 0.0f, -MathF.Sin(a1)) * radius;
                Vector3 mid = (p0 + p1) * 0.5f;
                Vector3 along = (p1 - p0) * 0.51f;
                Vector3 toCamera = camera - mid;
                Vector3 side = Vector3.Cross(along, toCamera);
                if (side.LengthSquared() < 1e-12f) continue;
                side = Vector3.Normalize(side) * (OrbitWidth * 0.5f * toCamera.Length() * pixel);

                float trail = MathF.Exp(-(i + 0.5f) * step * 1.5f);
                float alpha = MathF.Min(1.0f, (OrbitOpacity + 0.55f * trail) * highlight * fade);
                BillboardBatch.Draw(mid, along, side, new Vector4(tint, alpha), _lineTexture, BlendMode.Additive);
            }
        }

        BillboardBatch.End();
        Renderer.SetFaceCulling(true);
    }

    private void DrawGlows(RenderContext context)
    {
        if (Simulation.Current is not { } simulation || _quad is null)
        {
            return;
        }

        SunLight(simulation, out Vector3 sunPosition, out Vector3 sunColor);
        Vector3 ambient = AmbientLight();
        CameraAxes(context, out Vector3 right, out Vector3 up);

        IGraphicsDevice device = Renderer.Device;
        Renderer.SetDepthTest(true);
        Renderer.SetDepthWrite(false);
        Renderer.SetFaceCulling(false);
        device.SetCapability(GraphicsCapability.Blend, true);
        device.SetBlendFunc(BlendFactor.One, BlendFactor.One);

        foreach (CelestialBody body in simulation.Bodies)
        {
            if (!body.Entity.IsActiveInHierarchy())
            {
                continue;
            }

            Vector3 center = body.Position;
            float distance = Vector3.Distance(context.CameraPosition, center);
            if (body.Kind == BodyKind.Star)
            {
                if (_coronaShader is null) continue;
                Shader corona = _coronaShader;
                corona.Use();
                SetBillboard(corona, context, center, right, up, body.Radius * 7.0f);
                corona.SetUniform("uRadius", body.Radius);
                corona.SetUniform("uColorA", Linear(body.ColorA));
                corona.SetUniform("uColorB", Linear(body.ColorB));
                corona.SetUniform("uTime", _time);
                corona.SetUniform("uIntensity", CoronaBrightness);
                Renderer.DrawIndexed(_quad.VertexArray, _quad.IndexCount);
                continue;
            }

            float outer = body.Radius * (1.0f + body.AtmosphereHeight);
            if (_atmosphereShader is null || body.Atmosphere.W <= 0.0f || distance <= outer * 1.02f)
            {
                continue;
            }

            // A quad at the center must be a little larger than the sphere to cover its silhouette up close.
            float halfSize = outer / MathF.Sqrt(MathF.Max(1.0f - outer * outer / (distance * distance), 1e-4f)) * 1.02f;
            Shader atmosphere = _atmosphereShader;
            atmosphere.Use();
            SetBillboard(atmosphere, context, center, right, up, halfSize);
            atmosphere.SetUniform("uRadius", body.Radius);
            atmosphere.SetUniform("uOuterRadius", outer);
            atmosphere.SetUniform("uSunPos", sunPosition);
            atmosphere.SetUniform("uSunColor", sunColor);
            atmosphere.SetUniform("uAtmosphere", Atmosphere(body));
            Renderer.DrawIndexed(_quad.VertexArray, _quad.IndexCount);
        }

        // Rings blend over whatever is behind them; the opaque planet already hides their far side.
        device.SetBlendFunc(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);
        if (_ringShader is not null && _plane is not null)
        {
            foreach (CelestialBody body in simulation.Bodies)
            {
                if (body.RingOuter <= 0.0f || !body.Entity.IsActiveInHierarchy())
                {
                    continue;
                }

                Shader ring = _ringShader;
                ring.Use();
                ring.SetUniform("uViewProjection", context.ViewProjection);
                ring.SetUniform("uModel",
                    Matrix4x4.CreateScale(body.Radius * body.RingOuter) * body.Orientation * Matrix4x4.CreateTranslation(body.Position));
                ring.SetUniform("uCenter", body.Position);
                ring.SetUniform("uRadius", body.Radius);
                ring.SetUniform("uCameraPos", context.CameraPosition);
                ring.SetUniform("uSunPos", sunPosition);
                ring.SetUniform("uSunColor", sunColor);
                ring.SetUniform("uAmbient", ambient);
                ring.SetUniform("uPole", body.PoleAxis);
                ring.SetUniform("uRing", new Vector2(body.RingInner, body.RingOuter));
                ring.SetUniform("uRingStyle", body.NarrowRings ? 1 : 0);
                ring.SetUniform("uRingColor", RingColor(body));
                ring.SetUniform("uSeed", body.Seed);
                Renderer.DrawIndexed(_plane.VertexArray, _plane.IndexCount);
            }
        }

        // Hand the engine back the state its 3D pipeline runs with.
        device.SetCapability(GraphicsCapability.Blend, false);
        Renderer.SetDepthWrite(true);
        Renderer.SetFaceCulling(true);
    }

    private static void SetBillboard(Shader shader, in RenderContext context, Vector3 center, Vector3 right, Vector3 up, float halfSize)
    {
        shader.SetUniform("uViewProjection", context.ViewProjection);
        shader.SetUniform("uCenter", center);
        shader.SetUniform("uRight", right);
        shader.SetUniform("uUp", up);
        shader.SetUniform("uHalfSize", halfSize);
        shader.SetUniform("uCameraPos", context.CameraPosition);
    }

    // The Sun's position and the color its point light shines with (white if it has no light).
    private static void SunLight(Simulation simulation, out Vector3 position, out Vector3 color)
    {
        position = Vector3.Zero;
        color = Vector3.One;
        if (simulation.Sun is not { } sun)
        {
            return;
        }

        position = sun.Position;
        if (sun.Entity.TryGetComponent(out Light? light) && light.Enabled)
        {
            color = light.Color * light.Intensity;
        }
    }

    // The fill light: the scene's directional light, as the engine itself uses it for ambient.
    private Vector3 AmbientLight()
    {
        foreach (Entity entity in Scene.View<Light>())
        {
            Light light = entity.GetComponent<Light>();
            if (light.Enabled && light.Type == LightType.Directional && entity.IsActiveInHierarchy())
            {
                return light.Color * light.Intensity * light.AmbientIntensity;
            }
        }

        return new Vector3(0.02f);
    }

    /// <summary>The angle one pixel covers at the center of the view, in radians.</summary>
    internal static float PixelAngle(in RenderContext context)
    {
        Matrix4x4.Invert(context.ViewProjection, out Matrix4x4 inverse);
        Vector3 top = Unproject(inverse, 0.0f, 1.0f);
        Vector3 bottom = Unproject(inverse, 0.0f, -1.0f);
        float depth = Vector3.Distance(Unproject(inverse, 0.0f, 0.0f), context.CameraPosition);
        float height = MathF.Max(context.ViewportHeight, 1u);
        return depth > 0.0f ? Vector3.Distance(top, bottom) / depth / height : 0.001f;
    }

    private static void CameraAxes(in RenderContext context, out Vector3 right, out Vector3 up)
    {
        Matrix4x4.Invert(context.ViewProjection, out Matrix4x4 inverse);
        right = Vector3.Normalize(Unproject(inverse, 1.0f, 0.0f) - Unproject(inverse, -1.0f, 0.0f));
        up = Vector3.Normalize(Unproject(inverse, 0.0f, 1.0f) - Unproject(inverse, 0.0f, -1.0f));
    }

    private static Vector3 Unproject(in Matrix4x4 inverse, float x, float y)
    {
        Vector4 p = Vector4.Transform(new Vector4(x, y, 0.5f, 1.0f), inverse);
        return new Vector3(p.X, p.Y, p.Z) / p.W;
    }

    private static Vector4 Storm(CelestialBody body)
    {
        Vector4 s = body.Storm;
        const float rad = MathF.PI / 180.0f;
        return new Vector4(s.X * rad, s.Y * rad, s.Z * rad, s.W);
    }

    private static Vector4 Atmosphere(CelestialBody body) => new(Linear(new Vector3(body.Atmosphere.X, body.Atmosphere.Y, body.Atmosphere.Z)), body.Atmosphere.W);

    private static Vector4 RingColor(CelestialBody body) => new(Linear(new Vector3(body.RingColor.X, body.RingColor.Y, body.RingColor.Z)), body.RingColor.W);

    /// <summary>Converts a color picked on screen (sRGB) to the linear light the shaders work in.</summary>
    internal static Vector3 Linear(Vector3 srgb) =>
        new(MathF.Pow(MathF.Max(srgb.X, 0.0f), 2.2f), MathF.Pow(MathF.Max(srgb.Y, 0.0f), 2.2f), MathF.Pow(MathF.Max(srgb.Z, 0.0f), 2.2f));

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    private static Mesh ToMesh(MeshData data) => new(data.Vertices, data.Indices);

    private static Shader? Compile(string name, string vertex, string fragment)
    {
        var shader = new Shader(vertex, fragment);
        if (shader.IsValid)
        {
            return shader;
        }

        Log.Error("Space renderer: the {0} shader failed to compile; it will not be drawn.\n{1}", name, shader.ErrorLog);
        shader.Dispose();
        return null;
    }
}

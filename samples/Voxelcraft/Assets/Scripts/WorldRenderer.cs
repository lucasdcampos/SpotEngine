using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Framework;
using Spot.Framework.Graphics;
using Spot.Framework.Mathematics;

namespace Voxelcraft;

/// <summary>
/// Draws the voxel world from custom render passes inside the engine's HDR frame, so the post-processing stack
/// (ACES, bloom, FXAA) applies to all of it:
/// <list type="bullet">
/// <item><see cref="RenderStage.BeforeOpaque"/>: the sun's shadow map, the sky (gradient, square sun and moon,
/// stars, clouds) and the terrain, front to back, culled to the view.</item>
/// <item><see cref="RenderStage.AfterOpaque"/>: the chips of broken blocks.</item>
/// <item><see cref="RenderStage.AfterTransparent"/>: water, back to front, then the outline of the targeted
/// block.</item>
/// </list>
/// </summary>
public sealed class WorldRenderer : Component
{
    /// <summary>Gets or sets whether the sun and moon cast shadows.</summary>
    public bool Shadows { get; set; } = true;

    /// <summary>Gets or sets the resolution of the shadow map, in texels.</summary>
    [InspectorRange(512.0f, 4096.0f, 512.0f)]
    public int ShadowResolution { get; set; } = 2048;

    /// <summary>Gets or sets how far around the camera shadows reach, in blocks.</summary>
    [InspectorRange(16.0f, 200.0f, 1.0f)]
    public float ShadowDistance { get; set; } = 80.0f;

    public static WorldRenderer? Current { get; private set; }

    public BlockParticles Particles { get; } = new();

    /// <summary>Gets the chunks drawn last frame and the chunks in range.</summary>
    public int VisibleChunks { get; private set; }

    public int DrawnQuads { get; private set; }

    private readonly List<IRenderPass> _passes = new();
    private readonly List<(Chunk Chunk, float Distance)> _visible = new();
    private readonly float[] _outline = new float[24 * 3];
    private Shader? _terrain;
    private Shader? _water;
    private Shader? _shadow;
    private Shader? _sky;
    private Shader? _line;
    private Shader? _particle;
    private TextureHandle _atlas;
    private TextureHandle _tintMask;
    private DepthFramebuffer? _shadowMap;
    private VertexArray? _outlineArray;
    private VertexBuffer? _outlineBuffer;
    private Matrix4x4 _lightSpace = Matrix4x4.Identity;
    private Vector3 _shadowCenter;
    private bool _shadowReady;
    private bool _underwater;
    private float _time;

    public override void OnStart()
    {
        Current = this;
        try
        {
            BlockAtlas.Build();
            _atlas = CreateTexture(BlockAtlas.Pixels, TextureFilter.Nearest);
            _tintMask = CreateTexture(BlockAtlas.TintMask, TextureFilter.Nearest);
            _terrain = Compile("terrain", WorldShaders.TerrainVertex, WorldShaders.TerrainFragment);
            _water = Compile("water", WorldShaders.WaterVertex, WorldShaders.WaterFragment);
            _shadow = Compile("shadow", WorldShaders.ShadowVertex, WorldShaders.ShadowFragment);
            _sky = Compile("sky", FullscreenPass.VertexShaderSource, WorldShaders.SkyFragment);
            _line = Compile("outline", WorldShaders.LineVertex, WorldShaders.LineFragment);
            _particle = Compile("particle", WorldShaders.ParticleVertex, WorldShaders.ParticleFragment);
        }
        catch (Exception ex)
        {
            Log.Error("Voxelcraft: the world renderer could not create its GPU resources: {0}", ex.Message);
            return;
        }

        _passes.Add(new DelegateRenderPass(RenderStage.BeforeOpaque, DrawWorld, name: "Voxel Terrain"));
        _passes.Add(new DelegateRenderPass(RenderStage.AfterOpaque, DrawParticles, name: "Block Particles"));
        _passes.Add(new DelegateRenderPass(RenderStage.AfterTransparent, DrawWater, order: 0, name: "Voxel Water"));
        _passes.Add(new DelegateRenderPass(RenderStage.AfterTransparent, DrawOutline, order: 10, name: "Block Outline"));
        foreach (IRenderPass pass in _passes) Scene.AddRenderPass(pass);
    }

    public override void OnDestroy()
    {
        foreach (IRenderPass pass in _passes) Scene.RemoveRenderPass(pass);
        _passes.Clear();
        _terrain?.Dispose();
        _water?.Dispose();
        _shadow?.Dispose();
        _sky?.Dispose();
        _line?.Dispose();
        _particle?.Dispose();
        _shadowMap?.Dispose();
        _outlineArray?.Dispose();
        _outlineBuffer?.Dispose();
        Particles.Dispose();
        if (_atlas.Id != 0) Renderer.Device.DeleteTexture(_atlas);
        if (_tintMask.Id != 0) Renderer.Device.DeleteTexture(_tintMask);
        _atlas = default;
        _tintMask = default;
        if (Current == this) Current = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        _time = (_time + deltaTime) % 3600.0f;
        if (VoxelWorld.Current?.World is { } world) Particles.Update(world, deltaTime);
    }

    // ---- passes ----

    private void DrawWorld(RenderContext context)
    {
        World? world = VoxelWorld.Current?.World;
        DayNightCycle? cycle = DayNightCycle.Current;
        if (world is null || cycle is null || _terrain is null) return;

        _underwater = world.GetBlock(BlockPos.Floor(context.CameraPosition)) == BlockId.Water;
        CollectVisible(world, context);

        if (Shadows && _shadow is not null && cycle.LightColor.LengthSquared() > 1e-4f && cycle.LightDirection.Y > 0.04f)
        {
            RenderShadowMap(world, context, cycle);
        }
        else
        {
            _shadowReady = false;
        }

        if (_sky is not null)
        {
            Matrix4x4.Invert(context.ViewProjection, out Matrix4x4 inverse);
            _sky.Use();
            SetSky(_sky, cycle);
            _sky.SetUniform("uInverseViewProjection", inverse);
            _sky.SetUniform("uCameraPos", context.CameraPosition);
            _sky.SetUniform("uMoonDir", cycle.MoonDirection);
            _sky.SetUniform("uLightColor", cycle.LightColor);
            _sky.SetUniform("uAmbientSky", cycle.AmbientSky);
            _sky.SetUniform("uStars", cycle.Stars);
            _sky.SetUniform("uTime", _time);
            _sky.SetUniform("uStarAngle", cycle.TimeOfDay * MathF.Tau);
            _sky.SetUniform("uUnderwater", _underwater ? 1.0f : 0.0f);
            Renderer.SetDepthTest(false);
            Renderer.SetDepthWrite(false);
            FullscreenPass.Draw(_sky);
            Renderer.SetDepthWrite(true);
            Renderer.SetDepthTest(true);
        }

        Shader shader = _terrain;
        shader.Use();
        SetCommon(shader, context, cycle, textures: true);
        Renderer.SetDepthTest(true);
        Renderer.SetDepthWrite(true);
        Renderer.SetFaceCulling(true);

        int quads = 0;
        foreach ((Chunk chunk, _) in _visible)
        {
            ChunkMesh? mesh = chunk.Mesh;
            if (mesh?.Opaque is null) continue;
            shader.SetUniform("uChunkOrigin", chunk.Origin);
            Renderer.DrawIndexed(mesh.Opaque, mesh.OpaqueIndexCount);
            quads += (int)mesh.OpaqueIndexCount / 6;
        }

        DrawnQuads = quads;
    }

    private void DrawWater(RenderContext context)
    {
        World? world = VoxelWorld.Current?.World;
        DayNightCycle? cycle = DayNightCycle.Current;
        if (world is null || cycle is null || _water is null) return;

        Shader shader = _water;
        shader.Use();
        SetCommon(shader, context, cycle, textures: false);

        IGraphicsDevice device = Renderer.Device;
        Renderer.SetDepthTest(true);
        Renderer.SetDepthWrite(false);
        Renderer.SetFaceCulling(false);
        device.SetCapability(GraphicsCapability.Blend, true);
        device.SetBlendFunc(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);

        // Back to front, so nearer water blends over farther water.
        for (int i = _visible.Count - 1; i >= 0; i--)
        {
            ChunkMesh? mesh = _visible[i].Chunk.Mesh;
            if (mesh?.Water is null) continue;
            shader.SetUniform("uChunkOrigin", _visible[i].Chunk.Origin);
            Renderer.DrawIndexed(mesh.Water, mesh.WaterIndexCount);
        }

        device.SetCapability(GraphicsCapability.Blend, false);
        Renderer.SetDepthWrite(true);
        Renderer.SetFaceCulling(true);
    }

    private void DrawParticles(RenderContext context)
    {
        DayNightCycle? cycle = DayNightCycle.Current;
        if (_particle is null || cycle is null || Particles.Count == 0) return;

        Matrix4x4.Invert(context.ViewProjection, out Matrix4x4 inverse);
        Vector3 right = Vector3.Normalize(Unproject(inverse, 1, 0) - Unproject(inverse, -1, 0));
        Vector3 up = Vector3.Normalize(Unproject(inverse, 0, 1) - Unproject(inverse, 0, -1));
        Vector3 light = cycle.AmbientSky * 0.9f + cycle.LightColor * 0.45f + new Vector3(0.03f);

        _particle.Use();
        SetSky(_particle, cycle);
        _particle.SetUniform("uCameraPos", context.CameraPosition);
        SetFog(_particle);
        Renderer.Device.BindTexture(0, _atlas);
        _particle.SetUniform("uAtlas", 0);
        Renderer.SetFaceCulling(false);
        Particles.Draw(_particle, context.ViewProjection, right, up, light);
        Renderer.SetFaceCulling(true);
    }

    private void DrawOutline(RenderContext context)
    {
        if (_line is null || PlayerController.Current is not { Target: { } hit } || Game.Current is { HudHidden: true })
        {
            return;
        }

        if (_outlineArray is null)
        {
            _outlineArray = new VertexArray();
            _outlineBuffer = new VertexBuffer((uint)_outline.Length, ShaderDataType.Float3);
            _outlineArray.AddVertexBuffer(_outlineBuffer);
        }

        Bounds(hit.Block, out Vector3 min, out Vector3 max);
        Vector3 origin = new(hit.Position.X, hit.Position.Y, hit.Position.Z);
        const float grow = 0.004f;
        min = origin + min - new Vector3(grow);
        max = origin + max + new Vector3(grow);
        int i = 0;
        void Edge(Vector3 a, Vector3 b)
        {
            _outline[i++] = a.X; _outline[i++] = a.Y; _outline[i++] = a.Z;
            _outline[i++] = b.X; _outline[i++] = b.Y; _outline[i++] = b.Z;
        }

        for (int k = 0; k < 4; k++)
        {
            float x0 = (k & 1) == 0 ? min.X : max.X;
            float z0 = (k & 2) == 0 ? min.Z : max.Z;
            Edge(new Vector3(x0, min.Y, z0), new Vector3(x0, max.Y, z0));
        }

        foreach (float y in new[] { min.Y, max.Y })
        {
            Edge(new Vector3(min.X, y, min.Z), new Vector3(max.X, y, min.Z));
            Edge(new Vector3(max.X, y, min.Z), new Vector3(max.X, y, max.Z));
            Edge(new Vector3(max.X, y, max.Z), new Vector3(min.X, y, max.Z));
            Edge(new Vector3(min.X, y, max.Z), new Vector3(min.X, y, min.Z));
        }

        _outlineBuffer!.SetData(_outline);
        IGraphicsDevice device = Renderer.Device;
        _line.Use();
        _line.SetUniform("uViewProjection", context.ViewProjection);
        bool night = DayNightCycle.Current is { Daylight: < 0.3f };
        _line.SetUniform("uColor", night ? new Vector4(0.8f, 0.8f, 0.8f, 0.5f) : new Vector4(0.0f, 0.0f, 0.0f, 0.55f));
        device.SetCapability(GraphicsCapability.Blend, true);
        device.SetBlendFunc(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);
        Renderer.SetDepthWrite(false);
        _outlineArray.Bind();
        device.DrawArrays(PrimitiveKind.Lines, 0, 24);
        Renderer.SetDepthWrite(true);
        device.SetCapability(GraphicsCapability.Blend, false);
    }

    /// <summary>The selection box of a block, in its own cell.</summary>
    public static void Bounds(BlockId id, out Vector3 min, out Vector3 max)
    {
        if (Blocks.Shape(id) == BlockShape.Plant)
        {
            min = new Vector3(0.15f, 0.0f, 0.15f);
            max = new Vector3(0.85f, 0.8f, 0.85f);
            return;
        }

        min = Vector3.Zero;
        max = Vector3.One;
    }

    // ---- shadows ----

    private void RenderShadowMap(World world, in RenderContext context, DayNightCycle cycle)
    {
        int resolution = Math.Clamp(ShadowResolution, 256, 4096);
        if (_shadowMap is null || _shadowMap.Width != (uint)resolution)
        {
            _shadowMap?.Dispose();
            _shadowMap = new DepthFramebuffer((uint)resolution, (uint)resolution);
        }

        // An orthographic view down the light, around the camera, snapped to whole texels so the shadows don't
        // shimmer as the camera moves.
        Vector3 light = Vector3.Normalize(cycle.LightDirection);
        Vector3 up = MathF.Abs(light.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        Matrix4x4 view = Matrix4x4.CreateLookAt(Vector3.Zero, -light, up);
        float range = ShadowDistance;
        Vector3 center = Vector3.Transform(context.CameraPosition, view);
        float texel = range * 2.0f / resolution;
        center.X = MathF.Floor(center.X / texel) * texel;
        center.Y = MathF.Floor(center.Y / texel) * texel;
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(
            center.X - range, center.X + range, center.Y - range, center.Y + range, -center.Z - 260.0f, -center.Z + 260.0f);
        _lightSpace = view * projection;
        _shadowCenter = context.CameraPosition;

        FramebufferHandle target = Renderer.CurrentRenderTarget;
        int x = Renderer.ViewportX;
        int y = Renderer.ViewportY;
        uint width = Renderer.ViewportWidth;
        uint height = Renderer.ViewportHeight;

        _shadowMap.Bind();
        Renderer.ClearDepth();
        Renderer.SetDepthTest(true);
        Renderer.SetDepthWrite(true);
        Renderer.SetFaceCulling(false);

        Shader shader = _shadow!;
        shader.Use();
        shader.SetUniform("uViewProjection", _lightSpace);
        shader.SetUniform("uTime", _time);
        Renderer.Device.BindTexture(0, _atlas);
        shader.SetUniform("uAtlas", 0);

        Vector3 camera = context.CameraPosition;
        float reach = range + 24.0f;
        foreach (Chunk chunk in world.Chunks)
        {
            if (chunk.Mesh?.Opaque is not { } array) continue;
            float cx = chunk.X * Chunk.Size + 8.0f - camera.X;
            float cz = chunk.Z * Chunk.Size + 8.0f - camera.Z;
            if (MathF.Abs(cx) > reach || MathF.Abs(cz) > reach) continue;
            shader.SetUniform("uChunkOrigin", chunk.Origin);
            Renderer.DrawIndexed(array, chunk.Mesh.OpaqueIndexCount);
        }

        Renderer.SetFaceCulling(true);
        Renderer.BindRenderTarget(target, x, y, width, height);
        _shadowReady = true;
    }

    // ---- shared state ----

    private void CollectVisible(World world, in RenderContext context)
    {
        _visible.Clear();
        var frustum = new Frustum(context.ViewProjection);
        Vector3 camera = context.CameraPosition;
        foreach (Chunk chunk in world.Chunks)
        {
            ChunkMesh? mesh = chunk.Mesh;
            if (mesh is null || mesh.MaxY <= mesh.MinY) continue;
            Vector3 min = chunk.Origin + new Vector3(0.0f, mesh.MinY - 1.0f, 0.0f);
            Vector3 max = chunk.Origin + new Vector3(Chunk.Size, mesh.MaxY + 1.0f, Chunk.Size);
            if (!frustum.Intersects(new Aabb3d((min + max) * 0.5f, max - min))) continue;
            Vector3 c = (min + max) * 0.5f;
            _visible.Add((chunk, Vector2.DistanceSquared(new Vector2(c.X, c.Z), new Vector2(camera.X, camera.Z))));
        }

        _visible.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        VisibleChunks = _visible.Count;
    }

    private void SetCommon(Shader shader, in RenderContext context, DayNightCycle cycle, bool textures)
    {
        shader.SetUniform("uViewProjection", context.ViewProjection);
        shader.SetUniform("uCameraPos", context.CameraPosition);
        shader.SetUniform("uTime", _time);
        SetSky(shader, cycle);
        shader.SetUniform("uLightDir", Vector3.Normalize(cycle.LightDirection));
        shader.SetUniform("uLightColor", cycle.LightColor);
        shader.SetUniform("uAmbientSky", cycle.AmbientSky);
        shader.SetUniform("uAmbientHorizon", cycle.AmbientHorizon);
        shader.SetUniform("uUnderwater", _underwater ? 1.0f : 0.0f);
        SetFog(shader);

        if (textures)
        {
            Renderer.Device.BindTexture(0, _atlas);
            shader.SetUniform("uAtlas", 0);
            Renderer.Device.BindTexture(1, _tintMask);
            shader.SetUniform("uTintMask", 1);
        }

        shader.SetUniform("uShadowEnabled", _shadowReady ? 1.0f : 0.0f);
        if (_shadowMap is not null)
        {
            _shadowMap.BindDepthTexture(2);
            shader.SetUniform("uShadowMap", 2);
            shader.SetUniform("uShadowTexel", new Vector2(1.0f / _shadowMap.Width));
        }
        else
        {
            // Keep the shadow sampler off unit 0, where the atlas (a color texture) is bound.
            shader.SetUniform("uShadowMap", 2);
        }

        shader.SetUniform("uLightSpace", _lightSpace);
        shader.SetUniform("uShadowRange", ShadowDistance);
        shader.SetUniform("uShadowCenter", _shadowCenter);
    }

    private static void SetSky(Shader shader, DayNightCycle cycle)
    {
        shader.SetUniform("uSunDir", cycle.SunDirection);
        shader.SetUniform("uZenith", cycle.Zenith);
        shader.SetUniform("uHorizon", cycle.Horizon);
        shader.SetUniform("uTwilight", cycle.Twilight);
        shader.SetUniform("uDaylight", cycle.Daylight);
    }

    private static void SetFog(Shader shader)
    {
        float end = MathF.Max(32.0f, (VoxelWorld.Current?.World?.RenderDistance ?? 8) * Chunk.Size - 10.0f);
        shader.SetUniform("uFogEnd", end);
        shader.SetUniform("uFogStart", end * 0.62f);
    }

    private static Vector3 Unproject(in Matrix4x4 inverse, float x, float y)
    {
        Vector4 p = Vector4.Transform(new Vector4(x, y, 0.5f, 1.0f), inverse);
        return new Vector3(p.X, p.Y, p.Z) / p.W;
    }

    private static TextureHandle CreateTexture(byte[] pixels, TextureFilter magnify)
    {
        IGraphicsDevice device = Renderer.Device;
        TextureHandle handle = device.CreateTexture();
        device.BindTexture(0, handle);
        device.SetTextureWrap(TextureWrap.ClampToEdge);
        device.SetTextureFilter(TextureFilter.LinearMipmapLinear, magnify);
        device.TextureImage2DRgba8(BlockAtlas.Size, BlockAtlas.Size, pixels);
        device.GenerateMipmap2D();
        return handle;
    }

    private static Shader? Compile(string name, string vertex, string fragment)
    {
        var shader = new Shader(vertex, fragment);
        if (shader.IsValid) return shader;

        Log.Error("Voxelcraft: the {0} shader failed to compile; it will not be drawn.\n{1}", name, shader.ErrorLog);
        shader.Dispose();
        return null;
    }
}

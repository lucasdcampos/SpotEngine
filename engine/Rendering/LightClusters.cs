using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Spot.Rendering;

/// <summary>
/// Builds and uploads the per-frame light cluster grid ("froxels") used by the clustered forward lighting
/// path. The view frustum is diced into a <see cref="ClustersX"/>×<see cref="ClustersY"/>×<see cref="ClustersZ"/>
/// grid — screen tiles in x/y, an exponential radial-distance slice in z — and every point light is assigned
/// to the froxels its range sphere reaches. Two integer lookup textures carry the result to the shader: a
/// <b>grid</b> texture (one packed <c>offset·count</c> per froxel) and a flat <b>light-index</b> list. A
/// fragment then loops over just its froxel's lights instead of all of them.
/// </summary>
/// <remarks>
/// The froxel a point falls in is defined identically here and in the shader: the screen tile from
/// <c>gl_FragCoord</c> / screen size, and the radial slice from the point's distance to the camera between the
/// recovered near and far planes. Assignment is deliberately conservative (it projects each light's enclosing
/// cube, never under-covering), so a fragment may occasionally test a light just outside its froxel — correct,
/// only slightly less optimal. Everything derives from the view-projection, its inverse, and the camera
/// position, so no extra camera plumbing is needed; the path bows out (returning <see langword="false"/> from
/// <see cref="Build"/>) for orthographic or degenerate cameras.
/// </remarks>
internal sealed class LightClusters
{
    // Grid dimensions. Must match the CLUSTERS_* / *_TEX_W constants in the fragment shader source.
    public const int ClustersX = 16;
    public const int ClustersY = 9;
    public const int ClustersZ = 24;
    public const int ClusterCount = ClustersX * ClustersY * ClustersZ; // 3456

    // The grid texture is ClusterCount texels laid out GridTexWidth wide. 64 divides 3456 evenly (54 rows).
    public const int GridTexWidth = 64;
    public const int GridTexHeight = ClusterCount / GridTexWidth; // 54

    // The flat light-index list texture. Capacity is width*height entries; overflow is dropped (and logged).
    public const int IndexTexWidth = 256;
    public const int IndexTexHeight = 64;
    public const int MaxIndices = IndexTexWidth * IndexTexHeight; // 16384

    // Per-froxel count is packed into the low 8 bits of a grid texel, so it can't exceed this.
    private const int MaxLightsPerFroxel = 255;

    // Texture units the grid and index list are bound to (0 albedo, 1 shadow, 2 normal are taken).
    public const uint GridTextureUnit = 3;
    public const uint IndexTextureUnit = 4;

    // Reused CPU buffers (cleared, not reallocated, each frame).
    private readonly uint[] _grid = new uint[GridTexWidth * GridTexHeight];
    private readonly uint[] _indices = new uint[MaxIndices];
    private readonly List<int>[] _froxelLights = new List<int>[ClusterCount];

    // GPU textures are created lazily on first Build, so the (pure) CPU assignment can be unit-tested
    // without a graphics device.
    private IGraphicsDevice? _device;
    private TextureHandle _gridTexture;
    private TextureHandle _indexTexture;
    private bool _texturesReady;
    private bool _overflowLogged;

    public LightClusters()
    {
        for (int i = 0; i < _froxelLights.Length; i++)
        {
            _froxelLights[i] = new List<int>();
        }
    }

    /// <summary>The packed grid buffer, exposed for tests.</summary>
    internal ReadOnlySpan<uint> Grid => _grid;

    /// <summary>The flat light-index buffer, exposed for tests.</summary>
    internal ReadOnlySpan<uint> Indices => _indices;

    /// <summary>The near plane distance recovered from the camera this build, for the shader's slice math.</summary>
    public float Near { get; private set; }

    /// <summary>The far plane distance recovered from the camera this build.</summary>
    public float Far { get; private set; }

    /// <summary>
    /// Assigns the given point lights to froxels and uploads the grid and index textures. Returns
    /// <see langword="false"/> without uploading when the camera is orthographic or degenerate (the caller
    /// then uses the brute-force light loop). Pure-CPU assignment is factored into <see cref="Assign"/>.
    /// </summary>
    public bool Build(Matrix4x4 viewProjection, Matrix4x4 invViewProjection, Vector3 cameraPos,
        float screenWidth, float screenHeight, ReadOnlySpan<Renderer3D.PointLightData> lights)
    {
        if (!Assign(viewProjection, invViewProjection, cameraPos, lights))
        {
            return false;
        }

        EnsureTextures();

        // Both lookups are nearest-sampled integer textures; re-upload the whole (small) images each frame.
        _device!.BindTexture(GridTextureUnit, _gridTexture);
        _device.TextureImage2D(TextureInternalFormat.R32UI, (uint)GridTexWidth, (uint)GridTexHeight, MemoryMarshal.AsBytes(_grid.AsSpan()));
        _device.BindTexture(IndexTextureUnit, _indexTexture);
        _device.TextureImage2D(TextureInternalFormat.R32UI, (uint)IndexTexWidth, (uint)IndexTexHeight, MemoryMarshal.AsBytes(_indices.AsSpan()));
        return true;
    }

    /// <summary>
    /// Fills <see cref="_grid"/> and <see cref="_indices"/> from the lights, without touching the GPU (so it
    /// is unit-testable). Returns <see langword="false"/> for a non-perspective or degenerate camera.
    /// </summary>
    public bool Assign(Matrix4x4 viewProjection, Matrix4x4 invViewProjection, Vector3 cameraPos,
        ReadOnlySpan<Renderer3D.PointLightData> lights)
    {
        if (!RecoverNearFar(invViewProjection, cameraPos, out float near, out float far))
        {
            return false;
        }

        Near = near;
        Far = far;

        for (int i = 0; i < _froxelLights.Length; i++)
        {
            _froxelLights[i].Clear();
        }

        float logFarNear = MathF.Log(far / near);

        for (int li = 0; li < lights.Length; li++)
        {
            Vector3 center = lights[li].Position;
            float radius = lights[li].Range;
            if (radius <= 0f)
            {
                continue;
            }

            // Screen (tile) extent from the projected enclosing cube; radial slice extent from the sphere.
            if (!ProjectSphereToTiles(viewProjection, cameraPos, center, radius,
                    out int tileMinX, out int tileMaxX, out int tileMinY, out int tileMaxY))
            {
                // The sphere straddles the camera — cover the whole screen for its slice span.
                tileMinX = 0; tileMaxX = ClustersX - 1;
                tileMinY = 0; tileMaxY = ClustersY - 1;
            }

            float dist = Vector3.Distance(cameraPos, center);
            int sliceMin = SliceOf(dist - radius, near, logFarNear);
            int sliceMax = SliceOf(dist + radius, near, logFarNear);

            for (int z = sliceMin; z <= sliceMax; z++)
            {
                int zBase = z * ClustersX * ClustersY;
                for (int y = tileMinY; y <= tileMaxY; y++)
                {
                    int yBase = zBase + y * ClustersX;
                    for (int x = tileMinX; x <= tileMaxX; x++)
                    {
                        List<int> cell = _froxelLights[yBase + x];
                        if (cell.Count < MaxLightsPerFroxel)
                        {
                            cell.Add(li);
                        }
                    }
                }
            }
        }

        Flatten();
        return true;
    }

    /// <summary>Binds the grid and index textures to their units, so the shader's usamplers stay valid even
    /// when clustering is off. A no-op until the first <see cref="Build"/> has created the textures.</summary>
    public void Bind()
    {
        if (!_texturesReady)
        {
            return;
        }

        _device!.BindTexture(GridTextureUnit, _gridTexture);
        _device.BindTexture(IndexTextureUnit, _indexTexture);
    }

    // Packs each froxel's light list into the flat index array and writes its (offset, count) into the grid.
    private void Flatten()
    {
        int cursor = 0;
        for (int f = 0; f < ClusterCount; f++)
        {
            List<int> cell = _froxelLights[f];
            int count = cell.Count;
            if (cursor + count > MaxIndices)
            {
                count = Math.Max(0, MaxIndices - cursor);
                if (!_overflowLogged)
                {
                    Spot.Core.Log.CoreWarn("Light cluster index list overflowed ({0} slots); some lights dropped from distant froxels.", MaxIndices);
                    _overflowLogged = true;
                }
            }

            for (int k = 0; k < count; k++)
            {
                _indices[cursor + k] = (uint)cell[k];
            }

            // Packed as (offset << 8) | count, matching the shader's unpack.
            _grid[f] = ((uint)cursor << 8) | (uint)count;
            cursor += count;
        }

        // Grid texels beyond ClusterCount (padding rows) read as empty.
        for (int f = ClusterCount; f < _grid.Length; f++)
        {
            _grid[f] = 0;
        }
    }

    private static int SliceOf(float distance, float near, float logFarNear)
    {
        float d = MathF.Max(distance, near);
        int slice = (int)(MathF.Log(d / near) / logFarNear * ClustersZ);
        return Math.Clamp(slice, 0, ClustersZ - 1);
    }

    // Projects the light's enclosing cube (center ± radius) to screen tiles. Returns false when any corner is
    // at/behind the camera, in which case the caller falls back to the full screen for safety.
    private static bool ProjectSphereToTiles(Matrix4x4 viewProjection, Vector3 cameraPos, Vector3 center, float radius,
        out int tileMinX, out int tileMaxX, out int tileMinY, out int tileMaxY)
    {
        _ = cameraPos;
        float ndcMinX = float.MaxValue, ndcMinY = float.MaxValue;
        float ndcMaxX = float.MinValue, ndcMaxY = float.MinValue;

        for (int c = 0; c < 8; c++)
        {
            var corner = new Vector3(
                center.X + ((c & 1) == 0 ? -radius : radius),
                center.Y + ((c & 2) == 0 ? -radius : radius),
                center.Z + ((c & 4) == 0 ? -radius : radius));

            Vector4 clip = Vector4.Transform(new Vector4(corner, 1f), viewProjection);
            if (clip.W <= 1e-4f)
            {
                tileMinX = tileMaxX = tileMinY = tileMaxY = 0;
                return false;
            }

            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;
            ndcMinX = MathF.Min(ndcMinX, ndcX);
            ndcMaxX = MathF.Max(ndcMaxX, ndcX);
            ndcMinY = MathF.Min(ndcMinY, ndcY);
            ndcMaxY = MathF.Max(ndcMaxY, ndcY);
        }

        tileMinX = TileOf(ndcMinX, ClustersX);
        tileMaxX = TileOf(ndcMaxX, ClustersX);
        tileMinY = TileOf(ndcMinY, ClustersY);
        tileMaxY = TileOf(ndcMaxY, ClustersY);
        return true;
    }

    private static int TileOf(float ndc, int count)
    {
        float frac = ndc * 0.5f + 0.5f;
        int tile = (int)(frac * count);
        return Math.Clamp(tile, 0, count - 1);
    }

    // Recovers near/far plane distances and rejects orthographic/degenerate cameras (where the near and far
    // planes are the same size, so radial clustering doesn't apply).
    private static bool RecoverNearFar(Matrix4x4 invViewProjection, Vector3 cameraPos, out float near, out float far)
    {
        near = 0f;
        far = 0f;

        // System.Numerics projections use a [0, 1] depth clip range (near plane at NDC z = 0, far at z = 1).
        if (!Unproject(invViewProjection, 0f, 0f, 0f, out Vector3 nearCenter) ||
            !Unproject(invViewProjection, 0f, 0f, 1f, out Vector3 farCenter) ||
            !Unproject(invViewProjection, -1f, 0f, 0f, out Vector3 nearLeft) ||
            !Unproject(invViewProjection, 1f, 0f, 0f, out Vector3 nearRight) ||
            !Unproject(invViewProjection, -1f, 0f, 1f, out Vector3 farLeft) ||
            !Unproject(invViewProjection, 1f, 0f, 1f, out Vector3 farRight))
        {
            return false;
        }

        near = Vector3.Distance(cameraPos, nearCenter);
        far = Vector3.Distance(cameraPos, farCenter);
        if (!float.IsFinite(near) || !float.IsFinite(far) || near <= 1e-4f || far <= near)
        {
            return false;
        }

        // Perspective frustums widen with depth; an orthographic one keeps a constant width, so bail out of
        // clustering (the shader's radial-distance slicing assumes perspective).
        float nearWidth = Vector3.Distance(nearLeft, nearRight);
        float farWidth = Vector3.Distance(farLeft, farRight);
        return farWidth > nearWidth * 1.05f;
    }

    private static bool Unproject(Matrix4x4 invViewProjection, float ndcX, float ndcY, float ndcZ, out Vector3 world)
    {
        Vector4 h = Vector4.Transform(new Vector4(ndcX, ndcY, ndcZ, 1f), invViewProjection);
        if (MathF.Abs(h.W) <= 1e-6f)
        {
            world = default;
            return false;
        }

        world = new Vector3(h.X, h.Y, h.Z) / h.W;
        return true;
    }

    private void EnsureTextures()
    {
        if (_texturesReady)
        {
            return;
        }

        _device = Renderer.Device;
        _gridTexture = CreateLookupTexture(GridTexWidth, GridTexHeight);
        _indexTexture = CreateLookupTexture(IndexTexWidth, IndexTexHeight);
        _texturesReady = true;
    }

    private TextureHandle CreateLookupTexture(int width, int height)
    {
        TextureHandle handle = _device!.CreateTexture();
        _device.BindTexture(0, handle);
        _device.TextureImage2D(TextureInternalFormat.R32UI, (uint)width, (uint)height, ReadOnlySpan<byte>.Empty);
        // Integer textures cannot be linearly filtered; nearest + clamp is required.
        _device.SetTextureFilter(TextureFilter.Nearest, TextureFilter.Nearest);
        _device.SetTextureWrap(TextureWrap.ClampToEdge);
        return handle;
    }
}

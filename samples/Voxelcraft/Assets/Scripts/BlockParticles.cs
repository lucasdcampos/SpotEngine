using System.Numerics;
using Spot.Framework.Graphics;

namespace Voxelcraft;

/// <summary>
/// The little chips a block breaks into: bits of its own texture that tumble, bounce on the ground and fade.
/// Simulated on the CPU against the world and drawn as camera-facing quads in one dynamic buffer.
/// </summary>
public sealed class BlockParticles : IDisposable
{
    private const int MaxParticles = 600;
    private const int FloatsPerVertex = 9;

    private struct Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector4 Uv;
        public Vector3 Color;
        public float Size;
        public float Life;
    }

    private readonly Particle[] _particles = new Particle[MaxParticles];
    private readonly float[] _vertices = new float[MaxParticles * 4 * FloatsPerVertex];
    private readonly Random _random = new();
    private int _count;
    private VertexArray? _array;
    private VertexBuffer? _buffer;
    private IndexBuffer? _indices;

    public int Count => _count;

    /// <summary>Bursts a block into chips.</summary>
    public void Burst(BlockPos block, BlockId id, Vector3 foliage)
    {
        BlockInfo info = Blocks.Get(id);
        bool plant = info.Shape == BlockShape.Plant;
        int tile = info.Side;
        Vector3 tint = info.Tinted ? foliage : Vector3.One;
        for (int i = 0; i < (plant ? 10 : 26) && _count < MaxParticles; i++)
        {
            var offset = new Vector3((float)_random.NextDouble(), (float)_random.NextDouble() * (plant ? 0.7f : 1.0f), (float)_random.NextDouble());
            Vector3 position = new Vector3(block.X, block.Y, block.Z) + offset;
            Vector3 outward = offset - new Vector3(0.5f, 0.3f, 0.5f);
            int tx = _random.Next(13);
            int ty = _random.Next(13);
            float u0 = ((tile % BlockAtlas.Columns) * 16 + tx) / (float)BlockAtlas.Size;
            float v0 = ((tile / BlockAtlas.Columns) * 16 + ty) / (float)BlockAtlas.Size;
            _particles[_count++] = new Particle
            {
                Position = position,
                Velocity = outward * 3.2f + new Vector3(0.0f, 2.2f + (float)_random.NextDouble() * 1.5f, 0.0f),
                Uv = new Vector4(u0, v0, u0 + 3.0f / BlockAtlas.Size, v0 + 3.0f / BlockAtlas.Size),
                Color = tint,
                Size = 0.07f + (float)_random.NextDouble() * 0.06f,
                Life = 0.6f + (float)_random.NextDouble() * 0.7f,
            };
        }
    }

    public void Update(World world, float deltaTime)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            ref Particle p = ref _particles[i];
            p.Life -= deltaTime;
            if (p.Life <= 0.0f)
            {
                _particles[i] = _particles[--_count];
                continue;
            }

            p.Velocity.Y -= 22.0f * deltaTime;
            Vector3 next = p.Position + p.Velocity * deltaTime;
            if (Blocks.IsSolid(world.GetBlock(BlockPos.Floor(next with { Y = next.Y - p.Size }))))
            {
                // Settle on the ground with a little skid.
                next.Y = MathF.Floor(next.Y - p.Size) + 1.0f + p.Size;
                p.Velocity = new Vector3(p.Velocity.X * 0.6f, 0.0f, p.Velocity.Z * 0.6f);
            }

            if (Blocks.IsSolid(world.GetBlock(BlockPos.Floor(new Vector3(next.X, p.Position.Y, p.Position.Z))))) { next.X = p.Position.X; p.Velocity.X = 0.0f; }
            if (Blocks.IsSolid(world.GetBlock(BlockPos.Floor(new Vector3(p.Position.X, p.Position.Y, next.Z))))) { next.Z = p.Position.Z; p.Velocity.Z = 0.0f; }
            p.Position = next;
        }
    }

    public void Draw(Shader shader, in Matrix4x4 viewProjection, Vector3 right, Vector3 up, Vector3 light)
    {
        if (_count == 0) return;
        EnsureBuffers();
        if (_array is null || _buffer is null) return;

        int v = 0;
        for (int i = 0; i < _count; i++)
        {
            ref Particle p = ref _particles[i];
            float size = p.Size * MathF.Min(1.0f, p.Life * 4.0f);
            Vector3 r = right * size;
            Vector3 u = up * size;
            Vector3 c = p.Color * light;
            Put(ref v, p.Position - r - u, p.Uv.X, p.Uv.W, c);
            Put(ref v, p.Position + r - u, p.Uv.Z, p.Uv.W, c);
            Put(ref v, p.Position + r + u, p.Uv.Z, p.Uv.Y, c);
            Put(ref v, p.Position - r + u, p.Uv.X, p.Uv.Y, c);
        }

        _buffer.SetData(_vertices.AsSpan(0, v));
        shader.SetUniform("uViewProjection", viewProjection);
        Renderer.DrawIndexed(_array, (uint)_count * 6);
    }

    private void Put(ref int v, Vector3 position, float u, float t, Vector3 color)
    {
        float[] d = _vertices;
        d[v] = position.X;
        d[v + 1] = position.Y;
        d[v + 2] = position.Z;
        d[v + 3] = u;
        d[v + 4] = t;
        d[v + 5] = color.X;
        d[v + 6] = color.Y;
        d[v + 7] = color.Z;
        d[v + 8] = 1.0f;
        v += FloatsPerVertex;
    }

    private void EnsureBuffers()
    {
        if (_array is not null) return;

        // The vertex array first, so its index buffer binds into it.
        _array = new VertexArray();
        _buffer = new VertexBuffer((uint)_vertices.Length, ShaderDataType.Float3, ShaderDataType.Float2, ShaderDataType.Float4);
        _array.AddVertexBuffer(_buffer);
        var indices = new uint[MaxParticles * 6];
        for (int q = 0; q < MaxParticles; q++)
        {
            uint b = (uint)q * 4;
            indices[q * 6] = b;
            indices[q * 6 + 1] = b + 1;
            indices[q * 6 + 2] = b + 2;
            indices[q * 6 + 3] = b + 2;
            indices[q * 6 + 4] = b + 3;
            indices[q * 6 + 5] = b;
        }

        _indices = new IndexBuffer(indices);
        _array.SetIndexBuffer(_indices);
    }

    public void Dispose()
    {
        _array?.Dispose();
        _buffer?.Dispose();
        _indices?.Dispose();
        _array = null;
    }
}

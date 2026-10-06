using System.Numerics;

namespace Spot.Engine.Graphics;

/// <summary>
/// Generates the geometry of procedural primitives — cube, sphere, capsule, cylinder, cone, plane and quad — as
/// CPU <see cref="MeshData"/> (no GPU work, safe on any thread). Turn it into a drawable mesh with
/// <see cref="PrimitiveModelFactory"/>, or with the <see cref="Mesh"/> constructor.
/// </summary>
/// <remarks>
/// Every primitive is centered on the origin, wound counter-clockwise when seen from outside (so it survives
/// back-face culling), has unit normals, and maps its texture coordinates in <c>[0, 1]</c> without mirroring:
/// seen from outside, <c>u</c> runs right and <c>v</c> runs up. Curved shapes are smooth-shaded and repeat their
/// seam column so the texture wraps once around them.
/// </remarks>
public static class PrimitiveMeshes
{
    /// <summary>Generates the geometry a spec describes.</summary>
    /// <param name="spec">The primitive; normalized first.</param>
    /// <returns>The CPU mesh.</returns>
    public static MeshData Build(PrimitiveSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        PrimitiveSpec s = spec.Normalize();
        return s.Shape switch
        {
            PrimitiveShape.Cube => Cube(new Vector3(s.Width, s.Height, s.Depth)),
            PrimitiveShape.Sphere => Sphere(s.Radius, s.Segments, s.Rings),
            PrimitiveShape.Capsule => Capsule(s.Radius, s.Height, s.Segments, s.Rings),
            PrimitiveShape.Cylinder => Cylinder(s.Radius, s.Height, s.Segments),
            PrimitiveShape.Cone => Cone(s.Radius, s.Height, s.Segments),
            PrimitiveShape.Plane => Plane(new Vector2(s.Width, s.Depth), s.Subdivisions),
            PrimitiveShape.Quad => Quad(new Vector2(s.Width, s.Height)),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), s.Shape, "Unknown primitive shape."),
        };
    }

    /// <summary>Generates a box. Each face maps the whole texture.</summary>
    /// <param name="size">The size along X, Y and Z.</param>
    /// <returns>The CPU mesh.</returns>
    public static MeshData Cube(Vector3 size)
    {
        var builder = new MeshBuilder();
        Vector3 half = size * 0.5f;

        // Each face: its outward normal, then the directions u and v run along it (u × v = normal).
        (Vector3 Normal, Vector3 U, Vector3 V)[] faces =
        {
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),
            (Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),
            (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ),
            (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
        };

        foreach ((Vector3 normal, Vector3 u, Vector3 v) in faces)
        {
            Vector3 center = normal * Vector3.Dot(Vector3.Abs(normal), half);
            Vector3 du = u * Vector3.Dot(Vector3.Abs(u), half);
            Vector3 dv = v * Vector3.Dot(Vector3.Abs(v), half);
            builder.Quad(
                builder.Vertex(center - du - dv, normal, new Vector2(0, 0)),
                builder.Vertex(center + du - dv, normal, new Vector2(1, 0)),
                builder.Vertex(center + du + dv, normal, new Vector2(1, 1)),
                builder.Vertex(center - du + dv, normal, new Vector2(0, 1)));
        }

        return builder.Build();
    }

    /// <summary>Generates a flat rectangle on the XY plane, facing +Z.</summary>
    /// <param name="size">The width (X) and height (Y).</param>
    /// <returns>The CPU mesh.</returns>
    public static MeshData Quad(Vector2 size)
    {
        var builder = new MeshBuilder();
        Vector2 half = size * 0.5f;
        builder.Quad(
            builder.Vertex(new Vector3(-half.X, -half.Y, 0), Vector3.UnitZ, new Vector2(0, 0)),
            builder.Vertex(new Vector3(half.X, -half.Y, 0), Vector3.UnitZ, new Vector2(1, 0)),
            builder.Vertex(new Vector3(half.X, half.Y, 0), Vector3.UnitZ, new Vector2(1, 1)),
            builder.Vertex(new Vector3(-half.X, half.Y, 0), Vector3.UnitZ, new Vector2(0, 1)));
        return builder.Build();
    }

    /// <summary>
    /// Generates a flat grid on the XZ plane, facing up. The texture spans the whole plane once (seen from above,
    /// <c>u</c> runs along +X and <c>v</c> along −Z).
    /// </summary>
    /// <param name="size">The width (X) and depth (Z).</param>
    /// <param name="subdivisions">The cells along each side.</param>
    /// <returns>The CPU mesh.</returns>
    public static MeshData Plane(Vector2 size, int subdivisions)
    {
        int n = Math.Max(1, subdivisions);
        var builder = new MeshBuilder();
        Vector2 half = size * 0.5f;
        for (int row = 0; row <= n; row++)
        {
            for (int column = 0; column <= n; column++)
            {
                float u = (float)column / n;
                float v = (float)row / n;
                builder.Vertex(new Vector3(-half.X + size.X * u, 0, half.Y - size.Y * v), Vector3.UnitY, new Vector2(u, v));
            }
        }

        for (int row = 0; row < n; row++)
        {
            for (int column = 0; column < n; column++)
            {
                uint a = (uint)(row * (n + 1) + column);
                uint d = a + (uint)(n + 1);
                builder.Quad(a, a + 1, d + 1, d);
            }
        }

        return builder.Build();
    }

    /// <summary>Generates a UV sphere.</summary>
    /// <param name="radius">The radius.</param>
    /// <param name="segments">The divisions around the vertical axis.</param>
    /// <param name="rings">The latitude bands from pole to pole.</param>
    /// <returns>The CPU mesh.</returns>
    public static MeshData Sphere(float radius, int segments, int rings)
    {
        int bands = Math.Max(2, rings);
        var profile = new List<ProfilePoint>(bands + 1);
        for (int i = 0; i <= bands; i++)
        {
            float latitude = MathF.PI / 2 - MathF.PI * i / bands;
            profile.Add(ProfilePoint.OnArc(radius, 0, latitude, 1 - (float)i / bands));
        }

        var builder = new MeshBuilder();
        builder.Lathe(profile, segments);
        return builder.Build();
    }

    /// <summary>
    /// Generates a capsule: a cylinder capped by two hemispheres. The texture's <c>v</c> follows the length of the
    /// profile, so the caps and the side share one continuous mapping.
    /// </summary>
    /// <param name="radius">The radius of the cylinder and caps.</param>
    /// <param name="height">The total height, caps included; at least <c>2 × radius</c>.</param>
    /// <param name="segments">The divisions around the vertical axis.</param>
    /// <param name="rings">The latitude bands in each hemisphere.</param>
    /// <returns>The CPU mesh.</returns>
    public static MeshData Capsule(float radius, float height, int segments, int rings)
    {
        int bands = Math.Max(1, rings);
        float halfCylinder = MathF.Max(0, height * 0.5f - radius);
        float quarterArc = MathF.PI * 0.5f * radius;
        float length = 2 * quarterArc + 2 * halfCylinder;

        var profile = new List<ProfilePoint>(2 * bands + 2);
        for (int i = 0; i <= bands; i++)
        {
            float t = (float)i / bands;
            profile.Add(ProfilePoint.OnArc(radius, halfCylinder, MathF.PI / 2 * (1 - t), 1 - quarterArc * t / length));
        }

        for (int i = 0; i <= bands; i++)
        {
            float t = (float)i / bands;
            float travelled = quarterArc + 2 * halfCylinder + quarterArc * t;
            profile.Add(ProfilePoint.OnArc(radius, -halfCylinder, -MathF.PI / 2 * t, 1 - travelled / length));
        }

        var builder = new MeshBuilder();
        builder.Lathe(profile, segments);
        return builder.Build();
    }

    /// <summary>Generates a cylinder with flat caps.</summary>
    /// <param name="radius">The radius.</param>
    /// <param name="height">The height.</param>
    /// <param name="segments">The divisions around the vertical axis.</param>
    /// <returns>The CPU mesh.</returns>
    public static MeshData Cylinder(float radius, float height, int segments)
    {
        float half = height * 0.5f;
        var builder = new MeshBuilder();
        builder.Lathe(
            new[]
            {
                new ProfilePoint(radius, half, new Vector2(1, 0), 1),
                new ProfilePoint(radius, -half, new Vector2(1, 0), 0),
            },
            segments);
        builder.Disc(radius, half, up: true, segments);
        builder.Disc(radius, -half, up: false, segments);
        return builder.Build();
    }

    /// <summary>Generates a cone, apex up, with a flat base.</summary>
    /// <param name="radius">The base radius.</param>
    /// <param name="height">The height from base to apex.</param>
    /// <param name="segments">The divisions around the vertical axis.</param>
    /// <returns>The CPU mesh.</returns>
    public static MeshData Cone(float radius, float height, int segments)
    {
        float half = height * 0.5f;

        // The side's outward normal leans up by the slope: perpendicular to the slant (radius out, height down).
        Vector2 slope = Vector2.Normalize(new Vector2(height, radius));
        var builder = new MeshBuilder();
        builder.Lathe(
            new[]
            {
                new ProfilePoint(0, half, slope, 1),
                new ProfilePoint(radius, -half, slope, 0),
            },
            segments);
        builder.Disc(radius, -half, up: false, segments);
        return builder.Build();
    }

    // A point of a surface of revolution's profile: its distance from the axis, height, outward normal in the
    // (radial, up) plane, and texture v.
    private readonly record struct ProfilePoint(float Radius, float Y, Vector2 Normal, float V)
    {
        // A point on a circular arc of the given radius centered at (0, centerY), at a latitude in radians.
        public static ProfilePoint OnArc(float radius, float centerY, float latitude, float v)
        {
            float cos = MathF.Cos(latitude);
            float sin = MathF.Sin(latitude);

            // Snap the poles so their vertices coincide exactly and the pole triangles collapse cleanly.
            if (MathF.Abs(cos) < 1e-6f)
            {
                cos = 0;
                sin = MathF.Sign(sin);
            }

            return new ProfilePoint(radius * cos, centerY + radius * sin, new Vector2(cos, sin), v);
        }
    }

    // Accumulates interleaved vertices (position, normal, uv) and triangles. Triangles are oriented by the
    // vertex normals, so callers only list the corners; degenerate ones (collapsed at a pole or an apex) are
    // dropped.
    private sealed class MeshBuilder
    {
        private readonly List<float> _vertices = new();
        private readonly List<uint> _indices = new();

        public uint Vertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            uint index = (uint)(_vertices.Count / Mesh.FloatsPerVertex);
            _vertices.Add(position.X);
            _vertices.Add(position.Y);
            _vertices.Add(position.Z);
            _vertices.Add(normal.X);
            _vertices.Add(normal.Y);
            _vertices.Add(normal.Z);
            _vertices.Add(uv.X);
            _vertices.Add(uv.Y);
            return index;
        }

        public void Quad(uint a, uint b, uint c, uint d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        public void Triangle(uint a, uint b, uint c)
        {
            Vector3 pa = Position(a);
            Vector3 e1 = Position(b) - pa;
            Vector3 e2 = Position(c) - pa;
            Vector3 face = Vector3.Cross(e1, e2);
            if (face.LengthSquared() <= 1e-12f * e1.LengthSquared() * e2.LengthSquared())
            {
                return;
            }

            bool outward = Vector3.Dot(face, Normal(a) + Normal(b) + Normal(c)) >= 0;
            _indices.Add(a);
            _indices.Add(outward ? b : c);
            _indices.Add(outward ? c : b);
        }

        // Sweeps a profile (top to bottom) around the Y axis. Seen from outside, u runs right (the angle grows
        // from +X toward −Z) and v comes from the profile.
        public void Lathe(IReadOnlyList<ProfilePoint> profile, int segments)
        {
            int columns = Math.Max(3, segments);
            uint first = (uint)(_vertices.Count / Mesh.FloatsPerVertex);
            foreach (ProfilePoint point in profile)
            {
                for (int column = 0; column <= columns; column++)
                {
                    float u = (float)column / columns;
                    (float sin, float cos) = AngleAt(column, columns);
                    var around = new Vector3(cos, 0, -sin);
                    Vector3 normal = Vector3.Normalize(around * point.Normal.X + Vector3.UnitY * point.Normal.Y);
                    Vertex(around * point.Radius + new Vector3(0, point.Y, 0), normal, new Vector2(u, point.V));
                }
            }

            uint stride = (uint)(columns + 1);
            for (int row = 0; row < profile.Count - 1; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    uint topLeft = first + (uint)row * stride + (uint)column;
                    uint bottomLeft = topLeft + stride;
                    Quad(bottomLeft, bottomLeft + 1, topLeft + 1, topLeft);
                }
            }
        }

        // A flat disc cap at height y, facing up or down, mapped like the plane seen from its outside.
        public void Disc(float radius, float y, bool up, int segments)
        {
            int columns = Math.Max(3, segments);
            Vector3 normal = up ? Vector3.UnitY : -Vector3.UnitY;
            uint center = Vertex(new Vector3(0, y, 0), normal, new Vector2(0.5f, 0.5f));
            uint first = center + 1;
            for (int column = 0; column <= columns; column++)
            {
                (float sin, float cos) = AngleAt(column, columns);
                float v = up ? 0.5f + 0.5f * sin : 0.5f - 0.5f * sin;
                Vertex(new Vector3(radius * cos, y, -radius * sin), normal, new Vector2(0.5f + 0.5f * cos, v));
            }

            for (int column = 0; column < columns; column++)
            {
                Triangle(center, first + (uint)column, first + (uint)column + 1);
            }
        }

        public MeshData Build() => new(_vertices.ToArray(), _indices.ToArray());

        // The seam column repeats the first exactly, so the surface closes without a crack.
        private static (float Sin, float Cos) AngleAt(int column, int columns) =>
            column == columns ? (0, 1) : MathF.SinCos(2 * MathF.PI * column / columns);

        private Vector3 Position(uint index)
        {
            int i = (int)index * Mesh.FloatsPerVertex;
            return new Vector3(_vertices[i], _vertices[i + 1], _vertices[i + 2]);
        }

        private Vector3 Normal(uint index)
        {
            int i = (int)index * Mesh.FloatsPerVertex + 3;
            return new Vector3(_vertices[i], _vertices[i + 1], _vertices[i + 2]);
        }
    }
}

using System;
using System.Collections.Generic;
using Spot.Rendering;

namespace Spot.Assets;

public static class PrimitiveModelFactory
{
    public static Model Create(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "cube": return CreateCube();
            case "plane": return CreatePlane();
            case "quad": return CreateQuad();
            case "sphere": return CreateSphere();
            default: throw new ArgumentException($"Unknown primitive: {name}");
        }
    }

    private static Model CreateCube()
    {
        float[] vertices = {
            // Front face (Z = 0.5f)
            -0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  0.0f, 0.0f,
             0.5f, -0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  1.0f, 0.0f,
             0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  1.0f, 1.0f,
            -0.5f,  0.5f,  0.5f,  0.0f,  0.0f,  1.0f,  0.0f, 1.0f,

            // Back face (Z = -0.5f)
             0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  0.0f, 0.0f,
            -0.5f, -0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  1.0f, 0.0f,
            -0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  1.0f, 1.0f,
             0.5f,  0.5f, -0.5f,  0.0f,  0.0f, -1.0f,  0.0f, 1.0f,

            // Left face (X = -0.5f)
            -0.5f, -0.5f, -0.5f, -1.0f,  0.0f,  0.0f,  0.0f, 0.0f,
            -0.5f, -0.5f,  0.5f, -1.0f,  0.0f,  0.0f,  1.0f, 0.0f,
            -0.5f,  0.5f,  0.5f, -1.0f,  0.0f,  0.0f,  1.0f, 1.0f,
            -0.5f,  0.5f, -0.5f, -1.0f,  0.0f,  0.0f,  0.0f, 1.0f,

            // Right face (X = 0.5f)
             0.5f, -0.5f,  0.5f,  1.0f,  0.0f,  0.0f,  0.0f, 0.0f,
             0.5f, -0.5f, -0.5f,  1.0f,  0.0f,  0.0f,  1.0f, 0.0f,
             0.5f,  0.5f, -0.5f,  1.0f,  0.0f,  0.0f,  1.0f, 1.0f,
             0.5f,  0.5f,  0.5f,  1.0f,  0.0f,  0.0f,  0.0f, 1.0f,

            // Top face (Y = 0.5f)
            -0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f,  0.0f, 0.0f,
             0.5f,  0.5f,  0.5f,  0.0f,  1.0f,  0.0f,  1.0f, 0.0f,
             0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f,  1.0f, 1.0f,
            -0.5f,  0.5f, -0.5f,  0.0f,  1.0f,  0.0f,  0.0f, 1.0f,

            // Bottom face (Y = -0.5f)
            -0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,  0.0f, 0.0f,
             0.5f, -0.5f, -0.5f,  0.0f, -1.0f,  0.0f,  1.0f, 0.0f,
             0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,  1.0f, 1.0f,
            -0.5f, -0.5f,  0.5f,  0.0f, -1.0f,  0.0f,  0.0f, 1.0f,
        };

        uint[] indices = {
             0,  1,  2,  2,  3,  0,
             4,  5,  6,  6,  7,  4,
             8,  9, 10, 10, 11,  8,
            12, 13, 14, 14, 15, 12,
            16, 17, 18, 18, 19, 16,
            20, 21, 22, 22, 23, 20
        };

        var mesh = new Mesh(vertices, indices);
        return new Model(new[] { mesh });
    }

    private static Model CreateQuad()
    {
        float[] vertices = {
            -0.5f, -0.5f, 0.0f,  0.0f, 0.0f, 1.0f,  0.0f, 0.0f,
             0.5f, -0.5f, 0.0f,  0.0f, 0.0f, 1.0f,  1.0f, 0.0f,
             0.5f,  0.5f, 0.0f,  0.0f, 0.0f, 1.0f,  1.0f, 1.0f,
            -0.5f,  0.5f, 0.0f,  0.0f, 0.0f, 1.0f,  0.0f, 1.0f
        };
        uint[] indices = { 0, 1, 2, 2, 3, 0 };
        var mesh = new Mesh(vertices, indices);
        return new Model(new[] { mesh });
    }

    private static Model CreatePlane()
    {
        int segments = 10;
        float size = 10.0f;
        float halfSize = size / 2.0f;
        float step = size / segments;
        float uvStep = 1.0f / segments;

        var vertices = new List<float>();
        var indices = new List<uint>();

        for (int z = 0; z <= segments; z++)
        {
            for (int x = 0; x <= segments; x++)
            {
                vertices.Add(-halfSize + x * step); // px
                vertices.Add(0.0f);                 // py
                vertices.Add(-halfSize + z * step); // pz

                vertices.Add(0.0f);                 // nx
                vertices.Add(1.0f);                 // ny
                vertices.Add(0.0f);                 // nz

                vertices.Add(x * uvStep);           // u
                vertices.Add(z * uvStep);           // v
            }
        }

        for (int z = 0; z < segments; z++)
        {
            for (int x = 0; x < segments; x++)
            {
                uint tl = (uint)(z * (segments + 1) + x);
                uint tr = tl + 1;
                uint bl = (uint)((z + 1) * (segments + 1) + x);
                uint br = bl + 1;

                indices.Add(tl);
                indices.Add(bl);
                indices.Add(tr);

                indices.Add(tr);
                indices.Add(bl);
                indices.Add(br);
            }
        }

        var mesh = new Mesh(vertices.ToArray(), indices.ToArray());
        return new Model(new[] { mesh });
    }

    private static Model CreateSphere()
    {
        float radius = 0.5f;
        int sectorCount = 32;
        int stackCount = 16;
        var vertices = new List<float>();
        var indices = new List<uint>();

        float x, y, z, xy;
        float nx, ny, nz, lengthInv = 1.0f / radius;
        float s, t;

        float sectorStep = 2.0f * MathF.PI / sectorCount;
        float stackStep = MathF.PI / stackCount;
        float sectorAngle, stackAngle;

        for (int i = 0; i <= stackCount; ++i)
        {
            stackAngle = MathF.PI / 2.0f - i * stackStep;
            xy = radius * MathF.Cos(stackAngle);
            y = radius * MathF.Sin(stackAngle);

            for (int j = 0; j <= sectorCount; ++j)
            {
                sectorAngle = j * sectorStep;

                x = xy * MathF.Cos(sectorAngle);
                z = xy * MathF.Sin(sectorAngle);
                vertices.Add(x);
                vertices.Add(y);
                vertices.Add(z);

                nx = x * lengthInv;
                ny = y * lengthInv;
                nz = z * lengthInv;
                vertices.Add(nx);
                vertices.Add(ny);
                vertices.Add(nz);

                s = (float)j / sectorCount;
                t = (float)i / stackCount;
                vertices.Add(s);
                vertices.Add(t);
            }
        }

        int k1, k2;
        for (int i = 0; i < stackCount; ++i)
        {
            k1 = i * (sectorCount + 1);
            k2 = k1 + sectorCount + 1;

            for (int j = 0; j < sectorCount; ++j, ++k1, ++k2)
            {
                if (i != 0)
                {
                    indices.Add((uint)k1);
                    indices.Add((uint)k2);
                    indices.Add((uint)(k1 + 1));
                }

                if (i != (stackCount - 1))
                {
                    indices.Add((uint)(k1 + 1));
                    indices.Add((uint)k2);
                    indices.Add((uint)(k2 + 1));
                }
            }
        }

        var mesh = new Mesh(vertices.ToArray(), indices.ToArray());
        return new Model(new[] { mesh });
    }
}

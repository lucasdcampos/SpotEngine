using System.Globalization;
using System.Text;

namespace Spot.Framework.Graphics;

/// <summary>
/// Writes CPU geometry out to interchange formats, so generated meshes can be edited in a modeling tool or kept
/// as ordinary model files.
/// </summary>
public static class MeshExport
{
    /// <summary>
    /// Writes a mesh as Wavefront OBJ text: positions, texture coordinates, normals and triangles. Skinning data is
    /// not written.
    /// </summary>
    /// <param name="mesh">The mesh.</param>
    /// <param name="name">An object name to record, or <see langword="null"/>.</param>
    /// <returns>The OBJ text.</returns>
    public static string ToObj(MeshData mesh, string? name = null)
    {
        int stride = mesh.Skinned ? Mesh.SkinnedFloatsPerVertex : Mesh.FloatsPerVertex;
        float[] v = mesh.Vertices;
        int count = v.Length / stride;
        CultureInfo invariant = CultureInfo.InvariantCulture;

        var obj = new StringBuilder();
        obj.Append("# Exported by Spot\n");
        if (!string.IsNullOrWhiteSpace(name))
        {
            obj.Append("o ").Append(name.Trim()).Append('\n');
        }

        for (int i = 0; i < count; i++)
        {
            int o = i * stride;
            obj.Append(invariant, $"v {v[o]:R} {v[o + 1]:R} {v[o + 2]:R}\n");
        }

        for (int i = 0; i < count; i++)
        {
            int o = i * stride;
            obj.Append(invariant, $"vt {v[o + 6]:R} {v[o + 7]:R}\n");
        }

        for (int i = 0; i < count; i++)
        {
            int o = i * stride;
            obj.Append(invariant, $"vn {v[o + 3]:R} {v[o + 4]:R} {v[o + 5]:R}\n");
        }

        uint[] indices = mesh.Indices;
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            // OBJ indices are 1-based; position, texture coordinate and normal share an index here.
            uint a = indices[t] + 1, b = indices[t + 1] + 1, c = indices[t + 2] + 1;
            obj.Append(invariant, $"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}\n");
        }

        return obj.ToString();
    }
}

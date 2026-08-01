using System;
using System.IO;
using System.Numerics;
using Silk.NET.Assimp;
using Spot.Core;
using AssimpApi = Silk.NET.Assimp.Assimp;
using RenderMesh = Spot.Rendering.Mesh;

namespace Spot.Assets;

/// <summary>
/// Imports 3D models through the Assimp library, which handles many common formats
/// (OBJ, FBX, glTF/GLB, Collada, PLY, STL, and more).
/// </summary>
/// <remarks>
/// Only geometry is imported for now — positions, normals and the first texture-coordinate channel.
/// Materials, textures and scene hierarchy are ignored until the material/lighting systems land.
/// Companion files (for example an OBJ's .mtl, or a glTF's .bin) are not tracked here.
/// </remarks>
public sealed unsafe class AssimpModelImporter : IModelImporter
{
    private static readonly AssimpApi s_assimp = AssimpApi.GetApi();

    private static readonly string[] s_extensions =
    {
        ".obj", ".fbx", ".gltf", ".glb", ".dae", ".ply", ".stl"
    };

    /// <inheritdoc />
    public IEnumerable<string> SupportedExtensions => s_extensions;

    /// <inheritdoc />
    public Model Import(string path)
    {
        const uint flags = (uint)(
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.JoinIdenticalVertices);

        Scene* scene = s_assimp.ImportFile(path, flags);
        if (scene == null || scene->MRootNode == null)
        {
            throw new InvalidOperationException($"Failed to import model '{path}': {s_assimp.GetErrorStringS()}");
        }

        try
        {
            var meshes = new List<RenderMesh>((int)scene->MNumMeshes);
            for (uint i = 0; i < scene->MNumMeshes; i++)
            {
                meshes.Add(BuildMesh(scene->MMeshes[i]));
            }

            return new Model(meshes);
        }
        finally
        {
            s_assimp.ReleaseImport(scene);
        }
    }

    private static RenderMesh BuildMesh(Mesh* mesh)
    {
        uint vertexCount = mesh->MNumVertices;
        var vertices = new float[vertexCount * RenderMesh.FloatsPerVertex];

        if (mesh->MTextureCoords[0] == null)
        {
            Log.CoreWarn("Imported mesh has no texture coordinates (UVs); a material texture will show as a flat color.");
        }

        int cursor = 0;
        for (uint i = 0; i < vertexCount; i++)
        {
            Vector3 position = mesh->MVertices[i];
            vertices[cursor++] = position.X;
            vertices[cursor++] = position.Y;
            vertices[cursor++] = position.Z;

            Vector3 normal = mesh->MNormals != null ? mesh->MNormals[i] : Vector3.Zero;
            vertices[cursor++] = normal.X;
            vertices[cursor++] = normal.Y;
            vertices[cursor++] = normal.Z;

            // Assimp stores up to 8 UV channels; use the first when present.
            if (mesh->MTextureCoords[0] != null)
            {
                Vector3 texCoord = mesh->MTextureCoords[0][i];
                vertices[cursor++] = texCoord.X;
                vertices[cursor++] = texCoord.Y;
            }
            else
            {
                vertices[cursor++] = 0.0f;
                vertices[cursor++] = 0.0f;
            }
        }

        var indices = new List<uint>((int)(mesh->MNumFaces * 3));
        for (uint f = 0; f < mesh->MNumFaces; f++)
        {
            Face face = mesh->MFaces[f];
            for (uint j = 0; j < face.MNumIndices; j++)
            {
                indices.Add(face.MIndices[j]);
            }
        }

        return new RenderMesh(vertices, indices.ToArray());
    }

    /// <summary>
    /// Parses the model file and extracts any embedded textures to the same directory, generating 
    /// a corresponding .sptmat material file for each.
    /// </summary>
    public static void ExtractMaterials(string path)
    {
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string modelName = Path.GetFileNameWithoutExtension(path);

        const uint flags = 0;
        Scene* scene = s_assimp.ImportFile(path, flags);
        if (scene == null)
        {
            Log.CoreError($"Failed to read file for extraction '{path}': {s_assimp.GetErrorStringS()}");
            return;
        }

        try
        {
            for (uint i = 0; i < scene->MNumTextures; i++)
            {
                Texture* tex = scene->MTextures[i];
                if (tex->MHeight == 0) // Compressed texture (PNG, JPG, etc)
                {
                    string texName = tex->MFilename.AsString;
                    if (string.IsNullOrWhiteSpace(texName) || texName.StartsWith("*"))
                    {
                        texName = $"{modelName}_Texture_{i}";
                    }
                    else
                    {
                        texName = Path.GetFileNameWithoutExtension(texName);
                    }
                    
                    // Sanitize name
                    foreach (char c in Path.GetInvalidFileNameChars())
                        texName = texName.Replace(c, '_');

                    string imagePath = Path.Combine(directory, texName + ".png");
                    
                    int byteCount = (int)tex->MWidth;
                    if (byteCount > 0)
                    {
                        byte[] data = new byte[byteCount];
                        fixed (byte* pData = data)
                        {
                            System.Buffer.MemoryCopy(tex->PcData, pData, byteCount, byteCount);
                        }
                        System.IO.File.WriteAllBytes(imagePath, data);
                        Log.CoreInfo($"Extracted embedded texture: {imagePath}");

                        // Create a corresponding material
                        string matPath = Path.Combine(directory, texName + ".sptmat");
                        if (!System.IO.File.Exists(matPath))
                        {
                            var mat = new Material();
                            mat.SetTexture(imagePath);
                            mat.Save(matPath);
                            Log.CoreInfo($"Created material: {matPath}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.CoreError($"Error extracting materials from '{path}': {ex.Message}");
        }
        finally
        {
            s_assimp.ReleaseImport(scene);
        }
    }
}

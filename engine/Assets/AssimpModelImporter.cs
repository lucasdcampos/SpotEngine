using System;
using System.IO;
using System.Numerics;
using Silk.NET.Assimp;
using Spot.Core;
using AssimpApi = Silk.NET.Assimp.Assimp;
using AiMaterial = Silk.NET.Assimp.Material;
using RenderMesh = Spot.Rendering.Mesh;
using MeshData = Spot.Rendering.MeshData;
using File = System.IO.File;

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
    public IReadOnlyList<MeshData> ImportMeshData(string path)
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
            var meshes = new List<MeshData>((int)scene->MNumMeshes);
            for (uint i = 0; i < scene->MNumMeshes; i++)
            {
                meshes.Add(BuildMeshData(scene->MMeshes[i]));
            }

            return meshes;
        }
        finally
        {
            s_assimp.ReleaseImport(scene);
        }
    }

    /// <inheritdoc />
    public Model Import(string path)
    {
        IReadOnlyList<MeshData> data = ImportMeshData(path);
        var meshes = new List<RenderMesh>(data.Count);
        foreach (MeshData md in data)
        {
            meshes.Add(new RenderMesh(md.Vertices, md.Indices));
        }

        return new Model(meshes);
    }

    /// <summary>
    /// Reads a model's scene graph — its node hierarchy, the submeshes hanging off each node, and the
    /// material slot each submesh uses — without uploading anything to the GPU. The submesh ordering
    /// matches <see cref="ImportMeshData"/> (and therefore the cooked <c>.spmesh</c>) exactly, because it
    /// imports with the same post-processing flags, so a node's mesh indices double as
    /// <see cref="Scenes.MeshComponent.SubmeshIndex"/> values.
    /// </summary>
    /// <param name="path">The path to the model file.</param>
    /// <returns>The model's scene graph description.</returns>
    public ModelSceneInfo ImportSceneInfo(string path)
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
            var meshMaterialIndex = new int[(int)scene->MNumMeshes];
            for (uint i = 0; i < scene->MNumMeshes; i++)
            {
                meshMaterialIndex[i] = (int)scene->MMeshes[i]->MMaterialIndex;
            }

            ModelNodeInfo root = BuildNode(scene->MRootNode);
            return new ModelSceneInfo(root, meshMaterialIndex);
        }
        finally
        {
            s_assimp.ReleaseImport(scene);
        }
    }

    private static ModelNodeInfo BuildNode(Node* node)
    {
        string name = node->MName.AsString;
        if (string.IsNullOrEmpty(name))
        {
            name = "Node";
        }

        // Assimp stores row-major matrices with the translation in the fourth column; System.Numerics uses
        // the fourth row (row-vector convention). The value marshaled straight across is therefore the
        // transpose of the engine-convention matrix, so transpose it back here.
        Matrix4x4 local = Matrix4x4.Transpose(node->MTransformation);

        var meshIndices = new int[(int)node->MNumMeshes];
        for (uint i = 0; i < node->MNumMeshes; i++)
        {
            meshIndices[i] = (int)node->MMeshes[i];
        }

        var children = new List<ModelNodeInfo>((int)node->MNumChildren);
        for (uint i = 0; i < node->MNumChildren; i++)
        {
            children.Add(BuildNode(node->MChildren[i]));
        }

        return new ModelNodeInfo(name, local, meshIndices, children);
    }

    private static MeshData BuildMeshData(Mesh* mesh)
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

        return new MeshData(vertices, indices.ToArray());
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

    /// <summary>
    /// Extracts one <c>.sptmat</c> per material slot in the model into <paramref name="outDir"/>, pulling
    /// each slot's base color and base-color/diffuse texture (embedded textures are written out as PNGs,
    /// referenced ones are resolved next to the model). Existing <c>.sptmat</c> files are kept as-is. This
    /// is the material half of dropping a model into the scene: it produces assets that
    /// <see cref="Scenes.ModelInstantiator"/> assigns to the matching mesh parts.
    /// </summary>
    /// <param name="modelPath">The path to the source model file.</param>
    /// <param name="outDir">The directory the <c>.sptmat</c> files (and any extracted textures) are written to.</param>
    /// <returns>A map from material slot index to the written <c>.sptmat</c> path.</returns>
    public static IReadOnlyDictionary<int, string> ExtractMaterialsPerSlot(string modelPath, string outDir)
    {
        var result = new Dictionary<int, string>();

        Scene* scene = s_assimp.ImportFile(modelPath, 0);
        if (scene == null)
        {
            Log.CoreError($"Failed to read file for material extraction '{modelPath}': {s_assimp.GetErrorStringS()}");
            return result;
        }

        try
        {
            Directory.CreateDirectory(outDir);
            string modelDir = Path.GetDirectoryName(modelPath) ?? string.Empty;

            for (uint i = 0; i < scene->MNumMaterials; i++)
            {
                try
                {
                    AiMaterial* mat = scene->MMaterials[i];
                    string safeName = Sanitize(GetMaterialName(mat, i));
                    string matPath = Path.Combine(outDir, safeName + ".sptmat");

                    if (File.Exists(matPath))
                    {
                        result[(int)i] = matPath;
                        continue;
                    }

                    var material = new Material();

                    // Base color: prefer the PBR base-color factor, fall back to the legacy diffuse color.
                    if (TryGetColor(mat, AssimpApi.MatkeyBaseColor, out Vector4 color) ||
                        TryGetColor(mat, AssimpApi.MatkeyColorDiffuse, out color))
                    {
                        material.Color = color;
                    }

                    // Base texture: prefer base color, fall back to diffuse.
                    string? texturePath =
                        ResolveTexture(scene, mat, TextureType.BaseColor, modelDir, outDir, safeName) ??
                        ResolveTexture(scene, mat, TextureType.Diffuse, modelDir, outDir, safeName);
                    if (texturePath != null)
                    {
                        material.SetTexture(texturePath);
                    }

                    material.Save(matPath);
                    result[(int)i] = matPath;
                    Log.CoreInfo($"Created material: {matPath}");
                }
                catch (Exception ex)
                {
                    Log.CoreError($"Failed to extract material slot {i} from '{modelPath}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.CoreError($"Error extracting materials from '{modelPath}': {ex.Message}");
        }
        finally
        {
            s_assimp.ReleaseImport(scene);
        }

        return result;
    }

    private static string GetMaterialName(AiMaterial* mat, uint index)
    {
        AssimpString name = default;
        if (s_assimp.GetMaterialString(mat, AssimpApi.MatkeyName, 0, 0, ref name) == Return.Success)
        {
            string value = name.AsString;
            if (!string.IsNullOrWhiteSpace(value) && value != AssimpApi.DefaultMaterialName)
            {
                return value;
            }
        }

        return $"Material_{index}";
    }

    private static bool TryGetColor(AiMaterial* mat, string key, out Vector4 color)
    {
        Vector4 value = default;
        if (s_assimp.GetMaterialColor(mat, key, 0, 0, ref value) == Return.Success)
        {
            color = value;
            return true;
        }

        color = Vector4.One;
        return false;
    }

    private static string? ResolveTexture(Scene* scene, AiMaterial* mat, TextureType type, string modelDir, string outDir, string safeName)
    {
        if (s_assimp.GetMaterialTextureCount(mat, type) == 0)
        {
            return null;
        }

        AssimpString path = default;
        if (s_assimp.GetMaterialTexture(mat, type, 0, &path, null, null, null, null, null, null) != Return.Success)
        {
            return null;
        }

        string reference = path.AsString;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (reference.StartsWith("*", StringComparison.Ordinal))
        {
            // Embedded texture: "*N" indexes into the scene's texture table.
            if (int.TryParse(reference.AsSpan(1), out int texIndex) && texIndex >= 0 && texIndex < scene->MNumTextures)
            {
                string outPath = Path.Combine(outDir, safeName + ".png");
                if (WriteEmbeddedTexture(scene->MTextures[texIndex], outPath))
                {
                    return outPath;
                }
            }

            return null;
        }

        // Referenced texture: resolve relative to the model's own directory.
        string resolved = Path.IsPathRooted(reference) ? reference : Path.Combine(modelDir, reference);
        return File.Exists(resolved) ? resolved : null;
    }

    private static bool WriteEmbeddedTexture(Texture* tex, string outPath)
    {
        if (tex->MHeight != 0)
        {
            // Uncompressed raw ARGB texture; the compressed (PNG/JPG) path is all we extract for now.
            return false;
        }

        int byteCount = (int)tex->MWidth;
        if (byteCount <= 0)
        {
            return false;
        }

        byte[] data = new byte[byteCount];
        fixed (byte* pData = data)
        {
            System.Buffer.MemoryCopy(tex->PcData, pData, byteCount, byteCount);
        }

        File.WriteAllBytes(outPath, data);
        Log.CoreInfo($"Extracted embedded texture: {outPath}");
        return true;
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "Material" : name;
    }
}

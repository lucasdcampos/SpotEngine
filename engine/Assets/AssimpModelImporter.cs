using System;
using System.IO;
using System.Numerics;
using Silk.NET.Assimp;
using Spot.Animation;
using Spot.Core;
using AssimpApi = Silk.NET.Assimp.Assimp;
using AiMaterial = Silk.NET.Assimp.Material;
using AiAnimation = Silk.NET.Assimp.Animation;
using RenderMesh = Spot.Rendering.Mesh;
using MeshData = Spot.Rendering.MeshData;
using NQuaternion = System.Numerics.Quaternion;
using File = System.IO.File;

namespace Spot.Assets;

/// <summary>
/// Imports 3D models through the Assimp library, which handles many common formats
/// (OBJ, FBX, glTF/GLB, Collada, PLY, STL, and more).
/// </summary>
/// <remarks>
/// Geometry (positions, normals, the first texture-coordinate channel), skinning (bones and weights) and
/// animation clips are imported. The node hierarchy is exposed separately through <see cref="ImportSceneInfo"/>.
/// Companion files (for example an OBJ's .mtl, or a glTF's .bin) are not tracked here.
/// </remarks>
public sealed unsafe class AssimpModelImporter : IModelImporter
{
    private static readonly AssimpApi s_assimp = AssimpApi.GetApi();

    // The post-processing steps applied to every import. All entry points (geometry, scene graph, full
    // model) MUST use the same flags so submesh ordering and vertex counts line up between them and with the
    // cooked .spmesh. LimitBoneWeights caps skinning influences at four (matching the shader) and normalizes
    // them.
    private const uint ImportFlags = (uint)(
        PostProcessSteps.Triangulate |
        PostProcessSteps.GenerateSmoothNormals |
        PostProcessSteps.JoinIdenticalVertices |
        PostProcessSteps.LimitBoneWeights);

    private static readonly string[] s_extensions =
    {
        ".obj", ".fbx", ".gltf", ".glb", ".dae", ".ply", ".stl"
    };

    /// <inheritdoc />
    public IEnumerable<string> SupportedExtensions => s_extensions;

    /// <inheritdoc />
    public IReadOnlyList<MeshData> ImportMeshData(string path) => ImportModel(path).Submeshes;

    /// <inheritdoc />
    public CookedModel ImportModel(string path)
    {
        Scene* scene = ImportScene(path);
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

            IReadOnlyList<AnimationClip> clips = ParseAnimations(scene, path);
            return new CookedModel(meshes, clips);
        }
        finally
        {
            s_assimp.ReleaseImport(scene);
        }
    }

    /// <inheritdoc />
    public Model Import(string path) => ModelImporter.BuildModel(ImportModel(path));

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
        Scene* scene = ImportScene(path);
        if (scene == null || scene->MRootNode == null)
        {
            throw new InvalidOperationException($"Failed to import model '{path}': {s_assimp.GetErrorStringS()}");
        }

        try
        {
            var meshMaterialIndex = new int[(int)scene->MNumMeshes];
            var meshSkinned = new bool[(int)scene->MNumMeshes];
            for (uint i = 0; i < scene->MNumMeshes; i++)
            {
                meshMaterialIndex[i] = (int)scene->MMeshes[i]->MMaterialIndex;
                meshSkinned[i] = scene->MMeshes[i]->MNumBones > 0;
            }

            var clipNames = new string[(int)scene->MNumAnimations];
            for (uint i = 0; i < scene->MNumAnimations; i++)
            {
                string clipName = scene->MAnimations[i]->MName.AsString;
                clipNames[i] = ResolveClipName(path, clipName, (int)i, (int)scene->MNumAnimations);
            }

            ModelNodeInfo root = BuildNode(scene->MRootNode);
            return new ModelSceneInfo(root, meshMaterialIndex, meshSkinned, clipNames);
        }
        finally
        {
            s_assimp.ReleaseImport(scene);
        }
    }

    // Imports a scene with the shared flags, with Assimp's FBX pivot nodes baked into the bones. Preserving
    // the $AssimpFbx$ pivot nodes (Assimp's default) splits a bone's transform across several helper nodes,
    // and that split differs between exports — so a clip authored against one file lands its hip translation on
    // a different node than the bind pose of another, stacking the two and lifting the character off the floor.
    // Collapsing the pivots yields one clean node per bone whose name and transform match across files, so
    // clips retarget cleanly (and the instantiated hierarchy is far smaller and readable).
    private static Scene* ImportScene(string path)
    {
        PropertyStore* props = s_assimp.CreatePropertyStore();
        s_assimp.SetImportPropertyInteger(props, "IMPORT_FBX_PRESERVE_PIVOTS", 0);
        Scene* scene = s_assimp.ImportFileExWithProperties(path, ImportFlags, null, props);
        s_assimp.ReleasePropertyStore(props);
        return scene;
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
        int vertexCount = (int)mesh->MNumVertices;
        bool skinned = mesh->MNumBones > 0;
        int floatsPerVertex = skinned ? RenderMesh.SkinnedFloatsPerVertex : RenderMesh.FloatsPerVertex;
        var vertices = new float[vertexCount * floatsPerVertex];

        for (int i = 0; i < vertexCount; i++)
        {
            int cursor = i * floatsPerVertex;

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

            // The bone-index (4) and bone-weight (4) slots stay zero here; FillSkinning fills them below.
        }

        IReadOnlyList<BoneInfo>? bones = skinned ? FillSkinning(mesh, vertices, vertexCount) : null;

        var indices = new List<uint>((int)(mesh->MNumFaces * 3));
        for (uint f = 0; f < mesh->MNumFaces; f++)
        {
            Face face = mesh->MFaces[f];
            for (uint j = 0; j < face.MNumIndices; j++)
            {
                indices.Add(face.MIndices[j]);
            }
        }

        return new MeshData(vertices, indices.ToArray(), skinned, bones);
    }

    // Fills the four bone-index and four bone-weight slots of each skinned vertex (already sized to the
    // 16-float layout) from the mesh's bones, keeping the four strongest influences and renormalizing.
    // Returns the submesh's bone list; a vertex bone index refers into it.
    private static IReadOnlyList<BoneInfo> FillSkinning(Mesh* mesh, float[] vertices, int vertexCount)
    {
        int boneCount = (int)mesh->MNumBones;
        var bones = new BoneInfo[boneCount];

        // Per-vertex accumulator: up to four (bone index, weight) influences.
        var influenceIndex = new int[vertexCount * 4];
        var influenceWeight = new float[vertexCount * 4];
        var influenceCount = new int[vertexCount];

        for (int b = 0; b < boneCount; b++)
        {
            Bone* bone = mesh->MBones[b];
            string name = bone->MName.AsString;
            if (string.IsNullOrEmpty(name))
            {
                name = $"Bone{b}";
            }

            // Assimp's offset matrix is row-major (translation in the fourth column); transpose it into the
            // engine's row-vector convention, matching BuildNode's handling of node transforms.
            bones[b] = new BoneInfo(name, Matrix4x4.Transpose(bone->MOffsetMatrix));

            for (uint w = 0; w < bone->MNumWeights; w++)
            {
                VertexWeight vw = bone->MWeights[w];
                int v = (int)vw.MVertexId;
                if (v < 0 || v >= vertexCount || vw.MWeight <= 0.0f)
                {
                    continue;
                }

                AddInfluence(influenceIndex, influenceWeight, influenceCount, v, b, vw.MWeight);
            }
        }

        int floatsPerVertex = RenderMesh.SkinnedFloatsPerVertex;
        for (int v = 0; v < vertexCount; v++)
        {
            float sum = 0.0f;
            for (int k = 0; k < 4; k++)
            {
                sum += influenceWeight[v * 4 + k];
            }

            float normalize = sum > 1e-6f ? 1.0f / sum : 0.0f;
            int boneSlot = v * floatsPerVertex + RenderMesh.FloatsPerVertex;
            for (int k = 0; k < 4; k++)
            {
                vertices[boneSlot + k] = influenceIndex[v * 4 + k];             // bone index stored as a float
                vertices[boneSlot + 4 + k] = influenceWeight[v * 4 + k] * normalize;
            }
        }

        return bones;
    }

    // Records one bone influence for a vertex, keeping only the four strongest (a heavier influence evicts
    // the lightest once four are present).
    private static void AddInfluence(int[] index, float[] weight, int[] count, int vertex, int boneIndex, float boneWeight)
    {
        int at = vertex * 4;
        if (count[vertex] < 4)
        {
            index[at + count[vertex]] = boneIndex;
            weight[at + count[vertex]] = boneWeight;
            count[vertex]++;
            return;
        }

        int lightest = 0;
        for (int k = 1; k < 4; k++)
        {
            if (weight[at + k] < weight[at + lightest])
            {
                lightest = k;
            }
        }

        if (boneWeight > weight[at + lightest])
        {
            index[at + lightest] = boneIndex;
            weight[at + lightest] = boneWeight;
        }
    }

    // Names a clip meaningfully. Assimp keeps the source name when it has one, but many exporters (Mixamo in
    // particular) name every clip "mixamo.com", which collides once several are merged onto one animator. In
    // that case fall back to the source file name — the way animation files are actually identified — so
    // idle.fbx becomes "idle" and a rig's own throwaway clip becomes the model's name.
    private static string ResolveClipName(string sourcePath, string rawName, int index, int count)
    {
        bool generic = string.IsNullOrEmpty(rawName) || rawName.Equals("mixamo.com", StringComparison.OrdinalIgnoreCase);
        if (!generic)
        {
            return rawName;
        }

        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "Clip";
        }

        return count > 1 ? $"{baseName} ({index + 1})" : baseName;
    }

    private static IReadOnlyList<AnimationClip> ParseAnimations(Scene* scene, string sourcePath)
    {
        if (scene->MNumAnimations == 0)
        {
            return Array.Empty<AnimationClip>();
        }

        var clips = new List<AnimationClip>((int)scene->MNumAnimations);
        for (uint i = 0; i < scene->MNumAnimations; i++)
        {
            AiAnimation* anim = scene->MAnimations[i];

            // Ticks per second is 0 in some exporters; fall back to a sane default so key times convert.
            double ticks = anim->MTicksPerSecond;
            if (ticks <= 0.0)
            {
                ticks = 25.0;
            }

            string name = ResolveClipName(sourcePath, anim->MName.AsString, (int)i, (int)scene->MNumAnimations);

            float duration = (float)(anim->MDuration / ticks);

            var channels = new List<AnimationChannel>((int)anim->MNumChannels);
            for (uint c = 0; c < anim->MNumChannels; c++)
            {
                NodeAnim* channel = anim->MChannels[c];
                string node = channel->MNodeName.AsString;

                var positionKeys = new Keyframe<Vector3>[channel->MNumPositionKeys];
                for (uint k = 0; k < channel->MNumPositionKeys; k++)
                {
                    VectorKey key = channel->MPositionKeys[k];
                    positionKeys[k] = new Keyframe<Vector3>((float)(key.MTime / ticks), key.MValue);
                }

                var rotationKeys = new Keyframe<NQuaternion>[channel->MNumRotationKeys];
                for (uint k = 0; k < channel->MNumRotationKeys; k++)
                {
                    QuatKey key = channel->MRotationKeys[k];
                    var q = key.MValue;
                    rotationKeys[k] = new Keyframe<NQuaternion>((float)(key.MTime / ticks), new NQuaternion(q.X, q.Y, q.Z, q.W));
                }

                var scaleKeys = new Keyframe<Vector3>[channel->MNumScalingKeys];
                for (uint k = 0; k < channel->MNumScalingKeys; k++)
                {
                    VectorKey key = channel->MScalingKeys[k];
                    scaleKeys[k] = new Keyframe<Vector3>((float)(key.MTime / ticks), key.MValue);
                }

                channels.Add(new AnimationChannel(node, positionKeys, rotationKeys, scaleKeys));
            }

            clips.Add(new AnimationClip(name, duration, channels));
        }

        return clips;
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

                        // Create a corresponding material
                        string matPath = Path.Combine(directory, texName + ".sptmat");
                        if (!System.IO.File.Exists(matPath))
                        {
                            var mat = new Material();
                            mat.SetTexture(imagePath);
                            mat.Save(matPath);
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

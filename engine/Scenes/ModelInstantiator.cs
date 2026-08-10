using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Spot.Animation;
using Spot.Assets;
using Spot.Core;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// Turns a model file (FBX, OBJ, glTF, ...) into a live entity hierarchy: it rebuilds the source's node
/// tree as parented entities — one per node, each with its local transform — and points every mesh part at
/// the shared model via <see cref="MeshComponent.SubmeshIndex"/>. Optionally it extracts the model's
/// materials and assigns the right one to each part. This is the engine side of dragging a model into the
/// scene; the editor panels just call <see cref="Instantiate"/>.
/// </summary>
/// <remarks>
/// The whole operation is CPU-only — it reads the model with Assimp and writes component data but never
/// touches the GPU (models and materials resolve lazily at draw time), so it is safe to call off the render
/// thread and can be unit-tested headlessly. Failures are logged and leave the scene unchanged.
/// </remarks>
public static class ModelInstantiator
{
    private static readonly AssimpModelImporter s_importer = new();
    private static readonly IReadOnlyDictionary<int, string> s_noMaterials = new Dictionary<int, string>();

    /// <summary>
    /// Instantiates <paramref name="modelPath"/> into <paramref name="scene"/> under an optional parent,
    /// returning the root entity of the created hierarchy (named after the file) or <see langword="null"/>
    /// if the model could not be read.
    /// </summary>
    /// <param name="scene">The scene to add the entities to.</param>
    /// <param name="modelPath">The path to the source model file.</param>
    /// <param name="parent">The entity to parent the new root under, or <see langword="null"/> for a root object.</param>
    /// <param name="extractMaterials">
    /// When <see langword="true"/> (the default), the model's materials are extracted next to it (into a
    /// <c>&lt;Model&gt;_Materials</c> folder) and assigned to the matching mesh parts.
    /// </param>
    /// <returns>The root entity of the instantiated hierarchy, or <see langword="null"/> on failure.</returns>
    public static Entity? Instantiate(Scene scene, string modelPath, Entity? parent = null, bool extractMaterials = true)
    {
        if (scene is null || string.IsNullOrWhiteSpace(modelPath))
        {
            return null;
        }

        try
        {
            IReadOnlyDictionary<int, string> materials = extractMaterials ? ExtractMaterials(modelPath) : s_noMaterials;
            ModelSceneInfo info = s_importer.ImportSceneInfo(modelPath);

            string rootName = Path.GetFileNameWithoutExtension(modelPath);
            Entity root = BuildNode(scene, info, info.Root, parent, modelPath, materials, rootName);
            AddAnimator(root, info, modelPath);
            return root;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to instantiate model '{0}': {1}", modelPath, ex.Message);
            return null;
        }
    }

    private static IReadOnlyDictionary<int, string> ExtractMaterials(string modelPath)
    {
        try
        {
            string dir = Path.GetDirectoryName(modelPath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(modelPath);
            string outDir = Path.Combine(dir, name + "_Materials");
            return AssimpModelImporter.ExtractMaterialsPerSlot(modelPath, outDir);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to extract materials for '{0}': {1}", modelPath, ex.Message);
            return s_noMaterials;
        }
    }

    private static Entity BuildNode(Scene scene, ModelSceneInfo info, ModelNodeInfo node, Entity? parent, string modelPath, IReadOnlyDictionary<int, string> materials, string name)
    {
        Entity entity = scene.Instantiate(name);
        entity.SetParent(parent);
        ApplyTransform(entity.GetComponent<TransformComponent>(), node.LocalTransform);

        if (node.MeshIndices.Count == 1)
        {
            AddMesh(entity, modelPath, node.MeshIndices[0], info, materials);
        }
        else if (node.MeshIndices.Count > 1)
        {
            // A node carrying several meshes becomes one child entity per part, so each keeps its own
            // material and submesh; the node entity itself stays a pure transform.
            for (int i = 0; i < node.MeshIndices.Count; i++)
            {
                Entity part = scene.Instantiate($"{name}_part{i}");
                part.SetParent(entity);
                AddMesh(part, modelPath, node.MeshIndices[i], info, materials);
            }
        }

        foreach (ModelNodeInfo child in node.Children)
        {
            string childName = string.IsNullOrEmpty(child.Name) ? "Node" : child.Name;
            BuildNode(scene, info, child, entity, modelPath, materials, childName);
        }

        return entity;
    }

    private static void AddMesh(Entity entity, string modelPath, int meshIndex, ModelSceneInfo info, IReadOnlyDictionary<int, string> materials)
    {
        var mesh = new MeshComponent
        {
            ModelPath = modelPath,
            SubmeshIndex = meshIndex,
        };

        if (meshIndex >= 0 && meshIndex < info.MeshMaterialIndex.Count &&
            materials.TryGetValue(info.MeshMaterialIndex[meshIndex], out string? materialPath))
        {
            mesh.MaterialPath = materialPath;
        }

        entity.AddComponent(mesh);

        // A skinned submesh gets a companion marker so the render system draws it through the skinning path,
        // posing it from the bone entities rather than this entity's transform.
        if (meshIndex >= 0 && meshIndex < info.MeshSkinned.Count && info.MeshSkinned[meshIndex])
        {
            entity.AddComponent(new SkinnedMeshComponent());
        }
    }

    // Adds an Animator to the root of a skinned/animated model. It anchors bone resolution (the skinned
    // meshes walk up to it) and plays the model's clips; even a rig with no clips gets one as the anchor.
    private static void AddAnimator(Entity root, ModelSceneInfo info, string modelPath)
    {
        bool skinned = false;
        for (int i = 0; i < info.MeshSkinned.Count; i++)
        {
            if (info.MeshSkinned[i])
            {
                skinned = true;
                break;
            }
        }

        if (!skinned && info.ClipNames.Count == 0)
        {
            return;
        }

        var animator = new AnimatorComponent { ModelPath = modelPath };
        if (info.ClipNames.Count > 0)
        {
            animator.DefaultClip = info.ClipNames[0];
            animator.PlayOnStart = true;
        }
        else
        {
            animator.PlayOnStart = false;
        }

        root.AddComponent(animator);
    }

    private static void ApplyTransform(TransformComponent transform, Matrix4x4 local)
    {
        if (!Matrix4x4.Decompose(local, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
        {
            // Skew or a mirrored (negative-determinant) transform can't be cleanly decomposed; keep the
            // translation and drop the un-representable rotation/scale rather than fail the whole import.
            translation = local.Translation;
            rotation = Quaternion.Identity;
            scale = Vector3.One;
        }

        transform.Position = translation;
        transform.Rotation = AnimationMath.ToEulerDegrees(rotation);
        transform.Scale = scale;
    }
}

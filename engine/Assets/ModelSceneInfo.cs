using System.Numerics;

namespace Spot.Assets;

/// <summary>
/// A single node of an imported model's scene graph: its name, its transform relative to its parent,
/// the submeshes attached to it, and its children. This is the lightweight, GPU-free description that
/// lets the editor rebuild a model as an entity hierarchy — one entity per node — instead of collapsing
/// every part onto a single object.
/// </summary>
/// <remarks>
/// Mesh indices are positions into the flat submesh list the importer produces (and that the cooked
/// <c>.spmesh</c> preserves in order), so a node's <see cref="MeshIndices"/> line up one-to-one with
/// <see cref="Assets.Model.Meshes"/> and with <see cref="Scenes.MeshComponent.SubmeshIndex"/>.
/// </remarks>
public sealed class ModelNodeInfo
{
    /// <summary>Initializes a node with its name, local transform, submeshes and children.</summary>
    public ModelNodeInfo(string name, Matrix4x4 localTransform, IReadOnlyList<int> meshIndices, IReadOnlyList<ModelNodeInfo> children)
    {
        Name = name;
        LocalTransform = localTransform;
        MeshIndices = meshIndices;
        Children = children;
    }

    /// <summary>Gets the node's name (from the source file), used to name the entity it becomes.</summary>
    public string Name { get; }

    /// <summary>Gets the node's transform relative to its parent, in engine (row-vector) convention.</summary>
    public Matrix4x4 LocalTransform { get; }

    /// <summary>Gets the indices of the submeshes attached to this node (into the flat submesh list).</summary>
    public IReadOnlyList<int> MeshIndices { get; }

    /// <summary>Gets the node's children.</summary>
    public IReadOnlyList<ModelNodeInfo> Children { get; }
}

/// <summary>
/// The scene-graph description of an imported model: the root node (with the whole hierarchy hanging off
/// it) plus, for every submesh, which material slot it uses. Produced by
/// <see cref="AssimpModelImporter.ImportSceneInfo"/> and consumed by <see cref="Scenes.ModelInstantiator"/>.
/// </summary>
public sealed class ModelSceneInfo
{
    /// <summary>Initializes the scene info with its root node and per-submesh material slots.</summary>
    public ModelSceneInfo(ModelNodeInfo root, IReadOnlyList<int> meshMaterialIndex)
    {
        Root = root;
        MeshMaterialIndex = meshMaterialIndex;
    }

    /// <summary>Gets the root node of the model's hierarchy.</summary>
    public ModelNodeInfo Root { get; }

    /// <summary>
    /// Gets, for each submesh (indexed the same as <see cref="Assets.Model.Meshes"/>), the material slot
    /// that submesh uses — an index into the source file's material list.
    /// </summary>
    public IReadOnlyList<int> MeshMaterialIndex { get; }
}

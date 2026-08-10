using Spot.Animation;
using Spot.Rendering;

namespace Spot.Assets;

/// <summary>
/// An imported 3D model: one or more <see cref="Mesh"/> instances that together make up the asset, plus any
/// skinning data (per-submesh bones) and animation clips baked into the source file.
/// </summary>
/// <remarks>
/// This is the high-level asset type. Load one from a file with <see cref="Load"/>, which routes
/// through the <see cref="IModelImporter"/> registered for the file's format. For lower-level
/// control, build <see cref="Mesh"/> instances yourself and construct a <see cref="Model"/> around them.
/// </remarks>
public sealed class Model
{
    private static readonly IReadOnlyList<AnimationClip> s_noClips = Array.Empty<AnimationClip>();
    private readonly IReadOnlyList<IReadOnlyList<BoneInfo>?>? _submeshBones;

    /// <summary>
    /// Initializes a new <see cref="Model"/> from already-built meshes, optionally with per-submesh skinning
    /// bones and animation clips.
    /// </summary>
    /// <param name="meshes">The meshes that make up the model.</param>
    /// <param name="submeshBones">
    /// For each submesh (indexed like <paramref name="meshes"/>), its bone list, or <see langword="null"/>
    /// for a rigid submesh. Pass <see langword="null"/> for a fully rigid model.
    /// </param>
    /// <param name="animations">The animation clips baked into the model, or <see langword="null"/> for none.</param>
    public Model(
        IReadOnlyList<Mesh> meshes,
        IReadOnlyList<IReadOnlyList<BoneInfo>?>? submeshBones = null,
        IReadOnlyList<AnimationClip>? animations = null)
    {
        Meshes = meshes;
        _submeshBones = submeshBones;
        Animations = animations ?? s_noClips;
    }

    /// <summary>Gets the meshes that make up the model.</summary>
    public IReadOnlyList<Mesh> Meshes { get; }

    /// <summary>Gets the animation clips baked into the model (empty when it has none).</summary>
    public IReadOnlyList<AnimationClip> Animations { get; }

    /// <summary>Gets whether any submesh of the model is skinned (has bones).</summary>
    public bool HasSkinning
    {
        get
        {
            if (_submeshBones is null)
            {
                return false;
            }

            for (int i = 0; i < _submeshBones.Count; i++)
            {
                if (_submeshBones[i] is { Count: > 0 })
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets whether the given submesh is skinned (has a non-empty bone list).</summary>
    /// <param name="submesh">The submesh index (into <see cref="Meshes"/>).</param>
    /// <returns><see langword="true"/> if the submesh has bones.</returns>
    public bool IsSkinned(int submesh) => BonesFor(submesh) is { Count: > 0 };

    /// <summary>Gets the bone list for the given submesh, or <see langword="null"/> if it is rigid.</summary>
    /// <param name="submesh">The submesh index (into <see cref="Meshes"/>).</param>
    /// <returns>The submesh's bones, or <see langword="null"/>.</returns>
    public IReadOnlyList<BoneInfo>? BonesFor(int submesh)
    {
        if (_submeshBones is null || submesh < 0 || submesh >= _submeshBones.Count)
        {
            return null;
        }

        return _submeshBones[submesh];
    }

    /// <summary>Gets the file the model was loaded from, if any.</summary>
    public string? SourcePath { get; internal set; }

    /// <summary>
    /// Loads a model from a file, choosing an importer by extension. Results are cached by path, so
    /// loading the same file again returns the same instance and reuses its GPU buffers.
    /// </summary>
    /// <param name="path">The path to the model file.</param>
    /// <returns>The loaded model.</returns>
    public static Model Load(string path) => ModelImporter.Load(path);
}

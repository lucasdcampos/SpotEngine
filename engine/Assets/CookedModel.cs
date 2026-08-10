using Spot.Animation;
using Spot.Rendering;

namespace Spot.Assets;

/// <summary>
/// The CPU-side result of importing or reading a model: its submeshes (rigid or skinned) together with any
/// animation clips. No GPU work is done, so this can be produced on a background thread and turned into a
/// drawable <see cref="Model"/> on the render thread. It is the shared shape passed from the importer to the
/// cooker (<see cref="ModelAssetImporter"/>) and read back from a cooked <c>.spmesh</c> (<see cref="SpMesh.ReadModel"/>).
/// </summary>
public readonly struct CookedModel
{
    private static readonly IReadOnlyList<AnimationClip> s_noClips = Array.Empty<AnimationClip>();

    /// <summary>Initializes a cooked model from its submeshes and animation clips.</summary>
    /// <param name="submeshes">The CPU geometry, one entry per submesh.</param>
    /// <param name="animations">The animation clips, or <see langword="null"/> for none.</param>
    public CookedModel(IReadOnlyList<MeshData> submeshes, IReadOnlyList<AnimationClip>? animations)
    {
        Submeshes = submeshes;
        Animations = animations ?? s_noClips;
    }

    /// <summary>Gets the submeshes that make up the model.</summary>
    public IReadOnlyList<MeshData> Submeshes { get; }

    /// <summary>Gets the animation clips baked into the model (empty when it has none).</summary>
    public IReadOnlyList<AnimationClip> Animations { get; }
}

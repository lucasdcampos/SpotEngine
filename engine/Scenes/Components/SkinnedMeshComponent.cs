using System.Collections.Generic;
using System.Numerics;
using Spot.Animation;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// Marks the sibling <see cref="MeshComponent"/> as skinned: the render system draws it through the skinning
/// path, posing it from the model's bone entities instead of this entity's transform. It carries no authorable
/// data — the bones (names and inverse-bind matrices) come from the <see cref="Assets.Model"/> and are bound,
/// by name, to the entities of the instantiated skeleton. Added automatically to skinned mesh parts by
/// <see cref="ModelInstantiator"/>.
/// </summary>
[ComponentMenu("Skinned Mesh Renderer", Addable = false, Order = 21)]
[SceneComponent("SkinnedMeshRenderer")]
public sealed class SkinnedMeshComponent : Component
{
    // Bone entities resolved by name (indexed like the submesh's bone list), and the reusable palette buffer.
    // Both are runtime-only; the serializer skips them (arrays are unsupported types).
    private Entity[]? _bones;
    private Matrix4x4[]? _palette;

    /// <summary>
    /// Builds the world-space bone palette for the mesh on <paramref name="self"/> from the live bone-entity
    /// transforms, resolving (and caching) the bone entities by name on first use. Returns
    /// <see langword="false"/> when the mesh is not ready or not skinned, so the caller draws it rigidly.
    /// </summary>
    /// <param name="self">The entity carrying this component and its <see cref="MeshComponent"/>.</param>
    /// <param name="palette">The per-bone skinning matrices (<c>InverseBind * boneWorld</c>).</param>
    /// <returns><see langword="true"/> if a palette was produced.</returns>
    internal bool TryBuildPalette(Entity self, out Matrix4x4[] palette)
    {
        palette = System.Array.Empty<Matrix4x4>();

        if (!self.TryGetComponent(out MeshComponent? mesh) || mesh.Model is null)
        {
            return false;
        }

        IReadOnlyList<BoneInfo>? bones = mesh.Model.BonesFor(mesh.SubmeshIndex);
        if (bones is null || bones.Count == 0)
        {
            return false;
        }

        if (_bones is null || _bones.Length != bones.Count)
        {
            ResolveBones(self, bones);
        }

        Matrix4x4[] result = _palette!;
        Entity[] boneEntities = _bones!;
        for (int i = 0; i < bones.Count; i++)
        {
            Entity bone = boneEntities[i];
            result[i] = bone.IsValid && bone.TryGetComponent(out TransformComponent? transform)
                ? bones[i].InverseBind * transform.Matrix
                : Matrix4x4.Identity;
        }

        palette = result;
        return true;
    }

    private void ResolveBones(Entity self, IReadOnlyList<BoneInfo> bones)
    {
        Entity root = FindSkeletonRoot(self);
        Dictionary<string, Entity> byName = AnimationSystem.MapDescendantsByName(root);

        var resolved = new Entity[bones.Count];
        for (int i = 0; i < bones.Count; i++)
        {
            if (byName.TryGetValue(AnimationSystem.NormalizeBoneName(bones[i].Name), out Entity bone))
            {
                resolved[i] = bone;
            }
        }

        _bones = resolved;
        _palette = new Matrix4x4[bones.Count];
    }

    // The skeleton root is the nearest ancestor-or-self carrying an Animator (added to every skinned model
    // root at import). Falls back to the top-most ancestor when none is found.
    private static Entity FindSkeletonRoot(Entity self)
    {
        Entity topMost = self;
        Entity? current = self;
        while (current is Entity entity)
        {
            topMost = entity;
            if (entity.HasComponent<AnimatorComponent>())
            {
                return entity;
            }

            current = entity.Parent;
        }

        return topMost;
    }
}

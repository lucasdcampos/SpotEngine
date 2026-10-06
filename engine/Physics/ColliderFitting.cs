using System.Numerics;
using Spot.Engine.Assets;
using Spot.Engine;
using Spot.Engine.Scenes;
using Spot.Engine.Graphics;
using Spot.Engine.Mathematics;

namespace Spot.Engine.Physics;

/// <summary>
/// Sizes 3D colliders to the geometry an entity draws, so a collider matches its mesh without measuring by hand.
/// </summary>
public static class ColliderFitting
{
    // Flat geometry (a plane, a quad) still gets a sliver of thickness, so the box stays a valid shape.
    private const float MinThickness = 0.01f;

    /// <summary>
    /// Gets the local-space box a mesh renderer's geometry fills: exact for a built-in mesh (from its
    /// parameters, with no model loaded), otherwise from the loaded model — or just the submesh it draws.
    /// </summary>
    /// <param name="mesh">The mesh renderer.</param>
    /// <returns>The bounds, or <see langword="null"/> when the geometry is not known yet (a model still loading).</returns>
    public static Aabb3d? MeshBounds(MeshRenderer mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (BuiltinAssets.TryGetPrimitive(mesh.ModelPath, out PrimitiveSpec spec))
        {
            return spec.Bounds;
        }

        Model? model = mesh.Model;
        if (model is null || model.Meshes.Count == 0)
        {
            return null;
        }

        return mesh.SubmeshIndex >= 0 && mesh.SubmeshIndex < model.Meshes.Count
            ? model.Meshes[mesh.SubmeshIndex].Bounds
            : model.LocalBounds;
    }

    /// <summary>
    /// Fits a collider to a mesh renderer's geometry: a box to its bounds, a sphere around them, and a capsule
    /// standing along Y with the widest horizontal radius — exactly a built-in capsule's own radius and height.
    /// The collider's offset moves to the geometry's center.
    /// </summary>
    /// <param name="collider">The collider to resize (box, sphere or capsule).</param>
    /// <param name="mesh">The mesh renderer whose geometry it should match.</param>
    /// <returns><see langword="false"/> when the geometry is not known yet or the collider type is not supported.</returns>
    public static bool FitToMesh(Collider3D collider, MeshRenderer mesh)
    {
        ArgumentNullException.ThrowIfNull(collider);
        if (MeshBounds(mesh) is not { } bounds)
        {
            return false;
        }

        Vector3 size = bounds.HalfExtents * 2;
        switch (collider)
        {
            case BoxCollider3D box:
                box.Size = Vector3.Max(size, new Vector3(MinThickness));
                break;
            case SphereCollider3D sphere:
                sphere.Radius = MathF.Max(0.5f * MathF.Max(size.X, MathF.Max(size.Y, size.Z)), MinThickness);
                break;
            case CapsuleCollider3D capsule:
                capsule.Radius = MathF.Max(0.5f * MathF.Max(size.X, size.Z), MinThickness);
                capsule.Length = MathF.Max(0, size.Y - 2 * capsule.Radius);
                break;
            default:
                return false;
        }

        collider.Offset = bounds.Center;
        return true;
    }
}

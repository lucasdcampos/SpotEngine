using System;
using System.Numerics;
using Spot.Assets;
using Spot.Physics;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Editor.UI;

/// <summary>
/// Mouse picking for the editor viewport. Casts a ray from the cursor through the camera and finds
/// the entity under it. Works identically for the 2D orthographic and 3D perspective cameras because
/// every candidate is tested in its own local space, so rotation and scale are handled for free:
/// <see cref="Sprite2DComponent"/> quads against the unit quad, and <see cref="MeshComponent"/> models
/// against their local-space bounding box.
/// </summary>
public static class ScenePicker
{
    // Quads are the unit quad [-0.5, 0.5] on the z = 0 plane (see Renderer2D). Half-extent used for
    // the local-space hit test.
    private const float QuadHalf = 0.5f;

    // Screen-space radius (pixels) for picking entities that have no drawable quad (empties, cameras).
    private const float IconRadiusPx = 14f;

    /// <summary>
    /// Returns the entity under the cursor, or <see langword="null"/> when nothing is hit.
    /// </summary>
    /// <param name="scene">The scene being displayed in the viewport.</param>
    /// <param name="viewProjection">The camera's view-projection (the one the scene was rendered with).</param>
    /// <param name="mouse">The cursor position, in screen pixels.</param>
    /// <param name="viewportPos">The top-left of the viewport image, in screen pixels.</param>
    /// <param name="viewportSize">The size of the viewport image, in pixels.</param>
    public static Entity? Pick(Scene scene, Matrix4x4 viewProjection, Vector2 mouse, Vector2 viewportPos, Vector2 viewportSize)
    {
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return null;
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 invVp))
            return null;

        // Cursor -> normalized device coordinates (y flipped: screen y grows downward).
        float ndcX = (mouse.X - viewportPos.X) / viewportSize.X * 2f - 1f;
        float ndcY = 1f - (mouse.Y - viewportPos.Y) / viewportSize.Y * 2f;

        if (!Unproject(ndcX, ndcY, 0f, invVp, out Vector3 rayOrigin) ||
            !Unproject(ndcX, ndcY, 1f, invVp, out Vector3 rayFar))
        {
            return null;
        }

        Vector3 rayDir = rayFar - rayOrigin;
        if (rayDir.LengthSquared() < 1e-12f)
            return null;
        rayDir = Vector3.Normalize(rayDir);

        // Pass 1: the drawable quads. Keep the hit nearest to the camera; on ties (overlapping quads
        // at the same depth, common in 2D) prefer the one drawn later, i.e. on top.
        Entity? best = null;
        float bestDist = float.MaxValue;

        foreach (Entity entity in scene.View<TransformComponent, Sprite2DComponent>())
        {
            TransformComponent t = entity.GetComponent<TransformComponent>();
            if (!Matrix4x4.Invert(t.Matrix, out Matrix4x4 invModel))
                continue;

            Vector3 localOrigin = Vector3.Transform(rayOrigin, invModel);
            Vector3 localDir = Vector3.TransformNormal(rayDir, invModel);
            if (MathF.Abs(localDir.Z) < 1e-6f)
                continue; // ray parallel to the quad's plane

            float tHit = -localOrigin.Z / localDir.Z;
            if (tHit < 0f)
                continue; // behind the ray origin

            Vector3 local = localOrigin + localDir * tHit;
            if (local.X < -QuadHalf || local.X > QuadHalf || local.Y < -QuadHalf || local.Y > QuadHalf)
                continue;

            Vector3 worldHit = Vector3.Transform(local, t.Matrix);
            float dist = Vector3.Dot(worldHit - rayOrigin, rayDir);
            if (dist <= bestDist)
            {
                bestDist = dist;
                best = entity;
            }
        }

        // Pass 2: the 3D meshes (primitives and imported models). Test the ray against each mesh's
        // local-space bounding box, transformed into the entity's local space so rotation and scale are
        // handled for free. Competes with the quad pass on depth so the nearest thing under the cursor wins.
        foreach (Entity entity in scene.View<TransformComponent, MeshComponent>())
        {
            MeshComponent mesh = entity.GetComponent<MeshComponent>();
            Model? model = mesh.Model;
            if (model is null || model.Meshes.Count == 0)
                continue;

            // A single submesh part (imported models spread one submesh per entity) uses that submesh's
            // box; a whole-model renderer (SubmeshIndex == -1, e.g. primitives) uses the union of them all.
            Aabb3d local = mesh.SubmeshIndex >= 0 && mesh.SubmeshIndex < model.Meshes.Count
                ? model.Meshes[mesh.SubmeshIndex].Bounds
                : model.LocalBounds;

            // Skinned parts are posed by bones, not this transform, so pad the bind-pose box the same way
            // the render culling does, keeping animated geometry inside the tested volume.
            if (entity.HasComponent<SkinnedMeshComponent>())
                local = local.Expanded(2.0f);

            TransformComponent t = entity.GetComponent<TransformComponent>();
            if (!Matrix4x4.Invert(t.Matrix, out Matrix4x4 invModel))
                continue;

            Vector3 localOrigin = Vector3.Transform(rayOrigin, invModel);
            Vector3 localDir = Vector3.TransformNormal(rayDir, invModel);
            if (!RayAabb(localOrigin, localDir, local.Min, local.Max, out float tHit))
                continue;

            Vector3 localHit = localOrigin + localDir * tHit;
            Vector3 worldHit = Vector3.Transform(localHit, t.Matrix);
            float dist = Vector3.Dot(worldHit - rayOrigin, rayDir);
            if (dist < 0f)
                continue; // behind the camera
            if (dist < bestDist)
            {
                bestDist = dist;
                best = entity;
            }
        }

        if (best != null)
            return best;

        // Pass 3: fallback for entities with no drawable (empties, cameras). Pick the one whose origin
        // projects nearest to the cursor within a small pixel radius.
        Entity? bestIcon = null;
        float bestPix = IconRadiusPx;

        foreach (Entity entity in scene.View<TransformComponent>())
        {
            if (entity.HasComponent<Sprite2DComponent>())
                continue;

            TransformComponent t = entity.GetComponent<TransformComponent>();
            if (!Project(t.WorldPosition, viewProjection, viewportPos, viewportSize, out Vector2 screen))
                continue;

            float d = Vector2.Distance(mouse, screen);
            if (d < bestPix)
            {
                bestPix = d;
                bestIcon = entity;
            }
        }

        return bestIcon;
    }

    // Slab test of a ray against an axis-aligned box, all in the same (local) space. Returns the entry
    // parameter along <paramref name="dir"/>; when the ray starts inside the box, the entry is clamped to 0
    // so the origin itself counts as the hit. <paramref name="dir"/> need not be normalized — the returned
    // parameter is in its units, which is fine since the caller maps the hit back to world space to compare depth.
    private static bool RayAabb(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max, out float tEnter)
    {
        tEnter = 0f;
        float tMin = float.NegativeInfinity;
        float tMax = float.PositiveInfinity;

        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
            float lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;

            if (MathF.Abs(d) < 1e-9f)
            {
                if (o < lo || o > hi)
                    return false; // parallel to this slab and outside it
                continue;
            }

            float t1 = (lo - o) / d;
            float t2 = (hi - o) / d;
            if (t1 > t2)
                (t1, t2) = (t2, t1);

            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax)
                return false;
        }

        if (tMax < 0f)
            return false; // box is entirely behind the ray origin

        tEnter = MathF.Max(tMin, 0f);
        return true;
    }

    private static bool Unproject(float ndcX, float ndcY, float ndcZ, Matrix4x4 invVp, out Vector3 world)
    {
        Vector4 p = Vector4.Transform(new Vector4(ndcX, ndcY, ndcZ, 1f), invVp);
        if (MathF.Abs(p.W) < 1e-6f)
        {
            world = default;
            return false;
        }
        world = new Vector3(p.X, p.Y, p.Z) / p.W;
        return true;
    }

    private static bool Project(Vector3 world, Matrix4x4 vp, Vector2 viewportPos, Vector2 viewportSize, out Vector2 screen)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), vp);
        if (clip.W <= 1e-5f)
        {
            screen = default;
            return false;
        }
        Vector3 ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        screen = new Vector2(
            viewportPos.X + (ndc.X * 0.5f + 0.5f) * viewportSize.X,
            viewportPos.Y + (1f - (ndc.Y * 0.5f + 0.5f)) * viewportSize.Y);
        return true;
    }
}


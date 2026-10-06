using System;
using System.Numerics;
using Spot.Engine;
using Spot.Engine.Scenes;
using Spot.Engine.Graphics;
using Spot.Engine.Mathematics;

namespace Spot.Editor.UI;

/// <summary>
/// Mouse picking for the editor viewport. Casts a ray from the cursor through the camera and finds
/// the entity under it. Works identically for the 2D orthographic and 3D perspective cameras because
/// every candidate is tested in its own local space, so rotation and scale are handled for free:
/// <see cref="Sprite2D"/> quads against the unit quad, and <see cref="MeshRenderer"/> models
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
        if (!TryGetRay(viewProjection, mouse, viewportPos, viewportSize, out Vector3 rayOrigin, out Vector3 rayDir))
            return null;

        Entity? best = RaycastDrawables(scene, rayOrigin, rayDir, ignoreEnclosing: false, out _);
        if (best != null)
            return best;

        // Pass 3: fallback for entities with no drawable (empties, cameras). Pick the one whose origin
        // projects nearest to the cursor within a small pixel radius.
        Entity? bestIcon = null;
        float bestPix = IconRadiusPx;

        foreach (Entity entity in scene.View<Transform>())
        {
            if (entity.HasComponent<Sprite2D>())
                continue;

            Transform t = entity.GetComponent<Transform>();
            if (!TryProject(t.WorldPosition, viewProjection, viewportPos, viewportSize, out Vector2 screen))
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

    /// <summary>
    /// The mesh entity under the cursor, for dropping a material on it; <see langword="null"/> when nothing is
    /// there or the nearest thing is not a mesh (a sprite in front of it). Meshes whose bounds enclose the camera
    /// are passed over, as for <see cref="DropPoint"/>: from inside a room, the room itself is not "under" the
    /// cursor, what you point at in it is.
    /// </summary>
    public static Entity? PickMesh(Scene scene, Matrix4x4 viewProjection, Vector2 mouse, Vector2 viewportPos, Vector2 viewportSize)
    {
        if (!TryGetRay(viewProjection, mouse, viewportPos, viewportSize, out Vector3 rayOrigin, out Vector3 rayDir))
            return null;

        Entity? hit = RaycastDrawables(scene, rayOrigin, rayDir, ignoreEnclosing: true, out _);
        return hit is Entity entity && entity.HasComponent<MeshRenderer>() ? entity : null;
    }

    /// <summary>
    /// A mesh entity's bounding box in its local space — the box picking tests and the editor outlines.
    /// Returns <see langword="false"/> while its model is not loaded.
    /// </summary>
    public static bool TryGetMeshBounds(Entity entity, out Aabb3d bounds)
    {
        bounds = default;
        if (!entity.HasComponent<MeshRenderer>())
            return false;

        MeshRenderer mesh = entity.GetComponent<MeshRenderer>();
        Model? model = mesh.Model;
        if (model is null || model.Meshes.Count == 0)
            return false;

        // A single submesh part (imported models spread one submesh per entity) uses that submesh's box; a
        // whole-model renderer (SubmeshIndex == -1, e.g. primitives) uses the union of them all.
        bounds = mesh.SubmeshIndex >= 0 && mesh.SubmeshIndex < model.Meshes.Count
            ? model.Meshes[mesh.SubmeshIndex].Bounds
            : model.LocalBounds;

        // Skinned parts are posed by bones, not this transform, so pad the bind-pose box the same way the render
        // culling does, keeping animated geometry inside the box.
        if (entity.HasComponent<SkinnedMeshRenderer>())
            bounds = bounds.Expanded(2.0f);

        return true;
    }

    // Surfaces farther than this are out of reach for a drop: the asset lands FallbackDropDistance ahead
    // instead, the same distance the camera keeps from what it frames with F.
    private const float MaxDropDistance = 200f;
    private const float FallbackDropDistance = 10f;

    /// <summary>
    /// Where an asset dropped at the cursor should land. In 3D it is the nearest of the first surface under the
    /// cursor (a sprite quad, or the bounds of a mesh the camera is outside of) and the ground plane (y = 0);
    /// when neither is within reach (looking at the sky, or at something far off) it is a point a short way in
    /// front of the camera along the cursor ray. In 2D it is the cursor's spot on the z = 0 plane.
    /// </summary>
    /// <param name="scene">The scene being displayed, or <see langword="null"/> to place against the ground only.</param>
    /// <param name="viewProjection">The camera's view-projection.</param>
    /// <param name="is3D">Whether the camera is the 3D perspective one.</param>
    /// <param name="mouse">The cursor position, in screen pixels.</param>
    /// <param name="viewportPos">The top-left of the viewport image, in screen pixels.</param>
    /// <param name="viewportSize">The size of the viewport image, in pixels.</param>
    public static Vector3 DropPoint(Scene? scene, Matrix4x4 viewProjection, bool is3D, Vector2 mouse, Vector2 viewportPos, Vector2 viewportSize)
    {
        if (!TryGetRay(viewProjection, mouse, viewportPos, viewportSize, out Vector3 rayOrigin, out Vector3 rayDir))
            return Vector3.Zero;

        // The orthographic camera looks straight down -Z, so the ray's origin already is the cursor's world point.
        if (!is3D)
            return new Vector3(rayOrigin.X, rayOrigin.Y, 0f);

        float best = MaxDropDistance;
        if (scene != null && RaycastDrawables(scene, rayOrigin, rayDir, ignoreEnclosing: true, out float surface) != null)
            best = MathF.Min(best, surface);

        if (MathF.Abs(rayDir.Y) > 1e-6f)
        {
            float ground = -rayOrigin.Y / rayDir.Y;
            if (ground > 0f)
                best = MathF.Min(best, ground);
        }

        float distance = best < MaxDropDistance ? best : FallbackDropDistance;
        return rayOrigin + rayDir * distance;
    }

    /// <summary>
    /// Builds the world-space ray under the cursor: from the near plane through the cursor, normalized.
    /// Returns <see langword="false"/> for an empty viewport or a degenerate camera.
    /// </summary>
    public static bool TryGetRay(Matrix4x4 viewProjection, Vector2 mouse, Vector2 viewportPos, Vector2 viewportSize, out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = default;
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
            return false;
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 invVp))
            return false;

        // Cursor -> normalized device coordinates (y flipped: screen y grows downward).
        float ndcX = (mouse.X - viewportPos.X) / viewportSize.X * 2f - 1f;
        float ndcY = 1f - (mouse.Y - viewportPos.Y) / viewportSize.Y * 2f;

        if (!Unproject(ndcX, ndcY, 0f, invVp, out Vector3 near) ||
            !Unproject(ndcX, ndcY, 1f, invVp, out Vector3 far))
        {
            return false;
        }

        Vector3 dir = far - near;
        if (dir.LengthSquared() < 1e-12f)
            return false;

        origin = near;
        direction = Vector3.Normalize(dir);
        return true;
    }

    // The nearest drawable (sprite quad or mesh bounds) along the ray, and the distance to it. With
    // ignoreEnclosing set, a mesh whose bounds contain the ray origin is skipped rather than hit at distance
    // zero: when placing, the camera sitting inside a big model (a room, a level) must not mean "drop here".
    private static Entity? RaycastDrawables(Scene scene, Vector3 rayOrigin, Vector3 rayDir, bool ignoreEnclosing, out float distance)
    {
        // Pass 1: the drawable quads. Keep the hit nearest to the camera; on ties (overlapping quads
        // at the same depth, common in 2D) prefer the one drawn later, i.e. on top.
        Entity? best = null;
        float bestDist = float.MaxValue;

        foreach (Entity entity in scene.View<Transform, Sprite2D>())
        {
            Transform t = entity.GetComponent<Transform>();
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
        foreach (Entity entity in scene.View<Transform, MeshRenderer>())
        {
            if (!TryGetMeshBounds(entity, out Aabb3d local))
                continue;

            Transform t = entity.GetComponent<Transform>();
            if (!Matrix4x4.Invert(t.Matrix, out Matrix4x4 invModel))
                continue;

            Vector3 localOrigin = Vector3.Transform(rayOrigin, invModel);
            Vector3 localDir = Vector3.TransformNormal(rayDir, invModel);
            if (!RayAabb(localOrigin, localDir, local.Min, local.Max, out float tHit))
                continue;
            if (ignoreEnclosing && tHit <= 0f)
                continue; // the ray starts inside these bounds

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

        distance = bestDist;
        return best;
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

    /// <summary>
    /// Projects a world point to screen pixels. Returns <see langword="false"/> when it is behind the camera.
    /// </summary>
    public static bool TryProject(Vector3 world, Matrix4x4 vp, Vector2 viewportPos, Vector2 viewportSize, out Vector2 screen)
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


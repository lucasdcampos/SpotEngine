using System;
using System.Numerics;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Editor.UI;

/// <summary>
/// Mouse picking for the editor viewport. Casts a ray from the cursor through the camera and finds
/// the entity under it. Works identically for the 2D orthographic and 3D perspective cameras because
/// the scene is made of unit quads (<see cref="Sprite2DComponent"/> + <see cref="TransformComponent"/>): the ray is
/// tested against each quad in its own local space, so rotation and scale are handled for free.
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

        if (best != null)
            return best;

        // Pass 2: fallback for entities with no quad (empties, cameras). Pick the one whose origin
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


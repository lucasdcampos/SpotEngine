using System;
using System.Numerics;
using Spot.Scenes;

namespace Spot.Game;

/// <summary>
/// Small shared helpers for the Horde Survival demo: reaching another entity's script instance, and
/// the arena/obstacle collision math used by both the player and its projectiles. Obstacles are just
/// entities tagged <c>"Obstacle"</c> with a transform, so their axis-aligned box is
/// <c>Position ± Scale/2</c>.
/// </summary>
public static class GameUtil
{
    /// <summary>Returns the first script of type <typeparamref name="T"/> on <paramref name="entity"/>, or null.</summary>
    public static T? GetScript<T>(Entity entity)
        where T : EntityBehaviour
    {
        if (entity.TryGetComponent(out ScriptComponent? scripts))
        {
            foreach (ScriptInstance item in scripts.Items)
            {
                if (item.Instance is T typed)
                {
                    return typed;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Pushes <paramref name="pos"/> (a circle of the given radius) out of every obstacle box so a moving
    /// entity cannot pass through them.
    /// </summary>
    public static void ResolveObstacles(Scene scene, ref Vector2 pos, float radius)
    {
        foreach (Entity obstacle in scene.View<TransformComponent, Sprite2DComponent>())
        {
            if (!obstacle.CompareTag("Obstacle"))
            {
                continue;
            }

            TransformComponent t = obstacle.GetComponent<TransformComponent>();
            var center = new Vector2(t.Position.X, t.Position.Y);
            var half = new Vector2(MathF.Abs(t.Scale.X) * 0.5f, MathF.Abs(t.Scale.Y) * 0.5f);

            Vector2 closest = Vector2.Clamp(pos, center - half, center + half);
            Vector2 delta = pos - closest;
            float distSq = delta.LengthSquared();
            if (distSq >= radius * radius)
            {
                continue;
            }

            if (distSq > 1e-6f)
            {
                float dist = MathF.Sqrt(distSq);
                pos = closest + delta / dist * radius;
            }
            else
            {
                // Center is inside the box: eject along the axis of least penetration.
                float dx = pos.X - center.X;
                float dy = pos.Y - center.Y;
                float penX = half.X + radius - MathF.Abs(dx);
                float penY = half.Y + radius - MathF.Abs(dy);
                if (penX < penY)
                {
                    pos.X = center.X + (dx < 0.0f ? -1.0f : 1.0f) * (half.X + radius);
                }
                else
                {
                    pos.Y = center.Y + (dy < 0.0f ? -1.0f : 1.0f) * (half.Y + radius);
                }
            }
        }
    }

    /// <summary>Returns whether <paramref name="point"/> lies within any obstacle box (small margin included).</summary>
    public static bool HitsObstacle(Scene scene, Vector2 point)
    {
        const float margin = 0.1f;
        foreach (Entity obstacle in scene.View<TransformComponent, Sprite2DComponent>())
        {
            if (!obstacle.CompareTag("Obstacle"))
            {
                continue;
            }

            TransformComponent t = obstacle.GetComponent<TransformComponent>();
            float hx = MathF.Abs(t.Scale.X) * 0.5f + margin;
            float hy = MathF.Abs(t.Scale.Y) * 0.5f + margin;
            if (MathF.Abs(point.X - t.Position.X) <= hx && MathF.Abs(point.Y - t.Position.Y) <= hy)
            {
                return true;
            }
        }

        return false;
    }
}

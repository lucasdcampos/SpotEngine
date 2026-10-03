using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Framework.Mathematics;

namespace Spot.Engine.Physics;

/// <summary>
/// Builds framework bounding boxes from engine transforms.
/// </summary>
public static class AabbTransforms
{
    extension(Aabb)
    {
        /// <summary>
        /// Builds a box from a transform, using its XY position and scale. This matches a unit quad
        /// (as drawn for a sprite) placed by the transform.
        /// </summary>
        /// <param name="transform">The transform to build the box from.</param>
        /// <returns>The bounding box.</returns>
        public static Aabb FromTransform(TransformComponent transform) => new(
            new Vector2(transform.WorldPosition.X, transform.WorldPosition.Y),
            new Vector2(transform.WorldScale.X, transform.WorldScale.Y));
    }
}

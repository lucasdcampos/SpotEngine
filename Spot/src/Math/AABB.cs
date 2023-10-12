using System;

namespace SpotEngine
{
    /// <summary>
    /// Represents an Axis-Aligned Bounding Box (AABB) in 3D space.
    /// </summary>
    public class AABB
    {
        /// <summary>
        /// Gets or sets the minimum point of the AABB.
        /// </summary>
        public Vec3 Min { get; set; }

        /// <summary>
        /// Gets or sets the maximum point of the AABB.
        /// </summary>
        public Vec3 Max { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AABB"/> class with the specified minimum and maximum points.
        /// </summary>
        /// <param name="min">The minimum point of the AABB.</param>
        /// <param name="max">The maximum point of the AABB.</param>
        public AABB(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// Calculates the center of the AABB.
        /// </summary>
        /// <returns>The center of the AABB.</returns>
        public Vec3 Center()
        {
            return (Min + Max) * 0.5f;
        }

        /// <summary>
        /// Calculates the dimensions (width, height, and depth) of the AABB.
        /// </summary>
        /// <returns>The dimensions of the AABB.</returns>
        public Vec3 Dimensions()
        {
            return Max - Min;
        }

        /// <summary>
        /// Calculates the volume of the AABB.
        /// </summary>
        /// <returns>The volume of the AABB.</returns>
        public float Volume()
        {
            var dimensions = Dimensions();
            return dimensions.X * dimensions.Y * dimensions.Z;
        }

        /// <summary>
        /// Checks if a point is contained within the AABB.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside the AABB, otherwise false.</returns>
        public bool Contains(Vec3 point)
        {
            return point.X >= Min.X && point.X <= Max.X &&
                   point.Y >= Min.Y && point.Y <= Max.Y &&
                   point.Z >= Min.Z && point.Z <= Max.Z;
        }

        /// <summary>
        /// Expands the AABB to include a new point.
        /// </summary>
        /// <param name="point">The point to include.</param>
        public void ExpandToFit(Vec3 point)
        {
            Min = Vec3.Min(Min, point);
            Max = Vec3.Max(Max, point);
        }

        /// <summary>
        /// Checks for intersection with another AABB.
        /// </summary>
        /// <param name="other">The other AABB to test for intersection.</param>
        /// <returns>True if there is an intersection, otherwise false.</returns>
        public bool Intersects(AABB other)
        {
            return Min.X <= other.Max.X && Max.X >= other.Min.X &&
                   Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
                   Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
        }
    }
}

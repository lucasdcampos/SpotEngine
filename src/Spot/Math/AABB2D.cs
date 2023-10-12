namespace SpotEngine
{
    /// <summary>
    /// Represents an Axis-Aligned Bounding Box (AABB) in 2D space.
    /// </summary>
    public class AABB2D
    {
        /// <summary>
        /// Gets or sets the minimum point of the AABB.
        /// </summary>
        public Vec2 Min { get; set; }

        /// <summary>
        /// Gets or sets the maximum point of the AABB.
        /// </summary>
        public Vec2 Max { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AABB2D"/> class with the specified minimum and maximum points.
        /// </summary>
        /// <param name="min">The minimum point of the AABB.</param>
        /// <param name="max">The maximum point of the AABB.</param>
        public AABB2D(Vec2 min, Vec2 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>
        /// Gets the width of the AABB.
        /// </summary>
        public float Width
        {
            get { return Max.X - Min.X; }
        }

        /// <summary>
        /// Gets the height of the AABB.
        /// </summary>
        public float Height
        {
            get { return Max.Y - Min.Y; }
        }

        /// <summary>
        /// Gets the center point of the AABB.
        /// </summary>
        public Vec2 Center
        {
            get { return new Vec2((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2); }
        }

        /// <summary>
        /// Checks if the AABB contains the specified point.
        /// </summary>
        /// <param name="point">The point to check.</param>
        /// <returns>True if the point is inside the AABB; otherwise, false.</returns>
        public bool Contains(Vec2 point)
        {
            return point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;
        }

        /// <summary>
        /// Checks if this AABB intersects with another AABB.
        /// </summary>
        /// <param name="other">The other AABB to check for intersection.</param>
        /// <returns>True if the two AABBs intersect; otherwise, false.</returns>
        public bool Intersects(AABB2D other)
        {
            return !(Max.X < other.Min.X || Min.X > other.Max.X || Max.Y < other.Min.Y || Min.Y > other.Max.Y);
        }
    }
}

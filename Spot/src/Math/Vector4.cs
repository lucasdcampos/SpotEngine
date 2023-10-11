namespace SpotEngine
{
    /// <summary>
    /// Represents a four-component vector in a three-dimensional space.
    /// </summary>
    public class Vec4
    {
        /// <summary>
        /// Gets or sets the X component of the vector.
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// Gets or sets the Y component of the vector.
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// Gets or sets the Z component of the vector.
        /// </summary>
        public float Z { get; set; }

        /// <summary>
        /// Gets or sets the W component of the vector.
        /// </summary>
        public float W { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Vec4"/> class with the specified components.
        /// </summary>
        /// <param name="x">The X component of the vector.</param>
        /// <param name="y">The Y component of the vector.</param>
        /// <param name="z">The Z component of the vector.</param>
        /// <param name="w">The W component of the vector.</param>
        public Vec4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>
        /// Calculates the length (magnitude) of the vector.
        /// </summary>
        /// <returns>The length of the vector.</returns>
        public float Length()
        {
            return (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
        }

        /// <summary>
        /// Normalizes the vector, turning it into a unit vector (with a length of 1).
        /// </summary>
        /// <param name="vector">The vector to be normalized.</param>
        /// <returns>The normalized vector.</returns>
        public static Vec4 Normalize(Vec4 vector)
        {
            float length = vector.Length();
            if (length > 0)
            {
                return new Vec4(vector.X / length, vector.Y / length, vector.Z / length, vector.W / length);
            }
            return Vec4.Zero;
        }

        /// <summary>
        /// Gets a zero vector (with all components equal to zero).
        /// </summary>
        public static Vec4 Zero => new Vec4(0, 0, 0, 0);

        /// <summary>
        /// Gets a unit vector (with all components equal to one).
        /// </summary>
        public static Vec4 One => new Vec4(1, 1, 1, 1);
    }
}


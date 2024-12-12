namespace SpotEngine
{
    /// <summary>
    /// Represents a four-component vector in a three-dimensional space.
    /// </summary>
    public struct Vec4
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
        /// Gets a unit vector along the X-axis (1, 0, 0, 0).
        /// </summary>
        public static Vec4 UnitX => new Vec4(1.0f, 0.0f, 0.0f, 0.0f);

        /// <summary>
        /// Gets a unit vector along the Y-axis (0, 1, 0, 0).
        /// </summary>
        public static Vec4 UnitY => new Vec4(0.0f, 1.0f, 0.0f, 0.0f);

        /// <summary>
        /// Gets a unit vector along the Z-axis (0, 0, 1, 0).
        /// </summary>
        public static Vec4 UnitZ => new Vec4(0.0f, 0.0f, 1.0f, 0.0f);

        /// <summary>
        /// Gets a unit vector along the W-axis (0, 0, 0, 1).
        /// </summary>
        public static Vec4 UnitW => new Vec4(0.0f, 0.0f, 0.0f, 1.0f);


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
        /// Returns a normalized version of the vector (a unit vector pointing in the same direction).
        /// This method scales the vector so that its length (magnitude) is 1 while maintaining its direction.
        /// </summary>
        /// <returns>A new <see cref="Vec4"/> that is the normalized version of the current vector.</returns>
        public Vec4 Normalized()
        {
            return Vec4.Normalize(this);
        }

        /// <summary>
        /// Gets a zero vector (with all components equal to zero).
        /// </summary>
        public static Vec4 Zero => new Vec4(0, 0, 0, 0);

        /// <summary>
        /// Gets a unit vector (with all components equal to one).
        /// </summary>
        public static Vec4 One => new Vec4(1, 1, 1, 1);

        /// <summary>
        /// Adds two vectors.
        /// </summary>
        /// <param name="left">The left-hand vector.</param>
        /// <param name="right">The right-hand vector.</param>
        /// <returns>The result of the addition.</returns>
        public static Vec4 operator +(Vec4 left, Vec4 right)
        {
            return new Vec4(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);
        }

        /// <summary>
        /// Subtracts one vector from another.
        /// </summary>
        /// <param name="left">The left-hand vector.</param>
        /// <param name="right">The right-hand vector.</param>
        /// <returns>The result of the subtraction.</returns>
        public static Vec4 operator -(Vec4 left, Vec4 right)
        {
            return new Vec4(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);
        }

        /// <summary>
        /// Multiplies a vector by a scalar.
        /// </summary>
        /// <param name="vector">The vector to be multiplied.</param>
        /// <param name="scalar">The scalar value.</param>
        /// <returns>The result of the multiplication.</returns>
        public static Vec4 operator *(Vec4 vector, float scalar)
        {
            return new Vec4(vector.X * scalar, vector.Y * scalar, vector.Z * scalar, vector.W * scalar);
        }

        /// <summary>
        /// Divides a vector by a scalar.
        /// </summary>
        /// <param name="vector">The vector to be divided.</param>
        /// <param name="scalar">The scalar value.</param>
        /// <returns>The result of the division.</returns>
        public static Vec4 operator /(Vec4 vector, float scalar)
        {
            if (scalar == 0)
            {
                throw new DivideByZeroException("Division by zero is not allowed.");
            }
            return new Vec4(vector.X / scalar, vector.Y / scalar, vector.Z / scalar, vector.W / scalar);
        }
    }
}


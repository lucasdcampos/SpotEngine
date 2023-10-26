namespace SpotEngine
{
    /// <summary>
    /// Represents a two-component vector in 2D space.
    /// </summary>
    public class Vec2
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
        /// Initializes a new instance of the <see cref="Vec2"/> class with the specified components.
        /// </summary>
        /// <param name="x">The X component of the vector.</param>
        /// <param name="y">The Y component of the vector.</param>
        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        /// Calculates the length (magnitude) of the vector.
        /// </summary>
        /// <returns>The length of the vector.</returns>
        public float Length()
        {
            return (float)Math.Sqrt(X * X + Y * Y);
        }

        /// <summary>
        /// Normalizes the vector, turning it into a unit vector (with a length of 1).
        /// </summary>
        /// <param name="vector">The vector to be normalized.</param>
        /// <returns>The normalized vector.</returns>
        public static Vec2 Normalize(Vec2 vector)
        {
            float length = vector.Length();
            if (length > 0)
            {
                return new Vec2(vector.X / length, vector.Y / length);
            }
            return Vec2.Zero;
        }

        /// <summary>
        /// Gets a zero vector (with all components equal to zero).
        /// </summary>
        public static Vec2 Zero => new Vec2(0, 0);

        /// <summary>
        /// Gets a unit vector (with all components equal to one).
        /// </summary>
        public static Vec2 One => new Vec2(1, 1);

        /// <summary>
        /// Adds two vectors.
        /// </summary>
        /// <param name="left">The left-hand vector.</param>
        /// <param name="right">The right-hand vector.</param>
        /// <returns>The result of the addition.</returns>
        public static Vec2 operator +(Vec2 left, Vec2 right)
        {
            return new Vec2(left.X + right.X, left.Y + right.Y);
        }

        /// <summary>
        /// Subtracts one vector from another.
        /// </summary>
        /// <param name="left">The left-hand vector.</param>
        /// <param name="right">The right-hand vector.</param>
        /// <returns>The result of the subtraction.</returns>
        public static Vec2 operator -(Vec2 left, Vec2 right)
        {
            return new Vec2(left.X - right.X, left.Y - right.Y);
        }

        /// <summary>
        /// Multiplies a vector by a scalar.
        /// </summary>
        /// <param name="vector">The vector to be multiplied.</param>
        /// <param name="scalar">The scalar value.</param>
        /// <returns>The result of the multiplication.</returns>
        public static Vec2 operator *(Vec2 vector, float scalar)
        {
            return new Vec2(vector.X * scalar, vector.Y * scalar);
        }

        /// <summary>
        /// Divides a vector by a scalar.
        /// </summary>
        /// <param name="vector">The vector to be divided.</param>
        /// <param name="scalar">The scalar value.</param>
        /// <returns>The result of the division.</returns>
        public static Vec2 operator /(Vec2 vector, float scalar)
        {
            if (scalar == 0)
            {
                string err = "Division by zero is not allowed.";
                Log.Error(err);
                throw new DivideByZeroException(err);
            }
            return new Vec2(vector.X / scalar, vector.Y / scalar);
        }
    }

}


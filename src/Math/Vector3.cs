using System;

namespace SpotEngine
{
    /// <summary>
    /// Represents a three-component vector in 3D space.
    /// </summary>
    public class Vec3
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
        /// Initializes a new instance of the <see cref="Vec3"/> class with the specified components.
        /// </summary>
        /// <param name="x">The X component of the vector.</param>
        /// <param name="y">The Y component of the vector.</param>
        /// <param name="z">The Z component of the vector.</param>
        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// Calculates the length (magnitude) of the vector.
        /// </summary>
        /// <returns>The length of the vector.</returns>
        public float Length()
        {
            return (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        }

        /// <summary>
        /// Normalizes the vector, turning it into a unit vector (with a length of 1).
        /// </summary>
        /// <param name="vector">The vector to be normalized.</param>
        /// <returns>The normalized vector.</returns>
        public static Vec3 Normalize(Vec3 vector)
        {
            float length = vector.Length();
            if (length > 0)
            {
                return new Vec3(vector.X / length, vector.Y / length, vector.Z / length);
            }
            return Vec3.Zero;
        }

        /// <summary>
        /// Gets a zero vector (with all components equal to zero).
        /// </summary>
        public static Vec3 Zero => new Vec3(0, 0, 0);

        /// <summary>
        /// Gets a unit vector (with all components equal to one).
        /// </summary>
        public static Vec3 One => new Vec3(1, 1, 1);

        /// <summary>
        /// Returns a vector with the minimum components of two vectors.
        /// </summary>
        public static Vec3 Min(Vec3 left, Vec3 right)
        {
            return new Vec3(Math.Min(left.X, right.X), Math.Min(left.Y, right.Y), Math.Min(left.Z, right.Z));
        }

        /// <summary>
        /// Returns a vector with the maximum components of two vectors.
        /// </summary>
        public static Vec3 Max(Vec3 left, Vec3 right)
        {
            return new Vec3(Math.Max(left.X, right.X), Math.Max(left.Y, right.Y), Math.Max(left.Z, right.Z));
        }

        /// <summary>
        /// Adds two vectors.
        /// </summary>
        public static Vec3 operator +(Vec3 left, Vec3 right)
        {
            return new Vec3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        /// <summary>
        /// Subtracts one vector from another.
        /// </summary>
        public static Vec3 operator -(Vec3 left, Vec3 right)
        {
            return new Vec3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        /// <summary>
        /// Multiplies a vector by a scalar.
        /// </summary>
        public static Vec3 operator *(Vec3 vector, float scalar)
        {
            return new Vec3(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);
        }

        /// <summary>
        /// Divides a vector by a scalar.
        /// </summary>
        public static Vec3 operator /(Vec3 vector, float scalar)
        {
            if (scalar == 0)
            {
                string err = "Division by zero is not allowed.";
                Log.Error(err);
                throw new DivideByZeroException(err);
            }
            return new Vec3(vector.X / scalar, vector.Y / scalar, vector.Z / scalar);
        }
    }
}

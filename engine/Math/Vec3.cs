﻿using System;

namespace SpotEngine
{
    /// <summary>
    /// Represents a three-component vector in 3D space.
    /// </summary>
    public struct Vec3
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
        /// Returns a normalized version of the vector (a unit vector pointing in the same direction).
        /// This method scales the vector so that its length (magnitude) is 1 while maintaining its direction.
        /// </summary>
        /// <returns>A new <see cref="Vec3"/> that is the normalized version of the current vector.</returns>
        public Vec3 Normalized()
        {
            return Vec3.Normalize(this);
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
        /// Gets a unit vector along the X-axis (1, 0, 0).
        /// </summary>
        public static Vec3 UnitX => new Vec3(1.0f, 0.0f, 0.0f);

        /// <summary>
        /// Gets a unit vector along the Y-axis (0, 1, 0).
        /// </summary>
        public static Vec3 UnitY => new Vec3(0.0f, 1.0f, 0.0f);

        /// <summary>
        /// Gets a unit vector along the Z-axis (0, 0, 1).
        /// </summary>
        public static Vec3 UnitZ => new Vec3(0.0f, 0.0f, 1.0f);


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
        /// Calculates the cross product of two vectors.
        /// </summary>
        /// <param name="left">The first vector.</param>
        /// <param name="right">The second vector.</param>
        /// <returns>The cross product of the two vectors.</returns>
        public static Vec3 Cross(Vec3 left, Vec3 right)
        {
            float x = left.Y * right.Z - left.Z * right.Y;
            float y = left.Z * right.X - left.X * right.Z;
            float z = left.X * right.Y - left.Y * right.X;

            return new Vec3(x, y, z);
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

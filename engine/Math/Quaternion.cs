namespace SpotEngine
{
    /// <summary>
    /// Represents a quaternion for 3D rotation and orientation.
    /// </summary>
    public class Quaternion
    {
        /// <summary>
        /// Gets or sets the X component of the quaternion.
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// Gets or sets the Y component of the quaternion.
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// Gets or sets the Z component of the quaternion.
        /// </summary>
        public float Z { get; set; }

        /// <summary>
        /// Gets or sets the W component of the quaternion.
        /// </summary>
        public float W { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Quaternion"/> class with the specified components.
        /// </summary>
        /// <param name="x">The X component of the quaternion.</param>
        /// <param name="y">The Y component of the quaternion.</param>
        /// <param name="z">The Z component of the quaternion.</param>
        /// <param name="w">The W component of the quaternion.</param>
        public Quaternion(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        /// <summary>
        /// Gets the identity quaternion (no rotation).
        /// </summary>
        public static Quaternion Identity => new Quaternion(0, 0, 0, 1);

        /// <summary>
        /// Creates a quaternion from an axis and an angle using the axis-angle representation of rotation.
        /// The axis is normalized before calculation. The resulting quaternion represents a rotation 
        /// around the specified axis by the given angle.
        /// </summary>
        /// <param name="axis">The axis of rotation represented as a 3D vector.</param>
        /// <param name="angle">The angle of rotation in radians.</param>
        /// <returns>A new <see cref="Quaternion"/> representing the rotation around the given axis by the specified angle.</returns>
        public static Quaternion FromAxisAngle(Vec3 axis, float angle)
        {
            axis = axis.Normalized();

            float halfAngle = angle / 2.0f;
            float sinHalfAngle = (float)Math.Sin(halfAngle);

            return new Quaternion(
                axis.X * sinHalfAngle,
                axis.Y * sinHalfAngle,
                axis.Z * sinHalfAngle,
                (float)Math.Cos(halfAngle)
            );
        }

    }

}

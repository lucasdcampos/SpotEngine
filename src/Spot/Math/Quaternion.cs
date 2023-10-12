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
    }

}

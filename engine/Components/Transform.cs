namespace SpotEngine
{
    /// <summary>
    /// Represents a transform component, containing position, rotation, and scale data for a 3D object.
    /// </summary>
    public class Transform : Component
    {
        /// <summary>
        /// The position of the object in 3D space.
        /// </summary>
        public Vec3 Pos = Vec3.Zero;

        /// <summary>
        /// The rotation of the object in 3D space, in degrees.
        /// </summary>
        public Vec3 Rot = Vec3.Zero;

        /// <summary>
        /// The scale of the object in 3D space.
        /// </summary>
        public Vec3 Scale = Vec3.One;

        /// <summary>
        /// Sets the total scale of the object uniformly along all axes (X, Y, Z).
        /// </summary>
        /// <param name="scale">The scale factor to apply to all axes.</param>
        public void SetTotalScale(float scale)
        {
            Scale.X = scale;
            Scale.Y = scale;
            Scale.Z = scale;
        }

        /// <summary>
        /// Computes and returns the transformation matrix for this object, combining translation, rotation, and scaling.
        /// </summary>
        /// <returns>A 4x4 transformation matrix.</returns>
        public Mat4 GetTransformationMatrix()
        {
            Mat4 rotationMatrix = Mat4.CreateRotX(MathUtils.DegreesToRadians(Rot.X)) *
                                     Mat4.CreateRotY(MathUtils.DegreesToRadians(Rot.Y)) *
                                     Mat4.CreateRotZ(MathUtils.DegreesToRadians(Rot.Z));

            Mat4 translationMatrix = Mat4.CreateTranslation(Pos.X, Pos.Y, Pos.Z);
            Mat4 scaleMatrix = Mat4.CreateScale(Scale.X, Scale.Y, Scale.Z);

            return rotationMatrix * translationMatrix * scaleMatrix;
        }
    }
}
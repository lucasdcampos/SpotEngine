using OpenTK.Mathematics;

namespace SpotEngine
{
    internal static class SptUtils
    {
        internal static Vector3 ToVector3(Vec3 vec)
        {
            return new Vector3(vec.X, vec.Y, vec.Z);
        }

        internal static OpenTK.Mathematics.Quaternion ToQuaternion(Quaternion quat)
        {
            return new OpenTK.Mathematics.Quaternion(quat.X, quat.Y, quat.Z, quat.W);
        }

        public static Color4<Rgba> ToColor4(Color color)
        {
            if (color == null) return Color4.Magenta;
            return new Color4<Rgba>(color.R, color.G, color.B, color.A);
        }
    }
}

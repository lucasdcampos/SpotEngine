using OpenTK.Mathematics;

namespace SpotEngine
{
    internal static class SptUtils
    {
        internal static Vector3 ToVector3(Vec3 vec)
        {
            return new Vector3(vec.X, vec.Y, vec.Z);
        }

        public static Color4<Rgba> ToColor4(Color color)
        {
            if (color == null) return Color4.Magenta;
            return new Color4<Rgba>(color.Rf, color.Gf, color.Bf, color.Af);
        }
    }
}

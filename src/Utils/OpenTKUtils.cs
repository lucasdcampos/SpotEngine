
using OpenTK.Mathematics;

namespace SpotEngine.Utils
{
    internal static class OpenTKUtils
    {
        internal static float[] TriangleVertices { get; private set; } =
        {
            0.0f,  0.5f, 0.0f,
            -0.5f, -0.5f, 0.0f,
            0.5f, -0.5f, 0.0f
        };

        public static OpenTK.Mathematics.Color4<Rgba> SptColorToTKColor(Color color)
        {
            return new Color4<Rgba>(color.Rf, color.Gf, color.Bf, color.Af);
        }

        public static Vector3 SptVec3ToTKVector3(Vec3 vec)
        {
            return new Vector3(vec.X, vec.Y, vec.Z);
        }

        public static Vec3 TKV4ToSptV3(Vector3 vec)
        {
            return new Vec3(vec.X, vec.Y, vec.Z);
        }
    }
}

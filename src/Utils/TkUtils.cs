
using OpenTK.Mathematics;

namespace SpotEngine.Utils
{
    internal static class TkUtils
    {
        public static Vec3 ToVec3(Vector3 vec)
        {
            return new Vec3(vec.X, vec.Y, vec.Z);
        }
    }
}

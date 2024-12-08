
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
            return new Color4<Rgba>(color.R, color.G, color.B, color.A);
        }
    }
}

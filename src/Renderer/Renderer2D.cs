using OpenTK.Graphics.OpenGL.Compatibility;
using System.Drawing;

namespace SpotEngine.Internal.Renderer
{
    internal class Renderer2D
    {
        internal static Renderer2D Instance { get; private set; }

        internal static void Init()
        {
            if (Instance != null) Log.Warn("An instance of Renderer2D already exists!");

            Instance = new Renderer2D();
        }

        internal void DrawPrimitive(Primitive primitive, Vec3[] vertices, Color[] colors)
        {
            GL.Begin(FromSpotPrimitive(primitive));

            for(int i = 0; i < colors.Length; i++)
            {
                if (colors[i] != null)
                    GL.Color3f(colors[i].r, colors[i].g, colors[i].b);

                GL.Vertex3f(vertices[i].X, vertices[i].Y, vertices[i].Z);
            }

            GL.End();
        }

        private PrimitiveType FromSpotPrimitive(Primitive primitive)
        {
            switch (primitive)
            {
                case Primitive.Triangles:
                    return PrimitiveType.Triangles;
                case Primitive.Quads: 
                    return PrimitiveType.Quads;
            }

            return PrimitiveType.Triangles;
        }
    }

    enum Primitive
    {
        Triangles, Quads
    }
}

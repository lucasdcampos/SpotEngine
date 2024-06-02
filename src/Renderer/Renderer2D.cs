using OpenTK.Graphics.OpenGL.Compatibility;

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

        internal void DrawPrimitive(Primitive primitive, Vec3[] vertices, Color color)
        {
            GL.Begin(FromSpotPrimitive(primitive));

            GL.Color3f(color.r, color.g, color.b);
            foreach(var vec in vertices)
            {
                GL.Vertex3f(vec.X, vec.Y, vec.Z);
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

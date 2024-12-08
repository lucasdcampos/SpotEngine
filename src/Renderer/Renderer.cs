using OpenTK.Graphics.OpenGL.Compatibility;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace SpotEngine.Internal.Rendering
{
    internal class Renderer
    {
        private VAO m_squareVAO;
        private Shader m_shader;
        private IGLFWGraphicsContext m_glContext;
        internal Renderer(IGLFWGraphicsContext context)
        {
            m_glContext = context;
            m_shader = Shader.GetDefault();

            InitializeSquare();
        }

        // Note: This is just for testing purposes
        // legacy OpenGL should be avoided if possible
        internal void DrawPrimitive(Primitive primitive, Vec3[] vertices, Color[] colors)
        {
            
            GL.Begin(FromSpotPrimitive(primitive));

            for(int i = 0; i < vertices.Length; i++)
            {
                if (i < colors.Length)
                    GL.Color3f(colors[i].R, colors[i].G, colors[i].B);

                GL.Vertex3f(vertices[i].X, vertices[i].Y, vertices[i].Z);
            }

            GL.End();
        }


        private void InitializeSquare()
        {
            float[] squareVertices = {
                
                0.0f, 1.0f, 0.0f,
                1.0f, 1.0f, 0.0f,
                0.0f, 0.0f, 0.0f,
                1.0f, 0.0f, 0.0f
            };

            m_squareVAO = new VAO(squareVertices);
        }

        internal void DrawQuad(Transform transform, Color color)
        {
            m_shader.Use();

            var model = Matrix4.CreateScale(transform.Scale.X, transform.Scale.Y, 1.0f) * Matrix4.CreateTranslation(transform.Pos.X, transform.Pos.Y, 0.0f);
            m_shader.SetMatrix4("uModel", model);

            m_shader.SetVector4("uColor", new Vector4(color.R, color.G, color.B, color.A));

            m_squareVAO.Bind();
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
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

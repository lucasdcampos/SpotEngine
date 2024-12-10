// TODO: Needs revision
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL.Compatibility;
using OpenTK.Mathematics;
using SpotEngine.Internal.Rendering;

namespace SpotEngine.Rendering
{
    public class Renderer
    {
        private Shader m_defaultShader;
        private InternalCamera m_camera;

        private VAO m_triangleVAO;
        private float[] m_triangleVertices;

        public InternalCamera Camera => m_camera;

        public Renderer(Shader shader = null)
        {
            m_defaultShader = shader ?? Shader.GetDefault();
            m_camera = new InternalCamera(new Vector3(0, 0, 3));

            InitializeTriangle();
        }

        private void InitializeTriangle()
        {
            // All white color by default
            m_triangleVertices = new float[]
            {
                -0.5f, -0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    0.0f, 0.0f,
                0.5f, -0.5f, 0.0f,     1.0f, 1.0f, 1.0f,    1.0f, 0.0f,
                0.0f,  0.5f, 0.0f,     1.0f, 1.0f, 1.0f,    0.5f, 1.0f
            };

            m_triangleVAO = new VAO(m_triangleVertices, 8 * sizeof(float),
                (0, 3, 0),                  // Vertices (pos)
                (1, 3, 3 * sizeof(float)),  // Color (neutral)
                (2, 2, 6 * sizeof(float))   // Texture (not yet implemented)
            );
        }


        private void DrawBase(VAO vao, Matrix4 modelMatrix, Vector4 lightColor)
        {
            m_defaultShader.Use();

            m_defaultShader.SetMatrix4("model", modelMatrix);
            m_defaultShader.SetMatrix4("view", m_camera.GetViewMatrix());
            m_defaultShader.SetMatrix4("projection", m_camera.GetProjectionMatrix(45.0f, 800f / 600f, 0.1f, 100f));
            m_defaultShader.SetVector4("lightColor", lightColor);

            vao.Bind();
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            GL.BindVertexArray(VertexArrayHandle.Zero);
        }

        public void DrawTriangle(Vec3 position, Vec3 scale, Vec3 rotation, Color4<Rgba> color)
        {
            // The triangle will disappear if rotation.Y == 0
            if (rotation.Y == 0)
                rotation = new Vec3(rotation.X, 0.00001f, rotation.Z);

            float angle = SptUtils.ToVector3(rotation).Length;
            Vector3 axis = SptUtils.ToVector3(rotation).Normalized();

            Matrix4 modelMatrix = Matrix4.Identity;
            modelMatrix *= Matrix4.CreateTranslation(SptUtils.ToVector3(position));
            modelMatrix *= Matrix4.CreateFromAxisAngle(axis, angle);
            modelMatrix *= Matrix4.CreateScale(SptUtils.ToVector3(scale));

            DrawBase(m_triangleVAO, modelMatrix, new Vector4(color.X, color.Y, color.Z, color.W));
        }

        public void DrawObject(VAO vao, Vector3 position, Vector3 scale, float rotationAngle, Vector3 rotationAxis, Color4<Rgba> color)
        {
            Matrix4 modelMatrix = Matrix4.Identity;
            modelMatrix *= Matrix4.CreateTranslation(position);
            modelMatrix *= Matrix4.CreateFromAxisAngle(rotationAxis, rotationAngle);
            modelMatrix *= Matrix4.CreateScale(scale);

            DrawBase(vao, modelMatrix, new Vector4(color.X, color.Y, color.Z, color.W));
        }
    }

}
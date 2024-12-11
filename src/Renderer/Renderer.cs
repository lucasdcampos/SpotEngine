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

        private float[] m_triangleVertices;
        private float[] m_squareVertices;
        private VAO m_lineVAO;
        private Vec2 m_viewport;
        private ShaderManager m_shaderManager => Application.ShaderManager;
        private VAOManager m_vaoManager => Application.VAOManager;

        public InternalCamera Camera => m_camera;

        public Renderer(Shader shader = null)
        {
            m_defaultShader = shader ?? m_shaderManager.GetShader("default");
            m_camera = new InternalCamera(new Vector3(0, 0, 3));

            InitializeTriangle();
            InitializeSquare();
            InitializeLine();
        }

        private void InitializeTriangle()
        {
            m_triangleVertices = new float[]
            {
                -0.5f, -0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    0.0f, 0.0f,
                 0.5f, -0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    1.0f, 0.0f,
                 0.0f,  0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    0.5f, 1.0f
            };

            m_vaoManager.LoadVAO("triangle", m_triangleVertices, 8 * sizeof(float),
                (0, 3, 0),                  // Vertices (pos)
                (1, 3, 3 * sizeof(float)),  // Color
                (2, 2, 6 * sizeof(float))   // Texture
            );
        }

        private void InitializeSquare()
        {
            m_squareVertices = new float[]
            {
                -0.5f, -0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    0.0f, 0.0f,
                 0.5f, -0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    1.0f, 0.0f,
                 0.5f,  0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    1.0f, 1.0f,
                -0.5f,  0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    0.0f, 1.0f
            };

            m_vaoManager.LoadVAO("square", m_squareVertices, 8 * sizeof(float),
                (0, 3, 0),                  // Vertices (pos)
                (1, 3, 3 * sizeof(float)),  // Color
                (2, 2, 6 * sizeof(float))   // Texture
            );
        }

        private void InitializeLine()
        {
            float[] lineVertices = new float[]
            {
                0f, 0f, 0f, 1f, 1f, 1f, 1f,
                0f, 1f, 0f, 1f, 1f, 1f, 1f
            };

            m_lineVAO = m_vaoManager.LoadVAO("line", lineVertices, 7 * sizeof(float),
                (0, 3, 0),  // Posição
                (1, 4, 3 * sizeof(float)) // Color
            );
        }

        private void DrawBase(string vaoName, Matrix4 modelMatrix, Material material)
        {
            // TODO: We should have a better approach
            m_viewport = new Vec2(Application.Window.Width, Application.Window.Height);

            VAO vao = m_vaoManager.GetVAO(vaoName);
            vao.Bind();
            GL.DrawArrays(PrimitiveType.TriangleFan, 0, 4);

            GL.BindVertexArray(VertexArrayHandle.Zero);
        }

        public void DrawTriangle(Vec3 position, Vec3 scale, Vec3 rotation, Material material)
        {
            Matrix4 modelMatrix = CalculateModelMatrix(position, scale, rotation);

            SetShaderUniforms(m_defaultShader, modelMatrix,
                              m_camera.GetViewMatrix(),
                              m_camera.GetProjectionMatrix(45.0f, m_viewport.X / m_viewport.Y, 0.1f, 100f),
                              material);

            DrawBase("triangle", modelMatrix, material);
        }

        public void DrawSquare(Vec3 position, Vec3 scale, Vec3 rotation, Material material)
        {
            Matrix4 modelMatrix = CalculateModelMatrix(position, scale, rotation);

            SetShaderUniforms(m_defaultShader, modelMatrix,
                              m_camera.GetViewMatrix(),
                              m_camera.GetProjectionMatrix(45.0f, m_viewport.X / m_viewport.Y, 0.1f, 100f),
                              material);

            DrawBase("square", modelMatrix, material);
        }

        public void DrawSquare(Vec3 position, Vec3 scale, float rotationZ, Material material)
        {
            DrawSquare(position, scale, new Vec3(0, 0, rotationZ), material);
        }

        public void DrawLine(Vec3 start, Vec3 end, Color color)
        {
            float[] lineVertices = new float[]
            {
                start.X, start.Y, start.Z, color.R, color.G, color.B, color.A,
                end.X, end.Y, end.Z, color.R, color.G, color.B, color.A
            };

            m_lineVAO.UpdateVertices(lineVertices);

            Matrix4 modelMatrix = Matrix4.Identity;

            SetShaderUniforms(m_defaultShader, modelMatrix,
                              m_camera.GetViewMatrix(),
                              m_camera.GetProjectionMatrix(45.0f, m_viewport.X / m_viewport.Y, 0.1f, 100f),
                              new Material(color));

            m_lineVAO.Bind();
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);

            GL.BindVertexArray(VertexArrayHandle.Zero);
        }


        private Matrix4 CalculateModelMatrix(Vec3 position, Vec3 scale, Vec3 rotation)
        {
            if (rotation.Y == 0) rotation.Y = 0.00001f; // Prevent problems with rotation being 0
            float angle = SptUtils.ToVector3(rotation).Length;
            Vector3 axis = SptUtils.ToVector3(rotation).Normalized();

            return Matrix4.Identity
                * Matrix4.CreateTranslation(SptUtils.ToVector3(position))
                * Matrix4.CreateFromAxisAngle(axis, angle)
                * Matrix4.CreateScale(SptUtils.ToVector3(scale));
        }

        private void SetShaderUniforms(Shader shader, Matrix4 model, Matrix4 view, Matrix4 projection, Material material = null)
        {
            shader.Use();
            shader.SetUniform("model", model);
            shader.SetUniform("view", view);
            shader.SetUniform("projection", projection);

            if (material == null)
            {
                new Material(Color.White).ApplyMaterial(m_defaultShader);
            }
            else
            {
                material.ApplyMaterial(m_defaultShader);
            }
        }

        private void DrawDebugGrid()
        {
            DrawLine(new Vec3(m_camera.Position.X - 1000, 0, 0), new Vec3(m_camera.Position.X + 1000, 0, 0), Color.Red);
            DrawLine(new Vec3(0, m_camera.Position.Y - 1000, 0), new Vec3(0, m_camera.Position.Y + 1000, 0), Color.Green);
            DrawLine(new Vec3(0, 0, m_camera.Position.Z - 1000), new Vec3(0, 0, m_camera.Position.Z + 1000), Color.Blue);
        }

        public void DrawGizmo(Vec3 pos)
        {
            int size = 3;

            DrawLine(new Vec3(pos.X - size, 0, 0), new Vec3(pos.X + size, 0, 0), Color.Red);
            DrawLine(new Vec3(0, pos.Y - size, 0), new Vec3(0, pos.Y + size, 0), Color.Green);
            DrawLine(new Vec3(0, 0, pos.Z - size), new Vec3(0, 0, pos.Z + size), Color.Blue);
        }

        internal void Render(float dt)
        {
            DrawDebugGrid();
        }
    }
}

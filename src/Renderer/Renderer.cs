// TODO: Fix Camera Rotation
using OpenTK.Graphics.OpenGL.Compatibility;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using SpotEngine.Internal.Rendering;

namespace SpotEngine.Rendering
{
    public class Renderer
    {
        private InternalCamera camera = new InternalCamera { Position = new Vec3(0.0f, 0.0f, 3.0f) };
        private ShaderManager m_shaderManager;
        private VAOManager m_vaoManager;
        private IGLFWGraphicsContext m_glContext;
        public InternalCamera Camera => camera;
        internal Renderer(IGLFWGraphicsContext context)
        {
            m_glContext = context;
            m_shaderManager = new ShaderManager();
            m_vaoManager = new VAOManager();

            m_shaderManager.GetShader("default");

            InitializeSquare();
        }

        private void InitializeSquare()
        {
            float[] squareVertices = {
                -0.5f, -0.5f, 0.0f,
                 0.5f, -0.5f, 0.0f,
                -0.5f,  0.5f, 0.0f,
                 0.5f,  0.5f, 0.0f
            };

            m_vaoManager.GetVAO("square", squareVertices);
        }

        public void SetCameraPosition(Vec3 position)
        {
            camera.SetPosition(position);
        }

        public void SetCameraRotation(Vec3 rotation)
        {
            camera.SetRotation(rotation);
        }

        public void DrawObject(VAO vao, Transform transform, Color color)
        {
            Shader shader = m_shaderManager.GetShader("default");
            shader.Use();

            Matrix4 model = CreateModelMatrix(transform);

            Matrix4 view = camera.GetViewMatrix();

            Matrix4 projection = camera.GetProjectionMatrix(Application.Window.Width / (float)Application.Window.Height);

            shader.SetMatrix4("uModel", model);
            shader.SetMatrix4("uView", view);
            shader.SetMatrix4("uProjection", projection);

            shader.SetVector4("uColor", new Vector4(color.R, color.G, color.B, color.A));

            vao.Bind();
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        }

        internal void DrawObject(string vaoName, Transform transform, Color color)
        {
            VAO vao = m_vaoManager.GetVAO(vaoName, new float[] { });

            DrawObject(vao, transform, color);
        }

        public void DrawQuad(Transform transform, Color color)
        {
            DrawObject("square", transform, color);
        }

        internal void DrawTriangle(Transform transform, Color color)
        {
            string vaoKey = $"triangle_{transform.Scale.X}_{transform.Scale.Y}";
            var vao = m_vaoManager.GetVAO(vaoKey, GenerateTriangleVertices());

            DrawObject(vao, transform, color);
        }

        private float[] GenerateTriangleVertices()
        {
            return new float[]
            {
                0.0f,  0.5f, 0.0f,
               -0.5f, -0.5f, 0.0f,
                0.5f, -0.5f, 0.0f 
            };
        }

        private Matrix4 CreateModelMatrix(Transform transform)
        {
            Matrix4 model = Matrix4.CreateTranslation(transform.Pos.X, transform.Pos.Y, transform.Pos.Z) *
                            Matrix4.CreateRotationZ(transform.Rot.Z) *
                            Matrix4.CreateRotationY(transform.Rot.Y) *
                            Matrix4.CreateRotationX(transform.Rot.X) *
                            Matrix4.CreateScale(new Vector3(transform.Scale.X, transform.Scale.Y, 1.0f));

            return model;
        }

    }

}
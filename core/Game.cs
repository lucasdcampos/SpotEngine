using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.ComponentModel;
using OpenTK.Mathematics;
namespace SpotEngine
{
    public class Game : GameWindow
    {
        public List<Entity> entities = new List<Entity>();

        public int width, height;

        float[] vertices =
        {
            0f, 0.5f, 0f,
            -0.5f, -0.5f, 0f,
            0.5f, -0.5f, 0f
        };

        int vao;
        int shaderProgram;
        int vbo;

        public Game(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings)
            : base(gameWindowSettings, nativeWindowSettings)
        {

        }

        protected override void OnLoad()
        {
            base.OnLoad();

            InitEntities();

            vao = GL.GenVertexArray();

            vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);


            GL.BindVertexArray(vao);

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);

            GL.EnableVertexArrayAttrib(vao, 0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);


            shaderProgram = GL.CreateProgram();


            int vertexShader = GL.CreateShader(ShaderType.VertexShader);

            GL.ShaderSource(vertexShader, LoadShaderSource("Default.vert"));

            GL.CompileShader(vertexShader);

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, LoadShaderSource("Default.frag"));
            GL.CompileShader(fragmentShader);

            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);

            GL.LinkProgram(shaderProgram);

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        public void InitEntities()
        {
            foreach (var entity in entities)
            {
                entity.Init();
            }
        }

        public void UpdateEntities(float deltaTime)
        {
            foreach (var entity in entities)
            {
                entity.Flow(deltaTime);
            }
        }

        public void GetEntitiesByName()
        {
            foreach (var entity in entities)
            {
                Console.WriteLine(entity.name);
            }
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);

            float deltaTime = (float)e.Time;

            UpdateEntities(deltaTime);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            DrawTriangle();

            
            GL.ClearColor(new Color4(0.2f, 0.25f, 0.27f, 1f));
            GL.Clear(ClearBufferMask.ColorBufferBit);

            base.OnRenderFrame(e);

        }

        protected override void OnResize(ResizeEventArgs e)
        {
            
            base.OnResize(e); 

            GL.Viewport(0,0, e.Width, e.Height);

            this.width = e.Width;
            this.height = e.Height;

        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

        }

        
        protected override void OnUnload()
        {
            base.OnUnload();

            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
            GL.DeleteProgram(shaderProgram);
        }

        void DrawTriangle()
        {
            GL.UseProgram(shaderProgram);
            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            Context.SwapBuffers();
        }

        // Function to load a text file and return its contents as a string
        public static string LoadShaderSource(string filePath)
        {
            string shaderSource = "";

            try
            {
                using (StreamReader reader = new StreamReader("../../../shaders/" + filePath))
                {
                    
                    shaderSource = reader.ReadToEnd();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to load shader source file: " + e.Message);
            }

            return shaderSource;
        }


    }

}

using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using SpotEngine.Rendering;


namespace SpotEngine.Internal.Rendering
{
    public class Shader
    {
        public ProgramHandle Handle { get; private set; }

        public Shader(string vertexSource, string fragmentSource)
        {
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexSource);
            GL.CompileShader(vertexShader);

            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentSource);
            GL.CompileShader(fragmentShader);

            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vertexShader);
            GL.AttachShader(Handle, fragmentShader);
            GL.LinkProgram(Handle);

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        public void SetMatrix4(string name, Matrix4 matrix)
        {
            int location = GL.GetUniformLocation(Handle, name);
            if (location == -1)
            {
                Console.WriteLine($"Uniform '{name}' not found in shader!");
                return;
            }

            GL.UniformMatrix4f(location, false, ref matrix);

        }

        public void SetVector4(string name, Vector4 vector)
        {
            int location = GL.GetUniformLocation(Handle, name);
            if (location == -1)
            {
                Console.WriteLine($"Uniform '{name}' not found in shader!");
                return;
            }

            GL.Uniform4f(location, vector);
        }

        public static Shader GetDefault()
        {
            return new Shader(DefaultShaders.DefaultVertexShader, DefaultShaders.DefaultFragmentShader);
        }

        public void Use() => GL.UseProgram(Handle);
    }
}

using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using SpotEngine.Rendering;


namespace SpotEngine.Internal.Rendering
{
    public class Shader : IDisposable
    {
        public ProgramHandle Handle { get; private set; }

        public Shader(string vertexSource, string fragmentSource)
        {
            var vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexSource);
            GL.CompileShader(vertexShader);
            CheckShaderCompileErrors(vertexShader, ShaderType.VertexShader);

            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentSource);
            GL.CompileShader(fragmentShader);
            CheckShaderCompileErrors(fragmentShader, ShaderType.FragmentShader);

            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vertexShader);
            GL.AttachShader(Handle, fragmentShader);
            GL.LinkProgram(Handle);
            CheckProgramLinkErrors();

            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        private void CheckShaderCompileErrors(ShaderHandle shader, ShaderType type)
        {
            int status;
            unsafe
            {
                GL.GetShaderiv(shader, ShaderParameterName.CompileStatus, &status);
            }

            if (status == 0)
            {
                string infoLog;
                GL.GetShaderInfoLog(shader, out infoLog);
                Log.Error($"Error compiling {type}: {infoLog}");
            }
        }


        private void CheckProgramLinkErrors()
        {
            int status;
            unsafe
            {
                GL.GetProgramiv(Handle, ProgramPropertyARB.LinkStatus, &status);

            }
            if (status == 0)
            {
                string infoLog;
                GL.GetProgramInfoLog(Handle, out infoLog);
                Log.Error($"Error linking shader program: {infoLog}");
            }
        }

        public void SetUniform(string name, float value) => GL.Uniform1f(GetUniformLocation(name), value);
        public void SetUniform(string name, int value) => GL.Uniform1i(GetUniformLocation(name), value);
        public void SetUniform(string name, Vector3 vector) => GL.Uniform3f(GetUniformLocation(name), vector);
        public void SetUniform(string name, Vector4 vector) => GL.Uniform4f(GetUniformLocation(name), vector);
        public void SetUniform(string name, Matrix4 matrix) => GL.UniformMatrix4f(GetUniformLocation(name), false, ref matrix);

        private int GetUniformLocation(string name)
        {
            int location = GL.GetUniformLocation(Handle, name);
            if (location == -1)
                Log.Error($"Uniform '{name}' not found in shader!");

            return location;
        }

        public void Use() => GL.UseProgram(Handle);

        private static Shader s_defaultShader;
        public static Shader GetDefault()
        {
            return s_defaultShader ??= new Shader(DefaultShaders.DefaultVertexShader, DefaultShaders.DefaultFragmentShader);
        }

        public void Dispose()
        {
            if (Handle.Handle != 0)
            {
                GL.DeleteProgram(Handle);
                Handle = ProgramHandle.Zero;
            }
        }
    }

}

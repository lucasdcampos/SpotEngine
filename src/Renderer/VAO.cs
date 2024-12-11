using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace SpotEngine.Internal.Rendering
{
    public class VAO : IDisposable
    {
        private VertexArrayHandle vao;
        private BufferHandle vbo;

        public VAO(float[] vertices, int stride, params (int Location, int Size, int Offset)[] attributes)
        {
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

            unsafe
            {
                fixed (float* vertexPtr = vertices)
                {
                    GL.BufferData(BufferTargetARB.ArrayBuffer, vertices.Length * sizeof(float), (IntPtr)vertexPtr, BufferUsageARB.StaticDraw);
                }
            }

            foreach (var (location, size, offset) in attributes)
            {
                GL.VertexAttribPointer((uint)location, size, VertexAttribPointerType.Float, false, stride, offset);
                GL.EnableVertexAttribArray((uint)location);
            }

            GL.BindBuffer(BufferTargetARB.ArrayBuffer, BufferHandle.Zero);
            GL.BindVertexArray(VertexArrayHandle.Zero);
        }

        public void UpdateVertices(float[] newVertices)
        {
            GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            unsafe
            {
                fixed (float* vertexPtr = newVertices)
                {
                    GL.BufferSubData(BufferTargetARB.ArrayBuffer, IntPtr.Zero, newVertices.Length * sizeof(float), vertexPtr);
                }
            }
        }


        public void Bind() => GL.BindVertexArray(vao);

        public void Dispose()
        {
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
        }
    }


}

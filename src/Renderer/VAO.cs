using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL.Compatibility;

namespace SpotEngine.Internal.Rendering
{
    public class VAO
    {
        private VertexArrayHandle vao;
        private BufferHandle vbo;

        public VAO(float[] vertices)
        {
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

            unsafe
            {
                fixed (float* vertexPtr = &vertices[0])
                {
                    GL.BufferData(BufferTargetARB.ArrayBuffer, vertices.Length * sizeof(float), (IntPtr)vertexPtr, BufferUsageARB.StaticDraw);
                }
            }

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTargetARB.ArrayBuffer, BufferHandle.Zero);
            GL.BindVertexArray(VertexArrayHandle.Zero);
        }

        public void Bind() => GL.BindVertexArray(vao);
    }

}

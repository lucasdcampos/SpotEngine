using Silk.NET.OpenGL;

namespace Spot.Rendering;

/// <summary>
/// The high-level rendering facade. Wraps the underlying graphics API so callers never
/// touch the raw OpenGL context directly.
/// </summary>
public static class Renderer
{
    private static GL? s_gl;
    private static IGraphicsDevice? s_device;

    /// <summary>
    /// Gets the underlying OpenGL API. Used internally by rendering resources not yet migrated to
    /// <see cref="Device"/>.
    /// </summary>
    internal static GL Gl =>
        s_gl ?? throw new InvalidOperationException("The renderer has not been initialized.");

    /// <summary>
    /// Gets the active graphics device. Rendering resources issue all GPU commands through this so the
    /// same code runs on the desktop OpenGL backend and the browser WebGL2 backend.
    /// </summary>
    internal static IGraphicsDevice Device =>
        s_device ?? throw new InvalidOperationException("The renderer has not been initialized.");

    /// <summary>
    /// Gets the raw OpenGL API as a low-level escape hatch, for rendering the engine's abstractions
    /// do not cover. Using it couples your code to Silk.NET, so prefer the higher-level APIs
    /// (<see cref="Renderer"/>, <see cref="Renderer2D"/>) when they suffice.
    /// </summary>
    public static GL Api => Gl;

    /// <summary>
    /// Initializes the renderer with the active OpenGL context. Called once by the application.
    /// </summary>
    /// <param name="gl">The OpenGL API for the current context.</param>
    internal static void Init(GL gl)
    {
        s_gl = gl;
        s_device = new OpenGLGraphicsDevice(gl);
    }

    /// <summary>
    /// Sets the color used to clear the screen.
    /// </summary>
    /// <param name="r">The red component, in the range [0, 1].</param>
    /// <param name="g">The green component, in the range [0, 1].</param>
    /// <param name="b">The blue component, in the range [0, 1].</param>
    /// <param name="a">The alpha component, in the range [0, 1].</param>
    public static void SetClearColor(float r, float g, float b, float a) => Device.SetClearColor(r, g, b, a);

    /// <summary>
    /// Clears the color and depth buffers.
    /// </summary>
    public static void Clear() => Device.Clear(color: true, depth: true);

    /// <summary>
    /// Clears only the depth buffer.
    /// </summary>
    public static void ClearDepth() => Device.Clear(color: false, depth: true);

    /// <summary>
    /// Enables or disables depth testing.
    /// </summary>
    /// <param name="enable">Whether depth testing should be enabled.</param>
    public static void SetDepthTest(bool enable) => Device.SetCapability(GraphicsCapability.DepthTest, enable);

    /// <summary>
    /// Enables or disables face culling.
    /// </summary>
    /// <param name="enable">Whether face culling should be enabled.</param>
    public static void SetFaceCulling(bool enable) => Device.SetCapability(GraphicsCapability.CullFace, enable);

    /// <summary>
    /// Sets the rendering viewport.
    /// </summary>
    /// <param name="x">The lower-left x coordinate, in pixels.</param>
    /// <param name="y">The lower-left y coordinate, in pixels.</param>
    /// <param name="width">The viewport width, in pixels.</param>
    /// <param name="height">The viewport height, in pixels.</param>
    public static void SetViewport(int x, int y, uint width, uint height) => Device.SetViewport(x, y, width, height);

    /// <summary>
    /// Draws the given vertex array as a list of triangles using its vertex data.
    /// </summary>
    /// <param name="vertexArray">The vertex array to draw.</param>
    /// <param name="vertexCount">The number of vertices to draw.</param>
    public static void DrawArrays(VertexArray vertexArray, uint vertexCount)
    {
        vertexArray.Bind();
        Device.DrawArrays(PrimitiveKind.Triangles, 0, vertexCount);
    }

    /// <summary>
    /// Draws the given vertex array as a list of triangles using its index buffer.
    /// </summary>
    /// <param name="vertexArray">The vertex array to draw. It must have an index buffer set.</param>
    public static void DrawIndexed(VertexArray vertexArray) => DrawIndexed(vertexArray, vertexArray.IndexCount);

    /// <summary>
    /// Draws the first <paramref name="indexCount"/> indices of the given vertex array as triangles.
    /// </summary>
    /// <param name="vertexArray">The vertex array to draw. It must have an index buffer set.</param>
    /// <param name="indexCount">The number of indices to draw.</param>
    public static void DrawIndexed(VertexArray vertexArray, uint indexCount)
    {
        vertexArray.Bind();
        Device.DrawElements(PrimitiveKind.Triangles, indexCount);
    }
}

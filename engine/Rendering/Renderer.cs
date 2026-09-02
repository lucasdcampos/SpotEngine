namespace Spot.Rendering;

/// <summary>
/// The high-level rendering facade. Wraps the underlying graphics API so callers never
/// touch the raw OpenGL context directly.
/// </summary>
/// <remarks>
/// The facade itself is backend-neutral: it issues every command through <see cref="IGraphicsDevice"/>, so
/// the same code drives the desktop OpenGL backend and the browser WebGL2 backend. The raw Silk.NET escape
/// hatch (<c>Api</c>) and the desktop <c>Init(GL)</c> entry point live in a desktop-only partial so the
/// browser build never sees them.
/// </remarks>
public static partial class Renderer
{
    private static IGraphicsDevice? s_device;

    /// <summary>
    /// Gets the active graphics device. Rendering resources issue all GPU commands through this so the
    /// same code runs on the desktop OpenGL backend and the browser WebGL2 backend.
    /// </summary>
    internal static IGraphicsDevice Device =>
        s_device ?? throw new InvalidOperationException("The renderer has not been initialized.");

    /// <summary>
    /// Initializes the renderer with a graphics device. Called once by the host (the desktop application
    /// wraps a Silk.NET OpenGL context; the browser host wraps a WebGL2 context).
    /// </summary>
    /// <param name="device">The graphics device backing every draw call.</param>
    internal static void Init(IGraphicsDevice device) => s_device = device;

    /// <summary>
    /// Gets the framebuffer currently bound as the draw target (tracked here rather than queried from the GPU,
    /// so passes can save and restore it without a backend-specific state query). Defaults to the screen.
    /// </summary>
    public static FramebufferHandle CurrentRenderTarget { get; private set; } = FramebufferHandle.Default;

    /// <summary>Gets the current viewport width in pixels (tracked as it is set).</summary>
    public static uint ViewportWidth { get; private set; }

    /// <summary>Gets the current viewport height in pixels (tracked as it is set).</summary>
    public static uint ViewportHeight { get; private set; }

    /// <summary>Gets the current viewport lower-left x, in pixels (tracked as it is set).</summary>
    public static int ViewportX { get; private set; }

    /// <summary>Gets the current viewport lower-left y, in pixels (tracked as it is set).</summary>
    public static int ViewportY { get; private set; }

    /// <summary>
    /// Binds a framebuffer as the draw target and sets its viewport in one step, tracking both so a later
    /// pass can restore them via <see cref="CurrentRenderTarget"/> and the viewport properties. Pass
    /// <see cref="FramebufferHandle.Default"/> to render to the screen.
    /// </summary>
    /// <param name="target">The framebuffer to bind.</param>
    /// <param name="x">The viewport lower-left x, in pixels.</param>
    /// <param name="y">The viewport lower-left y, in pixels.</param>
    /// <param name="width">The viewport width, in pixels.</param>
    /// <param name="height">The viewport height, in pixels.</param>
    public static void BindRenderTarget(FramebufferHandle target, int x, int y, uint width, uint height)
    {
        Device.BindFramebuffer(target);
        CurrentRenderTarget = target;
        SetViewport(x, y, width, height);
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
    /// Enables or disables writing to the depth buffer.
    /// </summary>
    /// <param name="write">Whether depth writes should be enabled.</param>
    public static void SetDepthWrite(bool write) => Device.SetDepthWrite(write);

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
    public static void SetViewport(int x, int y, uint width, uint height)
    {
        Device.SetViewport(x, y, width, height);
        ViewportX = x;
        ViewportY = y;
        ViewportWidth = width;
        ViewportHeight = height;
    }

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

    /// <summary>
    /// Draws <paramref name="instanceCount"/> copies of the given indexed geometry in a single call, each
    /// reading its own per-instance attributes. The vertex array must have an index buffer and a
    /// per-instance vertex buffer (see <see cref="VertexArray.AddInstancedVertexBuffer"/>).
    /// </summary>
    /// <param name="vertexArray">The instanced vertex array to draw. It must have an index buffer set.</param>
    /// <param name="indexCount">The number of indices per instance.</param>
    /// <param name="instanceCount">The number of instances to draw.</param>
    public static void DrawIndexedInstanced(VertexArray vertexArray, uint indexCount, uint instanceCount)
    {
        vertexArray.Bind();
        Device.DrawElementsInstanced(PrimitiveKind.Triangles, indexCount, instanceCount);
    }
}

namespace Spot.Rendering;

/// <summary>
/// The kind of primitive a draw call assembles from its vertices.
/// </summary>
public enum PrimitiveKind
{
    /// <summary>A list of independent triangles (three vertices each).</summary>
    Triangles,

    /// <summary>A connected strip of triangles.</summary>
    TriangleStrip,

    /// <summary>A list of independent line segments (two vertices each).</summary>
    Lines,
}

/// <summary>
/// A toggleable piece of fixed-function graphics state.
/// </summary>
public enum GraphicsCapability
{
    /// <summary>Depth testing against the depth buffer.</summary>
    DepthTest,

    /// <summary>Back/front face culling.</summary>
    CullFace,

    /// <summary>Alpha blending of fragments with the framebuffer.</summary>
    Blend,

    /// <summary>Scissor-rectangle clipping.</summary>
    ScissorTest,
}

/// <summary>
/// The role a GPU buffer plays in a draw call.
/// </summary>
public enum BufferKind
{
    /// <summary>A buffer of vertex attribute data.</summary>
    Vertex,

    /// <summary>A buffer of element indices for indexed drawing.</summary>
    Index,
}

/// <summary>
/// A hint describing how often a buffer's contents change, so the backend can place it well.
/// </summary>
public enum BufferUsageKind
{
    /// <summary>Uploaded once and drawn many times.</summary>
    StaticDraw,

    /// <summary>Re-uploaded frequently (for example every frame).</summary>
    DynamicDraw,
}

/// <summary>
/// The base component type of a vertex attribute.
/// </summary>
public enum VertexAttribType
{
    /// <summary>32-bit floating point.</summary>
    Float,

    /// <summary>32-bit signed integer.</summary>
    Int,

    /// <summary>8-bit unsigned integer.</summary>
    UnsignedByte,
}

/// <summary>An opaque handle to a GPU buffer object.</summary>
/// <param name="Id">The backend-specific identifier.</param>
public readonly record struct BufferHandle(uint Id);

/// <summary>An opaque handle to a GPU vertex array object.</summary>
/// <param name="Id">The backend-specific identifier.</param>
public readonly record struct VertexArrayHandle(uint Id);

/// <summary>
/// A minimal graphics API abstraction. The engine issues all GPU commands through this interface so
/// the same rendering code runs on a desktop OpenGL backend and, later, a browser WebGL2 backend.
/// </summary>
/// <remarks>
/// The surface is deliberately close to OpenGL/WebGL2 semantics (bind-then-operate, integer handles)
/// but uses engine-neutral types so no caller is coupled to a particular graphics library.
/// </remarks>
public interface IGraphicsDevice
{
    /// <summary>Sets the color the framebuffer is cleared to, each component in the range [0, 1].</summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    /// <param name="a">The alpha component.</param>
    void SetClearColor(float r, float g, float b, float a);

    /// <summary>Clears the requested buffers to their clear values.</summary>
    /// <param name="color">Whether to clear the color buffer.</param>
    /// <param name="depth">Whether to clear the depth buffer.</param>
    void Clear(bool color, bool depth);

    /// <summary>Enables or disables a piece of fixed-function state.</summary>
    /// <param name="capability">The capability to toggle.</param>
    /// <param name="enabled">Whether the capability should be enabled.</param>
    void SetCapability(GraphicsCapability capability, bool enabled);

    /// <summary>Sets the rendering viewport, in pixels, with the origin at the lower left.</summary>
    /// <param name="x">The lower-left x coordinate.</param>
    /// <param name="y">The lower-left y coordinate.</param>
    /// <param name="width">The viewport width.</param>
    /// <param name="height">The viewport height.</param>
    void SetViewport(int x, int y, uint width, uint height);

    /// <summary>Draws vertices from the currently bound vertex array.</summary>
    /// <param name="primitive">The primitive kind to assemble.</param>
    /// <param name="first">The index of the first vertex to draw.</param>
    /// <param name="count">The number of vertices to draw.</param>
    void DrawArrays(PrimitiveKind primitive, uint first, uint count);

    /// <summary>Draws using the 32-bit indices of the currently bound vertex array, starting at offset zero.</summary>
    /// <param name="primitive">The primitive kind to assemble.</param>
    /// <param name="count">The number of indices to draw.</param>
    void DrawElements(PrimitiveKind primitive, uint count);

    /// <summary>Creates a new, uninitialized GPU buffer.</summary>
    /// <returns>A handle to the new buffer.</returns>
    BufferHandle CreateBuffer();

    /// <summary>Binds a buffer to the slot for its kind.</summary>
    /// <param name="kind">The buffer's role.</param>
    /// <param name="handle">The buffer to bind.</param>
    void BindBuffer(BufferKind kind, BufferHandle handle);

    /// <summary>Uploads data to the bound buffer of the given kind, sizing it to the data.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="kind">The buffer's role.</param>
    /// <param name="data">The data to upload.</param>
    /// <param name="usage">How the buffer will be used.</param>
    void BufferData<T>(BufferKind kind, ReadOnlySpan<T> data, BufferUsageKind usage)
        where T : unmanaged;

    /// <summary>Allocates uninitialized storage of the given byte size in the bound buffer.</summary>
    /// <param name="kind">The buffer's role.</param>
    /// <param name="sizeInBytes">The number of bytes to allocate.</param>
    /// <param name="usage">How the buffer will be used.</param>
    void BufferData(BufferKind kind, nuint sizeInBytes, BufferUsageKind usage);

    /// <summary>Replaces a region of the bound buffer starting at the given byte offset.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="kind">The buffer's role.</param>
    /// <param name="offsetInBytes">The byte offset to write at.</param>
    /// <param name="data">The data to upload.</param>
    void BufferSubData<T>(BufferKind kind, nint offsetInBytes, ReadOnlySpan<T> data)
        where T : unmanaged;

    /// <summary>Deletes a GPU buffer.</summary>
    /// <param name="handle">The buffer to delete.</param>
    void DeleteBuffer(BufferHandle handle);

    /// <summary>Creates a new vertex array object.</summary>
    /// <returns>A handle to the new vertex array.</returns>
    VertexArrayHandle CreateVertexArray();

    /// <summary>Binds a vertex array object.</summary>
    /// <param name="handle">The vertex array to bind.</param>
    void BindVertexArray(VertexArrayHandle handle);

    /// <summary>Enables the vertex attribute at the given index on the bound vertex array.</summary>
    /// <param name="index">The attribute index.</param>
    void EnableVertexAttribArray(uint index);

    /// <summary>Describes the memory layout of a vertex attribute in the bound vertex buffer.</summary>
    /// <param name="index">The attribute index.</param>
    /// <param name="size">The number of components.</param>
    /// <param name="type">The component base type.</param>
    /// <param name="normalized">Whether integer components are normalized to [0, 1]/[-1, 1].</param>
    /// <param name="stride">The byte distance between consecutive vertices.</param>
    /// <param name="offset">The byte offset of this attribute within a vertex.</param>
    void VertexAttribPointer(uint index, int size, VertexAttribType type, bool normalized, uint stride, nint offset);

    /// <summary>Deletes a vertex array object.</summary>
    /// <param name="handle">The vertex array to delete.</param>
    void DeleteVertexArray(VertexArrayHandle handle);
}

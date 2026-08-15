using System.Numerics;

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

/// <summary>
/// A programmable stage of the graphics pipeline.
/// </summary>
public enum ShaderStage
{
    /// <summary>The per-vertex stage.</summary>
    Vertex,

    /// <summary>The per-fragment stage.</summary>
    Fragment,
}

/// <summary>
/// How a texture is sampled between texels.
/// </summary>
public enum TextureFilter
{
    /// <summary>Nearest-neighbor sampling.</summary>
    Nearest,

    /// <summary>Linear interpolation within the base level.</summary>
    Linear,

    /// <summary>Trilinear: linear within and between mipmap levels.</summary>
    LinearMipmapLinear,
}

/// <summary>
/// How texture coordinates outside [0, 1] are resolved.
/// </summary>
public enum TextureWrap
{
    /// <summary>Tile the texture.</summary>
    Repeat,

    /// <summary>Clamp to the edge texel.</summary>
    ClampToEdge,
}

/// <summary>An opaque handle to a GPU buffer object.</summary>
/// <param name="Id">The backend-specific identifier.</param>
public readonly record struct BufferHandle(uint Id);

/// <summary>An opaque handle to a GPU vertex array object.</summary>
/// <param name="Id">The backend-specific identifier.</param>
public readonly record struct VertexArrayHandle(uint Id);

/// <summary>An opaque handle to a compiled shader stage.</summary>
/// <param name="Id">The backend-specific identifier.</param>
public readonly record struct ShaderHandle(uint Id);

/// <summary>An opaque handle to a linked shader program.</summary>
/// <param name="Id">The backend-specific identifier.</param>
public readonly record struct ProgramHandle(uint Id);

/// <summary>An opaque handle to a GPU texture.</summary>
/// <param name="Id">The backend-specific identifier.</param>
public readonly record struct TextureHandle(uint Id);

/// <summary>The location of a uniform within a linked program, or -1 if absent.</summary>
/// <param name="Location">The backend-specific location.</param>
public readonly record struct UniformLocation(int Location);

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

    /// <summary>Creates a shader object for the given pipeline stage.</summary>
    /// <param name="stage">The pipeline stage.</param>
    /// <returns>A handle to the new shader.</returns>
    ShaderHandle CreateShader(ShaderStage stage);

    /// <summary>Sets a shader's GLSL source.</summary>
    /// <param name="shader">The shader.</param>
    /// <param name="source">The GLSL source text.</param>
    void ShaderSource(ShaderHandle shader, string source);

    /// <summary>Compiles a shader.</summary>
    /// <param name="shader">The shader to compile.</param>
    void CompileShader(ShaderHandle shader);

    /// <summary>Gets whether a shader compiled successfully.</summary>
    /// <param name="shader">The shader.</param>
    /// <returns><see langword="true"/> if compilation succeeded.</returns>
    bool GetShaderCompileStatus(ShaderHandle shader);

    /// <summary>Gets a shader's compile log.</summary>
    /// <param name="shader">The shader.</param>
    /// <returns>The info log, possibly empty.</returns>
    string GetShaderInfoLog(ShaderHandle shader);

    /// <summary>Creates an empty shader program.</summary>
    /// <returns>A handle to the new program.</returns>
    ProgramHandle CreateProgram();

    /// <summary>Attaches a shader stage to a program.</summary>
    /// <param name="program">The program.</param>
    /// <param name="shader">The shader to attach.</param>
    void AttachShader(ProgramHandle program, ShaderHandle shader);

    /// <summary>Links a program from its attached shaders.</summary>
    /// <param name="program">The program to link.</param>
    void LinkProgram(ProgramHandle program);

    /// <summary>Gets whether a program linked successfully.</summary>
    /// <param name="program">The program.</param>
    /// <returns><see langword="true"/> if linking succeeded.</returns>
    bool GetProgramLinkStatus(ProgramHandle program);

    /// <summary>Gets a program's link log.</summary>
    /// <param name="program">The program.</param>
    /// <returns>The info log, possibly empty.</returns>
    string GetProgramInfoLog(ProgramHandle program);

    /// <summary>Detaches a shader stage from a program.</summary>
    /// <param name="program">The program.</param>
    /// <param name="shader">The shader to detach.</param>
    void DetachShader(ProgramHandle program, ShaderHandle shader);

    /// <summary>Deletes a shader object.</summary>
    /// <param name="shader">The shader to delete.</param>
    void DeleteShader(ShaderHandle shader);

    /// <summary>Deletes a shader program.</summary>
    /// <param name="program">The program to delete.</param>
    void DeleteProgram(ProgramHandle program);

    /// <summary>Makes a program current for subsequent draw calls.</summary>
    /// <param name="program">The program to use.</param>
    void UseProgram(ProgramHandle program);

    /// <summary>Looks up a uniform's location within a program.</summary>
    /// <param name="program">The program.</param>
    /// <param name="name">The uniform name.</param>
    /// <returns>The location, whose value is -1 when the uniform is absent.</returns>
    UniformLocation GetUniformLocation(ProgramHandle program, string name);

    /// <summary>Sets an <see cref="int"/> uniform on the current program.</summary>
    /// <param name="location">The uniform location.</param>
    /// <param name="value">The value.</param>
    void SetUniform(UniformLocation location, int value);

    /// <summary>Sets a <see cref="float"/> uniform on the current program.</summary>
    /// <param name="location">The uniform location.</param>
    /// <param name="value">The value.</param>
    void SetUniform(UniformLocation location, float value);

    /// <summary>Sets a <c>vec2</c> uniform on the current program.</summary>
    /// <param name="location">The uniform location.</param>
    /// <param name="value">The value.</param>
    void SetUniform(UniformLocation location, Vector2 value);

    /// <summary>Sets a <c>vec3</c> uniform on the current program.</summary>
    /// <param name="location">The uniform location.</param>
    /// <param name="value">The value.</param>
    void SetUniform(UniformLocation location, Vector3 value);

    /// <summary>Sets a <c>vec4</c> uniform on the current program.</summary>
    /// <param name="location">The uniform location.</param>
    /// <param name="value">The value.</param>
    void SetUniform(UniformLocation location, Vector4 value);

    /// <summary>Sets a <c>mat4</c> (or <c>mat4</c> array) uniform, uploaded untransposed.</summary>
    /// <param name="location">The uniform location.</param>
    /// <param name="values">One matrix per array element.</param>
    void SetUniformMatrix4(UniformLocation location, ReadOnlySpan<Matrix4x4> values);

    /// <summary>Creates a new, uninitialized 2D texture.</summary>
    /// <returns>A handle to the new texture.</returns>
    TextureHandle CreateTexture();

    /// <summary>Binds a 2D texture to a texture unit.</summary>
    /// <param name="unit">The texture unit index.</param>
    /// <param name="handle">The texture to bind.</param>
    void BindTexture(uint unit, TextureHandle handle);

    /// <summary>Sets the wrap mode for both axes of the bound 2D texture.</summary>
    /// <param name="wrap">The wrap mode.</param>
    void SetTextureWrap(TextureWrap wrap);

    /// <summary>Sets the minification and magnification filters of the bound 2D texture.</summary>
    /// <param name="minFilter">The minification filter.</param>
    /// <param name="magFilter">The magnification filter.</param>
    void SetTextureFilter(TextureFilter minFilter, TextureFilter magFilter);

    /// <summary>Gets the maximum supported anisotropy, or 1 if anisotropic filtering is unavailable.</summary>
    /// <returns>The maximum anisotropy.</returns>
    float GetMaxAnisotropy();

    /// <summary>Sets the anisotropy level of the bound 2D texture. A no-op where unsupported.</summary>
    /// <param name="anisotropy">The desired anisotropy.</param>
    void SetTextureMaxAnisotropy(float anisotropy);

    /// <summary>Uploads RGBA8 pixels to the bound 2D texture, sizing its base level.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="rgba">The pixel data, four bytes (R, G, B, A) per pixel.</param>
    void TextureImage2DRgba8(uint width, uint height, ReadOnlySpan<byte> rgba);

    /// <summary>Generates the mipmap chain for the bound 2D texture.</summary>
    void GenerateMipmap2D();

    /// <summary>Deletes a texture.</summary>
    /// <param name="handle">The texture to delete.</param>
    void DeleteTexture(TextureHandle handle);
}

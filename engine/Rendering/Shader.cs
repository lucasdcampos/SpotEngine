using System.Numerics;
using Spot.Core;

namespace Spot.Rendering;

/// <summary>
/// A compiled and linked shader program.
/// </summary>
public sealed class Shader : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly ProgramHandle _handle;
    private readonly Dictionary<string, UniformLocation> _uniformLocations = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Shader"/> class by compiling and linking the given sources.
    /// </summary>
    /// <param name="vertexSource">The GLSL source for the vertex stage.</param>
    /// <param name="fragmentSource">The GLSL source for the fragment stage.</param>
    public Shader(string vertexSource, string fragmentSource)
    {
        _device = Renderer.Device;

        ShaderHandle vertex = CompileShader(ShaderStage.Vertex, vertexSource);
        ShaderHandle fragment = CompileShader(ShaderStage.Fragment, fragmentSource);

        _handle = _device.CreateProgram();
        _device.AttachShader(_handle, vertex);
        _device.AttachShader(_handle, fragment);
        _device.LinkProgram(_handle);

        if (!_device.GetProgramLinkStatus(_handle))
        {
            Log.CoreError("Shader program link failed: {0}", _device.GetProgramInfoLog(_handle));
        }

        _device.DetachShader(_handle, vertex);
        _device.DetachShader(_handle, fragment);
        _device.DeleteShader(vertex);
        _device.DeleteShader(fragment);
    }

    /// <summary>
    /// Activates the shader program for subsequent draw calls.
    /// </summary>
    public void Use() => _device.UseProgram(_handle);

    /// <summary>
    /// Sets an <see cref="int"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUniform(string name, int value) => _device.SetUniform(GetUniformLocation(name), value);

    /// <summary>
    /// Sets a <see cref="float"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUniform(string name, float value) => _device.SetUniform(GetUniformLocation(name), value);

    /// <summary>
    /// Sets a <see cref="Vector2"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUniform(string name, Vector2 value) => _device.SetUniform(GetUniformLocation(name), value);

    /// <summary>
    /// Sets a <see cref="Vector3"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUniform(string name, Vector3 value) => _device.SetUniform(GetUniformLocation(name), value);

    /// <summary>
    /// Sets a <see cref="Vector4"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUniform(string name, Vector4 value) => _device.SetUniform(GetUniformLocation(name), value);

    /// <summary>
    /// Sets a <see cref="Matrix4x4"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    /// <remarks>
    /// The matrix is uploaded untransposed: <see cref="Matrix4x4"/>'s row-major memory layout
    /// matches the column-major matrix GLSL expects for the same transform.
    /// </remarks>
    public void SetUniform(string name, Matrix4x4 value)
    {
        Span<Matrix4x4> one = stackalloc Matrix4x4[1];
        one[0] = value;
        _device.SetUniformMatrix4(GetUniformLocation(name), one);
    }

    /// <summary>
    /// Sets a <c>mat4</c> array uniform (for example a skinning bone palette) from a span of matrices.
    /// </summary>
    /// <param name="name">The uniform array name (its base, e.g. <c>uBones</c>).</param>
    /// <param name="values">The matrices to upload, one per array element.</param>
    /// <remarks>Like the single-matrix overload, matrices are uploaded untransposed (see <see cref="SetUniform(string, Matrix4x4)"/>).</remarks>
    public void SetUniform(string name, ReadOnlySpan<Matrix4x4> values)
    {
        if (values.IsEmpty)
        {
            return;
        }

        _device.SetUniformMatrix4(GetUniformLocation(name), values);
    }

    /// <inheritdoc />
    public void Dispose() => _device.DeleteProgram(_handle);

    private UniformLocation GetUniformLocation(string name)
    {
        // Uniform locations are fixed for the life of a linked program, so look each one up once and
        // cache it. This turns a per-draw uniform lookup (dozens per mesh per frame) into a
        // dictionary hit, and — because the miss is cached too — warns at most once per missing name
        // instead of every frame.
        if (_uniformLocations.TryGetValue(name, out UniformLocation cached))
        {
            return cached;
        }

        UniformLocation location = _device.GetUniformLocation(_handle, name);
        if (location.Location == -1)
        {
            Log.CoreWarn("Uniform '{0}' not found in shader program.", name);
        }

        _uniformLocations[name] = location;
        return location;
    }

    private ShaderHandle CompileShader(ShaderStage stage, string source)
    {
        ShaderHandle shader = _device.CreateShader(stage);
        _device.ShaderSource(shader, _device.PreprocessShaderSource(stage, source));
        _device.CompileShader(shader);

        if (!_device.GetShaderCompileStatus(shader))
        {
            Log.CoreError("{0} shader compilation failed: {1}", stage, _device.GetShaderInfoLog(shader));
        }

        return shader;
    }
}

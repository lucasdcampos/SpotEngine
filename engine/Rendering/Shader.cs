using System.Numerics;
using Silk.NET.OpenGL;
using Spot.Core;

namespace Spot.Rendering;

/// <summary>
/// A compiled and linked OpenGL shader program.
/// </summary>
public sealed class Shader : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private readonly Dictionary<string, int> _uniformLocations = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Shader"/> class by compiling and linking the given sources.
    /// </summary>
    /// <param name="vertexSource">The GLSL source for the vertex stage.</param>
    /// <param name="fragmentSource">The GLSL source for the fragment stage.</param>
    public Shader(string vertexSource, string fragmentSource)
    {
        _gl = Renderer.Gl;

        uint vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        uint fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);

        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);

        _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            Log.CoreError("Shader program link failed: {0}", _gl.GetProgramInfoLog(_handle));
        }

        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }

    /// <summary>
    /// Activates the shader program for subsequent draw calls.
    /// </summary>
    public void Use() => _gl.UseProgram(_handle);

    /// <summary>
    /// Sets an <see cref="int"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUniform(string name, int value) => _gl.Uniform1(GetUniformLocation(name), value);

    /// <summary>
    /// Sets a <see cref="float"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUniform(string name, float value) => _gl.Uniform1(GetUniformLocation(name), value);

    /// <summary>
    /// Sets a <see cref="Vector2"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUniform(string name, Vector2 value) =>
        _gl.Uniform2(GetUniformLocation(name), value.X, value.Y);

    /// <summary>
    /// Sets a <see cref="Vector3"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUniform(string name, Vector3 value) =>
        _gl.Uniform3(GetUniformLocation(name), value.X, value.Y, value.Z);

    /// <summary>
    /// Sets a <see cref="Vector4"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    public void SetUniform(string name, Vector4 value) =>
        _gl.Uniform4(GetUniformLocation(name), value.X, value.Y, value.Z, value.W);

    /// <summary>
    /// Sets a <see cref="Matrix4x4"/> uniform.
    /// </summary>
    /// <param name="name">The uniform name.</param>
    /// <param name="value">The value to set.</param>
    /// <remarks>
    /// The matrix is uploaded untransposed: <see cref="Matrix4x4"/>'s row-major memory layout
    /// matches the column-major matrix GLSL expects for the same transform.
    /// </remarks>
    public unsafe void SetUniform(string name, Matrix4x4 value) =>
        _gl.UniformMatrix4(GetUniformLocation(name), 1, false, (float*)&value);

    /// <summary>
    /// Sets a <c>mat4</c> array uniform (for example a skinning bone palette) from a span of matrices.
    /// </summary>
    /// <param name="name">The uniform array name (its base, e.g. <c>uBones</c>).</param>
    /// <param name="values">The matrices to upload, one per array element.</param>
    /// <remarks>Like the single-matrix overload, matrices are uploaded untransposed (see <see cref="SetUniform(string, Matrix4x4)"/>).</remarks>
    public unsafe void SetUniform(string name, ReadOnlySpan<Matrix4x4> values)
    {
        if (values.IsEmpty)
        {
            return;
        }

        fixed (Matrix4x4* ptr = values)
        {
            _gl.UniformMatrix4(GetUniformLocation(name), (uint)values.Length, false, (float*)ptr);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gl.DeleteProgram(_handle);

    private int GetUniformLocation(string name)
    {
        // Uniform locations are fixed for the life of a linked program, so look each one up once and
        // cache it. This turns a per-draw glGetUniformLocation (dozens per mesh per frame) into a
        // dictionary hit, and — because the miss is cached too — warns at most once per missing name
        // instead of every frame.
        if (_uniformLocations.TryGetValue(name, out int cached))
        {
            return cached;
        }

        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            Log.CoreWarn("Uniform '{0}' not found in shader program.", name);
        }

        _uniformLocations[name] = location;
        return location;
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            Log.CoreError("{0} compilation failed: {1}", type, _gl.GetShaderInfoLog(shader));
        }

        return shader;
    }
}

using System.Text;

namespace Spot.Rendering;

/// <summary>
/// Rewrites engine-authored desktop GLSL (<c>#version 330 core</c>) into the WebGL2 / OpenGL ES 3.0
/// dialect (<c>#version 300 es</c>) that the browser backend consumes.
/// </summary>
/// <remarks>
/// The engine's 2D and UI shaders are deliberately written in the subset shared by GLSL 3.30 core and
/// GLSL ES 3.00 — <c>in</c>/<c>out</c> stage variables, <c>layout(location = …)</c> attributes,
/// <c>texture()</c> sampling and a single <c>out</c> color — so the only differences are the version
/// directive and the precision qualifiers ES requires. This performs exactly that swap; it is not a
/// general-purpose GLSL translator, and shaders using desktop-only features (the 3D, post-processing
/// and shadow pipeline) are never routed through it.
/// </remarks>
internal static class GlslTranspiler
{
    /// <summary>
    /// Converts a desktop GLSL shader source to the equivalent OpenGL ES 3.0 (WebGL2) source.
    /// </summary>
    /// <param name="source">The desktop GLSL source, typically beginning with <c>#version 330 core</c>.</param>
    /// <returns>The equivalent OpenGL ES 3.0 source, beginning with <c>#version 300 es</c> and the ES precision defaults.</returns>
    public static string ToGlslEs300(string source)
    {
        string[] lines = source.Replace("\r\n", "\n").Split('\n');

        // The #version directive, if present, must be the first statement in the shader; locate it so it
        // can be replaced rather than duplicated (a second #version is a compile error).
        int versionLine = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("#version", StringComparison.Ordinal))
            {
                versionLine = i;
                break;
            }
        }

        var sb = new StringBuilder(source.Length + 64);

        // ES requires an explicit float precision in the fragment stage and permits redeclaring the
        // (already-highp) defaults in the vertex stage, so the same preamble is valid for both.
        sb.Append("#version 300 es\n");
        sb.Append("precision highp float;\n");
        sb.Append("precision highp int;\n");

        for (int i = 0; i < lines.Length; i++)
        {
            if (i == versionLine)
            {
                continue; // Replaced by the ES directive emitted above.
            }

            sb.Append(lines[i]);
            sb.Append('\n');
        }

        return sb.ToString();
    }
}

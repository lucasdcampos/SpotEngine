using Silk.NET.OpenGL;

namespace Spot.Rendering;

/// <summary>
/// The desktop half of <see cref="Renderer"/>: the raw Silk.NET OpenGL escape hatch and the OpenGL context
/// entry point. This partial is compiled only for the desktop target — the browser build drives the renderer
/// through <see cref="IGraphicsDevice"/> and a WebGL2 backend instead, and never references Silk.NET.
/// </summary>
public static partial class Renderer
{
    private static GL? s_gl;

    /// <summary>
    /// Gets the underlying OpenGL API. Used internally by desktop rendering resources not expressed through
    /// <see cref="Device"/>.
    /// </summary>
    internal static GL Gl =>
        s_gl ?? throw new InvalidOperationException("The renderer has not been initialized.");

    /// <summary>
    /// Gets the raw OpenGL API as a low-level escape hatch, for rendering the engine's abstractions
    /// do not cover. Using it couples your code to Silk.NET, so prefer the higher-level APIs
    /// (<see cref="Renderer"/>, <see cref="Renderer2D"/>) when they suffice.
    /// </summary>
    public static GL Api => Gl;

    /// <summary>
    /// Initializes the renderer with the active OpenGL context, wrapping it in the desktop graphics device.
    /// Called once by the desktop application.
    /// </summary>
    /// <param name="gl">The OpenGL API for the current context.</param>
    internal static void Init(GL gl)
    {
        s_gl = gl;
        Init(new OpenGLGraphicsDevice(gl));
    }
}

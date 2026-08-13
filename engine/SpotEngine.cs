using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Spot.Editor")]
[assembly: InternalsVisibleTo("Spot.DebugUI")]
[assembly: InternalsVisibleTo("Spot.Engine.Tests")]

namespace Spot;

/// <summary>
/// Provides top-level information about the Spot engine.
/// </summary>
public static class SpotEngine
{
    /// <summary>
    /// Gets the current engine version.
    /// </summary>
    /// <returns>The engine version string.</returns>
    public static string GetVersion() => "0.2.0";

    /// <summary>
    /// Creates a new engine application instance.
    /// </summary>
    /// <param name="spec">The application specification.</param>
    /// <returns>A new application instance.</returns>
    public static Core.Application CreateApplication(Core.ApplicationSpec? spec = null) => new Core.Application(spec);
}

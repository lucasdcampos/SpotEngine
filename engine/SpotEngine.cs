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

#if !BROWSER
    /// <summary>
    /// Creates a new engine application instance. Desktop only — the browser host drives its own RAF loop
    /// instead of the Silk.NET-backed <see cref="Core.Application"/>.
    /// </summary>
    /// <param name="spec">The application specification.</param>
    /// <returns>A new application instance.</returns>
    public static Core.Application CreateApplication(Core.ApplicationSpec? spec = null) => new Core.Application(spec);
#endif
}

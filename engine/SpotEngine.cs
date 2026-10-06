using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Spot.Editor")]
[assembly: InternalsVisibleTo("Spot.DebugUI")]
[assembly: InternalsVisibleTo("Spot.Engine.Tests")]
[assembly: InternalsVisibleTo("Spot.Framework.Tests")]

namespace Spot.Engine;

/// <summary>
/// Provides top-level information about the Spot engine.
/// </summary>
public static class SpotEngine
{
    /// <summary>
    /// Gets the current engine version.
    /// </summary>
    /// <returns>The engine version string.</returns>
    public static string GetVersion() => "0.6.0";

#if !BROWSER
    /// <summary>
    /// Creates a new engine application instance. Desktop only — the browser host drives its own RAF loop
    /// instead of the Silk.NET-backed <see cref="Application"/>.
    /// </summary>
    /// <param name="spec">The application specification.</param>
    /// <returns>A new application instance.</returns>
    public static Application CreateApplication(ApplicationSpec? spec = null) => new Application(spec);
#endif
}

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
    public static string GetVersion() => "0.1.0";
}

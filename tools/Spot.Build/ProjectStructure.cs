using Spot.Engine;

namespace Spot.Build;

/// <summary>
/// The well-known folder names that make up a Spot project on disk. Centralized so the build pipeline and the
/// editor agree on the layout. The runtime knows none of this except the cooked content folder, which it owns
/// (<see cref="ApplicationSpec.DefaultContentFolder"/>) and which <see cref="ContentFolder"/> mirrors.
/// </summary>
public static class ProjectStructure
{
    /// <summary>Source assets authored by the user (the default <c>ProjectConfig.AssetDirectory</c>).</summary>
    public const string AssetsFolder = "Assets";

    /// <summary>Cooked, engine-native artifacts a build ships and the runtime loads (via the manifest).</summary>
    public const string ContentFolder = ApplicationSpec.DefaultContentFolder;

    /// <summary>Editor-only on-demand cook cache, so edit mode shows cooked assets without a full build.</summary>
    public const string LibraryFolder = "Library";

    /// <summary>Publish output: a distributable build under <c>Build/&lt;platform&gt;</c>, Play under <c>Build/play</c>.</summary>
    public const string BuildFolder = "Build";

    /// <summary>The engine DLLs bundled next to a generated project so it builds against the current engine.</summary>
    public const string EngineBinFolder = "EngineBin";
}

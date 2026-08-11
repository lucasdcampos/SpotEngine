namespace Spot.Core;

/// <summary>
/// The well-known folder names that make up a Spot project on disk. Centralized so the build pipeline,
/// the editor, and the runtime agree on the layout — the cook output the build writes must be the folder
/// the generated app copies and the runtime loads from.
/// </summary>
public static class ProjectStructure
{
    /// <summary>Source assets authored by the user (the default <c>ProjectConfig.AssetDirectory</c>).</summary>
    public const string AssetsFolder = "Assets";

    /// <summary>Cooked, engine-native artifacts a build ships and the runtime loads (via the manifest).</summary>
    public const string ContentFolder = "Content";

    /// <summary>Editor-only on-demand cook cache, so edit mode shows cooked assets without a full build.</summary>
    public const string LibraryFolder = "Library";

    /// <summary>Publish output: a distributable build under <c>Build/&lt;platform&gt;</c>, Play under <c>Build/play</c>.</summary>
    public const string BuildFolder = "Build";

    /// <summary>The engine DLLs bundled next to a generated project so it builds against the current engine.</summary>
    public const string EngineBinFolder = "EngineBin";
}

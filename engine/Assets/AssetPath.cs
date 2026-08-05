namespace Spot.Assets;

/// <summary>
/// Resolves asset paths stored in scenes and materials against the active project's asset directory,
/// so committed <c>.sptscene</c>/<c>.sptmat</c> files stay portable across machines instead of baking
/// in an absolute path from whoever authored them. Stored paths are relative to <see cref="Root"/>
/// (normally the project's <c>Assets/</c> folder); absolute paths and the <c>primitive:</c>/<c>editor:</c>
/// pseudo-paths are passed through unchanged.
/// </summary>
public static class AssetPath
{
    /// <summary>
    /// Gets or sets the directory that relative asset paths resolve against — normally the active
    /// project's <c>Assets/</c> directory. The host (editor or game) sets this when a project loads.
    /// When empty, relative paths resolve against the current working directory (legacy behaviour).
    /// </summary>
    public static string Root { get; set; } = string.Empty;

    /// <summary>
    /// Returns <see langword="true"/> for references that are not filesystem paths and must never be treated as
    /// one: built-in assets (<c>primitive:</c>, <c>editor:</c>) and asset-pipeline guid references
    /// (<c>guid:</c>). Keeping guid references here means the scene/material serializer and path-based loaders
    /// pass them through untouched, so no code below the resolver has to know the reference form.
    /// </summary>
    /// <param name="path">The path to test.</param>
    public static bool IsPseudoPath(string path) =>
        path.StartsWith("primitive:", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("editor:", StringComparison.OrdinalIgnoreCase) ||
        AssetRef.IsGuidRef(path);

    /// <summary>
    /// Resolves a <c>guid:</c> reference to the absolute cooked artifact it names, or returns
    /// <see langword="null"/> for anything else. The host installs the actual lookup: the running game points it
    /// at its <see cref="AssetManifest"/>; the editor points it at its Library cooking. When no host is set (for
    /// example plain unit tests), guid references simply do not resolve.
    /// </summary>
    public static Func<string, string?>? ContentResolver { get; set; }

    /// <summary>
    /// Resolves a stored reference to a cooked artifact path when it is a <c>guid:</c> reference and a
    /// <see cref="ContentResolver"/> is installed; otherwise returns <see langword="null"/> so the caller falls
    /// back to loading the reference as a source path via <see cref="Resolve"/>.
    /// </summary>
    /// <param name="storedRef">The stored reference from a scene, material, or component.</param>
    public static string? ResolveContent(string storedRef) =>
        AssetRef.IsGuidRef(storedRef) ? ContentResolver?.Invoke(storedRef) : null;

    /// <summary>
    /// Resolves a stored asset path to an absolute path suitable for loading. Pseudo-paths, absolute
    /// paths, and (when <see cref="Root"/> is unset) relative paths are returned unchanged.
    /// </summary>
    /// <param name="path">The stored path.</param>
    /// <returns>An absolute path to load from, or the original path when no resolution applies.</returns>
    public static string Resolve(string path)
    {
        if (string.IsNullOrEmpty(path) || IsPseudoPath(path) || Path.IsPathRooted(path) || string.IsNullOrEmpty(Root))
        {
            return path;
        }

        return Path.Combine(Root, path);
    }

    /// <summary>
    /// Converts an absolute asset path to one relative to <see cref="Root"/> (with forward slashes) for
    /// storing. Pseudo-paths, already-relative paths, and paths outside <see cref="Root"/> are returned
    /// unchanged so nothing outside the project is silently rewritten.
    /// </summary>
    /// <param name="path">The absolute path to relativize.</param>
    /// <returns>A project-relative path, or the original path when it cannot be relativized.</returns>
    public static string MakeRelative(string path)
    {
        if (string.IsNullOrEmpty(path) || IsPseudoPath(path) || !Path.IsPathRooted(path) || string.IsNullOrEmpty(Root))
        {
            return path;
        }

        string relative = Path.GetRelativePath(Root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return path;
        }

        return relative.Replace('\\', '/');
    }
}

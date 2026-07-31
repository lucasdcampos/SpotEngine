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
    /// Returns <see langword="true"/> for pseudo-paths that name a built-in asset (<c>primitive:</c>,
    /// <c>editor:</c>) rather than a file on disk, which must never be treated as filesystem paths.
    /// </summary>
    /// <param name="path">The path to test.</param>
    public static bool IsPseudoPath(string path) =>
        path.StartsWith("primitive:", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("editor:", StringComparison.OrdinalIgnoreCase);

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

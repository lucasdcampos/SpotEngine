using System.Collections.Generic;
using Spot.Rendering;

namespace Spot.DebugUI.UI;

/// <summary>
/// A small, process-wide cache of image thumbnails keyed by asset path, so widgets that show many
/// texture previews (the asset picker popup, inspector slots) can reuse one GPU texture per file
/// instead of loading it every frame. Entries live for the session and are capped so a project with
/// thousands of images can't exhaust GPU memory; paths that fail to load are remembered so we don't
/// retry them each frame. Never throws — a load failure just yields <c>null</c>.
/// </summary>
internal static class EditorThumbnails
{
    private static readonly Dictionary<string, Texture2D> _cache = new();
    private static readonly HashSet<string> _failed = new();
    private const int MaxCached = 256;

    /// <summary>
    /// Returns a cached texture for <paramref name="path"/>, loading it on first request. Returns
    /// <c>null</c> if the file can't be loaded, isn't an image, or the cache is full.
    /// </summary>
    public static Texture2D? Get(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (_cache.TryGetValue(path, out Texture2D? tex))
            return tex;
        if (_failed.Contains(path) || _cache.Count >= MaxCached)
            return null;

        try
        {
            tex = new Texture2D(path);
            _cache[path] = tex;
            return tex;
        }
        catch
        {
            _failed.Add(path);
            return null;
        }
    }
}

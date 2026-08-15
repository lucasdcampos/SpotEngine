using System.Collections.Generic;
using System.IO;
using System.Text;
using Spot.Assets;

namespace Spot.Browser;

/// <summary>
/// The browser <see cref="IAssetProvider"/>: an in-memory store of cooked content the host preloads over HTTP
/// before the game starts. WebAssembly cannot read files synchronously over the network, but the engine's
/// asset API is synchronous, so the host <c>fetch</c>es every cooked file up front (driven by a content index)
/// and hands the bytes here; the engine then reads them synchronously, exactly as it would from disk on desktop.
/// </summary>
public sealed class BrowserAssetStore : IAssetProvider
{
    private readonly Dictionary<string, byte[]> _files = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>Stores a fetched file's bytes under its content-relative path (separators are normalized).</summary>
    /// <param name="path">The file path, relative to the content root or absolute; normalized to '/'.</param>
    /// <param name="bytes">The file contents.</param>
    public void Add(string path, byte[] bytes) => _files[Normalize(path)] = bytes;

    /// <summary>Gets the number of files currently held.</summary>
    public int Count => _files.Count;

    /// <inheritdoc />
    public bool Exists(string path) => _files.ContainsKey(Normalize(path));

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path) =>
        _files.TryGetValue(Normalize(path), out byte[]? bytes)
            ? bytes
            : throw new FileNotFoundException($"Cooked asset not preloaded: {path}", path);

    /// <inheritdoc />
    public string ReadAllText(string path) => Encoding.UTF8.GetString(ReadAllBytes(path));

    // Content is fetched and keyed by forward-slash relative paths, while the engine resolves asset paths with
    // the host OS separator (backslash under a Windows-built manifest). Normalizing both ends to '/' — and
    // trimming any leading slash — lets a desktop-cooked manifest resolve unchanged in the browser.
    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}

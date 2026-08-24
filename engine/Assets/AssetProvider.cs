namespace Spot.Assets;

/// <summary>
/// Abstracts reading cooked content, so the runtime can load from the local filesystem on desktop and
/// from an in-memory store fetched over HTTP in the browser, where synchronous file IO is unavailable.
/// </summary>
public interface IAssetProvider
{
    /// <summary>Returns whether content exists at the given path.</summary>
    /// <param name="path">The content path.</param>
    /// <returns><see langword="true"/> if the content exists.</returns>
    bool Exists(string path);

    /// <summary>Reads content's full contents as raw bytes.</summary>
    /// <param name="path">The content path.</param>
    /// <returns>The content bytes.</returns>
    byte[] ReadAllBytes(string path);

    /// <summary>Reads content's full contents as UTF-8 text.</summary>
    /// <param name="path">The content path.</param>
    /// <returns>The content text.</returns>
    string ReadAllText(string path);
}

/// <summary>The default <see cref="IAssetProvider"/>, reading directly from the local filesystem.</summary>
public sealed class FileAssetProvider : IAssetProvider
{
    /// <inheritdoc />
    public bool Exists(string path) => File.Exists(path);

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    /// <inheritdoc />
    public string ReadAllText(string path) => File.ReadAllText(path);
}

/// <summary>
/// The process-wide content source the runtime loads cooked assets through. Defaults to the local
/// filesystem; a host can install a different provider — for example the browser's fetched content
/// store, since a WebAssembly game cannot read the disk — before any scene or asset loads.
/// </summary>
public static class AssetProvider
{
    private static IAssetProvider s_current = new FileAssetProvider();

    /// <summary>
    /// Gets or sets the active content provider. Setting <see langword="null"/> restores the
    /// filesystem default.
    /// </summary>
    public static IAssetProvider Current
    {
        get => s_current;
        set => s_current = value ?? new FileAssetProvider();
    }
}

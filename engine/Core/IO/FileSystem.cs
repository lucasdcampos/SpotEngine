namespace Spot.Engine.IO;

/// <summary>
/// A source of file contents. The default reads the local disk; a host can install another — for example the
/// browser's in-memory store of fetched content, since a WebAssembly app cannot read the disk.
/// </summary>
public interface IFileSystem
{
    /// <summary>Returns whether a file exists at the given path.</summary>
    /// <param name="path">The file path.</param>
    /// <returns><see langword="true"/> if the file exists.</returns>
    bool Exists(string path);

    /// <summary>Reads a file's full contents as raw bytes.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The file bytes.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    byte[] ReadAllBytes(string path);

    /// <summary>Reads a file's full contents as UTF-8 text.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The file text.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    string ReadAllText(string path);
}

/// <summary>The default <see cref="IFileSystem"/>, reading directly from the local disk.</summary>
public sealed class DiskFileSystem : IFileSystem
{
    /// <inheritdoc />
    public bool Exists(string path) => File.Exists(path);

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    /// <inheritdoc />
    public string ReadAllText(string path) => File.ReadAllText(path);
}

/// <summary>
/// The process-wide file access every loader goes through (<c>Texture2D.FromFile</c>, <c>Image.FromFile</c>,
/// <c>AudioClip.FromFile</c>, fonts, and the engine's content). Swap <see cref="Current"/> to load from somewhere
/// other than the disk, and set <see cref="PathResolver"/> to map the paths your code passes (for example,
/// relative to a content folder) to the paths the file system understands.
/// </summary>
public static class FileSystem
{
    private static IFileSystem s_current = new DiskFileSystem();

    /// <summary>
    /// Gets or sets the active file system. Setting <see langword="null"/> restores the disk default.
    /// </summary>
    public static IFileSystem Current
    {
        get => s_current;
        set => s_current = value ?? new DiskFileSystem();
    }

    /// <summary>
    /// Gets or sets an optional mapping applied to every path passed to <see cref="Exists"/>,
    /// <see cref="ReadAllBytes"/> and <see cref="ReadAllText"/> before it reaches <see cref="Current"/>. Null (the
    /// default) uses paths unchanged. The engine installs one that resolves project-relative paths.
    /// </summary>
    public static Func<string, string>? PathResolver { get; set; }

    /// <summary>
    /// Applies <see cref="PathResolver"/> to a path.
    /// </summary>
    /// <param name="path">The path as given by the caller.</param>
    /// <returns>The path the file system should read.</returns>
    public static string Resolve(string path) => PathResolver is { } resolver ? resolver(path) : path;

    /// <summary>Returns whether a file exists at the (resolved) path.</summary>
    /// <param name="path">The file path.</param>
    /// <returns><see langword="true"/> if the file exists.</returns>
    public static bool Exists(string path) => s_current.Exists(Resolve(path));

    /// <summary>Reads a file's bytes from the (resolved) path.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] ReadAllBytes(string path) => s_current.ReadAllBytes(Resolve(path));

    /// <summary>Reads a file's UTF-8 text from the (resolved) path.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The file text.</returns>
    public static string ReadAllText(string path) => s_current.ReadAllText(Resolve(path));
}

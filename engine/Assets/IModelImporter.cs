namespace Spot.Assets;

/// <summary>
/// Imports a 3D model file into engine <see cref="Model"/> data. Implement this to add support for a
/// new file format, then register it with <see cref="ModelImporter.Register"/>.
/// </summary>
public interface IModelImporter
{
    /// <summary>
    /// Gets the file extensions this importer handles, each lowercase and including the leading dot
    /// (for example ".obj").
    /// </summary>
    IEnumerable<string> SupportedExtensions { get; }

    /// <summary>
    /// Imports the model at the given path.
    /// </summary>
    /// <param name="path">The path to the model file.</param>
    /// <returns>The imported model.</returns>
    Model Import(string path);
}

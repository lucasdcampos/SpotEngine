using Spot.Rendering;

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
    /// Imports only the CPU-side geometry of the model, doing no GPU work. This is the expensive part
    /// (reading and parsing the file) and contains no GL calls, so it is safe to call off the main
    /// thread; the caller builds the GPU <see cref="Rendering.Mesh"/> instances on the render thread.
    /// </summary>
    /// <param name="path">The path to the model file.</param>
    /// <returns>The model's meshes as CPU geometry.</returns>
    IReadOnlyList<MeshData> ImportMeshData(string path);

    /// <summary>
    /// Imports the model at the given path, building its GPU meshes. Must run on the render thread.
    /// </summary>
    /// <param name="path">The path to the model file.</param>
    /// <returns>The imported model.</returns>
    Model Import(string path);
}

using Spot.Rendering;

namespace Spot.Assets;

/// <summary>
/// An imported 3D model: one or more <see cref="Mesh"/> instances that together make up the asset.
/// </summary>
/// <remarks>
/// This is the high-level asset type. Load one from a file with <see cref="Load"/>, which routes
/// through the <see cref="IModelImporter"/> registered for the file's format. For lower-level
/// control, build <see cref="Mesh"/> instances yourself and construct a <see cref="Model"/> around them.
/// </remarks>
public sealed class Model
{
    /// <summary>
    /// Initializes a new <see cref="Model"/> from already-built meshes.
    /// </summary>
    /// <param name="meshes">The meshes that make up the model.</param>
    public Model(IReadOnlyList<Mesh> meshes)
    {
        Meshes = meshes;
    }

    /// <summary>Gets the meshes that make up the model.</summary>
    public IReadOnlyList<Mesh> Meshes { get; }

    /// <summary>Gets the file the model was loaded from, if any.</summary>
    public string? SourcePath { get; internal set; }

    /// <summary>
    /// Loads a model from a file, choosing an importer by extension. Results are cached by path, so
    /// loading the same file again returns the same instance and reuses its GPU buffers.
    /// </summary>
    /// <param name="path">The path to the model file.</param>
    /// <returns>The loaded model.</returns>
    public static Model Load(string path) => ModelImporter.Load(path);
}

namespace Spot.Assets;

/// <summary>
/// The registry that maps model file formats to their <see cref="IModelImporter"/> and loads models
/// through them. Register additional importers to support more formats; nothing above this layer changes.
/// </summary>
public static class ModelImporter
{
    private static readonly Dictionary<string, IModelImporter> s_importers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Model> s_cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers an importer for each of its supported extensions. Later registrations win for an extension.
    /// </summary>
    /// <param name="importer">The importer to register.</param>
    public static void Register(IModelImporter importer)
    {
        foreach (string extension in importer.SupportedExtensions)
        {
            s_importers[extension] = importer;
        }
    }

    /// <summary>
    /// Gets whether a model at the given path can be loaded by a registered importer.
    /// </summary>
    /// <param name="path">The model file path.</param>
    /// <returns><see langword="true"/> if an importer is registered for the file's extension.</returns>
    public static bool CanLoad(string path) => s_importers.ContainsKey(Path.GetExtension(path));

    /// <summary>
    /// Loads a model from a file through the importer registered for its extension, caching by full path.
    /// </summary>
    /// <param name="path">The model file path.</param>
    /// <returns>The loaded model.</returns>
    /// <exception cref="NotSupportedException">No importer is registered for the file's extension.</exception>
    public static Model Load(string path)
    {
        if (path.StartsWith("primitive:", StringComparison.OrdinalIgnoreCase))
        {
            if (s_cache.TryGetValue(path, out Model? primitiveCached))
            {
                return primitiveCached;
            }
            string typeName = path.Substring(10);
            Model primitive = PrimitiveModelFactory.Create(typeName);
            primitive.SourcePath = path;
            s_cache[path] = primitive;
            return primitive;
        }

        string fullPath = Path.GetFullPath(path);
        if (s_cache.TryGetValue(fullPath, out Model? cached))
        {
            return cached;
        }

        string extension = Path.GetExtension(fullPath);
        if (!s_importers.TryGetValue(extension, out IModelImporter? importer))
        {
            throw new NotSupportedException($"No model importer is registered for '{extension}' files.");
        }

        Model model = importer.Import(fullPath);
        model.SourcePath = fullPath;
        s_cache[fullPath] = model;
        return model;
    }
}

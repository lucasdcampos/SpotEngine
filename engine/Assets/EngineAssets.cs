using Spot.IO;

namespace Spot.Assets;

/// <summary>
/// Plugs the engine's asset pipeline into the framework's loaders: project-relative paths resolve against
/// <see cref="AssetPath.Root"/>, <c>guid:</c> model references resolve to cooked content, and cooked
/// <c>.sptmesh</c> models load through <see cref="ModelImporter"/> like any other format.
/// </summary>
/// <remarks>
/// Runs automatically the first time the engine's asset paths are used (and the application calls it at
/// startup), so engine code never needs to; it is idempotent.
/// </remarks>
public static class EngineAssets
{
    private static readonly object s_gate = new();
    private static bool s_installed;

    /// <summary>
    /// Installs the engine's resolvers and importers into the framework. Safe to call more than once.
    /// </summary>
    public static void Install()
    {
        lock (s_gate)
        {
            if (s_installed)
            {
                return;
            }

            s_installed = true;
        }

        FileSystem.PathResolver ??= AssetPath.Resolve;
        ModelImporter.ReferenceResolver = ResolveModelReference;
        ModelImporter.Register(new SpMeshModelImporter());
    }

    /// <summary>
    /// Resolves a model reference: a <c>guid:</c> reference maps to its cooked artifact (null when nothing
    /// resolves it), any other value is resolved against the asset root.
    /// </summary>
    /// <param name="reference">The stored model reference.</param>
    /// <returns>The file to load, or <see langword="null"/>.</returns>
    public static string? ResolveModelReference(string reference) =>
        AssetRef.IsGuidRef(reference) ? AssetPath.ResolveContent(reference) : AssetPath.Resolve(reference);
}

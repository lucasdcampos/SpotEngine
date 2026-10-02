using Spot.Framework.Graphics;

namespace Spot.Engine.Assets;

/// <summary>
/// Loads cooked <c>.sptmesh</c> models — geometry, skinning and clips parsed once at cook time — through the
/// framework's <see cref="ModelImporter"/>, exactly like a source format. Registered by
/// <see cref="EngineAssets.Install"/>.
/// </summary>
public sealed class SpMeshModelImporter : IModelImporter
{
    /// <inheritdoc />
    public IEnumerable<string> SupportedExtensions => new[] { ".sptmesh" };

    /// <inheritdoc />
    public IReadOnlyList<MeshData> ImportMeshData(string path) => SpMesh.ReadFile(path);

    /// <inheritdoc />
    public CookedModel ImportModel(string path) => SpMesh.ReadModelFile(path);

    /// <inheritdoc />
    public Model Import(string path) => ModelImporter.BuildModel(ImportModel(path));
}

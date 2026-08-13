namespace Spot.Assets;

/// <summary>
/// Cooks 3D model files (FBX, OBJ, glTF, ...) into <c>.sptmesh</c> by running the Assimp parser once at import
/// time and serializing its interleaved vertex/index data, skinning bones and animation clips. The shipped
/// game reads the resulting blob and uploads it straight to the GPU, so Assimp never runs at runtime.
/// </summary>
public sealed class ModelAssetImporter : IAssetImporter
{
    private readonly AssimpModelImporter _assimp = new();

    /// <inheritdoc />
    public string Id => "model";

    /// <inheritdoc />
    public IEnumerable<string> SourceExtensions => new[] { ".obj", ".fbx", ".gltf", ".glb", ".dae", ".ply", ".stl" };

    /// <inheritdoc />
    public string CookedExtension => ".sptmesh";

    /// <inheritdoc />
    public CookedArtifact Cook(string sourcePath, AssetMeta meta, IGuidResolver resolver)
    {
        CookedModel model = _assimp.ImportModel(sourcePath);
        return new CookedArtifact(SpMesh.Write(model), Id);
    }
}

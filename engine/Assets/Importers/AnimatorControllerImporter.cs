using System.Text;
using System.Text.Json.Nodes;

namespace Spot.Assets;

/// <summary>
/// Cooks a ".sptcontroller" animator controller. Its states reference clips by name and it holds no nested
/// asset references, so cooking simply validates that the file is well-formed JSON and passes it through
/// unchanged — it is already engine-native and the runtime loads it directly.
/// </summary>
public sealed class AnimatorControllerImporter : IAssetImporter
{
    /// <inheritdoc />
    public string Id => "animatorController";

    /// <inheritdoc />
    public IEnumerable<string> SourceExtensions => new[] { ".sptcontroller" };

    /// <inheritdoc />
    public string CookedExtension => ".sptcontroller";

    /// <inheritdoc />
    public CookedArtifact Cook(string sourcePath, AssetMeta meta, IGuidResolver resolver)
    {
        JsonObject obj = JsonNode.Parse(File.ReadAllText(sourcePath))?.AsObject()
            ?? throw new InvalidDataException($"Animator controller '{sourcePath}' is not a JSON object.");

        byte[] bytes = Encoding.UTF8.GetBytes(obj.ToJsonString());
        return new CookedArtifact(bytes, Id);
    }
}

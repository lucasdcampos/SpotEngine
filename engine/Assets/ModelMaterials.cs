using Spot.Framework.Assimp;

namespace Spot.Engine.Assets;

/// <summary>
/// Turns the materials of a source model into engine <c>.sptmat</c> assets, on top of what the framework's model
/// importer reads from the file. Existing <c>.sptmat</c> files are always kept as they are.
/// </summary>
public static class ModelMaterials
{
    /// <summary>
    /// Extracts one <c>.sptmat</c> per material slot in the model into <paramref name="outDir"/>, with the slot's
    /// base color and base texture (embedded textures are written out as PNGs alongside). This is the material
    /// half of dropping a model into the scene: it produces the assets
    /// <see cref="Scenes.ModelInstantiator"/> assigns to the matching mesh parts.
    /// </summary>
    /// <param name="modelPath">The path to the source model file.</param>
    /// <param name="outDir">The directory the <c>.sptmat</c> files (and any extracted textures) are written to.</param>
    /// <returns>A map from material slot index to its <c>.sptmat</c> path.</returns>
    public static IReadOnlyDictionary<int, string> ExtractPerSlot(string modelPath, string outDir)
    {
        var result = new Dictionary<int, string>();
        string MaterialPath(string name) => Path.Combine(outDir, name + ".sptmat");

        IReadOnlyList<ImportedMaterial> slots =
            AssimpModelImporter.ReadMaterials(modelPath, outDir, name => !File.Exists(MaterialPath(name)));

        foreach (ImportedMaterial slot in slots)
        {
            string matPath = MaterialPath(slot.Name);
            try
            {
                if (!File.Exists(matPath))
                {
                    var material = new Material();
                    if (slot.Color is { } color)
                    {
                        material.Color = color;
                    }

                    if (slot.TexturePath is { } texture)
                    {
                        material.SetTexture(texture);
                    }

                    material.Save(matPath);
                }

                result[slot.Slot] = matPath;
            }
            catch (Exception ex)
            {
                Spot.Framework.Log.CoreError("Failed to write material slot {0} of '{1}': {2}", slot.Slot, modelPath, ex.Message);
            }
        }

        return result;
    }

    /// <summary>
    /// Writes the model's embedded textures next to it and creates a <c>.sptmat</c> for each one that does not
    /// already have one.
    /// </summary>
    /// <param name="modelPath">The path to the source model file.</param>
    public static void ExtractEmbedded(string modelPath)
    {
        string directory = Path.GetDirectoryName(modelPath) ?? string.Empty;
        foreach (string imagePath in AssimpModelImporter.ExtractEmbeddedTextures(modelPath, directory))
        {
            try
            {
                string matPath = Path.ChangeExtension(imagePath, ".sptmat");
                if (!File.Exists(matPath))
                {
                    var material = new Material();
                    material.SetTexture(imagePath);
                    material.Save(matPath);
                }
            }
            catch (Exception ex)
            {
                Spot.Framework.Log.CoreError("Failed to create a material for '{0}': {1}", imagePath, ex.Message);
            }
        }
    }
}

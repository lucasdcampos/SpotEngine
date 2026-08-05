using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Spot.Core;

namespace Spot.Assets;

/// <summary>
/// The committed <c>.meta</c> sidecar that gives a source asset a stable identity independent of its path.
/// Each source file (<c>foo.fbx</c>) gets a <c>foo.fbx.meta</c> holding a <see cref="Guid"/> that scenes and
/// materials reference (as <c>guid:&lt;hex&gt;</c>), the importer that cooks it, and per-asset import
/// <see cref="Settings"/>. Because the GUID lives beside the source, renaming or moving the source never breaks
/// references. Cache/staleness state (the last-cooked source hash) lives in the regenerable Library, not here,
/// so the committed sidecar stays pure identity + configuration.
/// </summary>
public sealed class AssetMeta
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Gets or sets the stable 32-hex-character identity referenced by scenes and materials.</summary>
    public string Guid { get; set; } = string.Empty;

    /// <summary>Gets or sets the id of the importer that cooks this source (e.g. <c>texture</c>, <c>model</c>).</summary>
    public string Importer { get; set; } = string.Empty;

    /// <summary>Gets or sets the per-asset import settings, or <see langword="null"/> when there are none.</summary>
    public JsonObject? Settings { get; set; }

    /// <summary>Returns the sidecar path for a source file: the source path with <c>.meta</c> appended.</summary>
    /// <param name="sourcePath">The source asset path.</param>
    public static string MetaPathFor(string sourcePath) => sourcePath + ".meta";

    /// <summary>
    /// Loads the sidecar for a source, or creates a fresh one with a new <see cref="Guid"/> when it is missing
    /// or unreadable. Never throws: a corrupt <c>.meta</c> is logged and replaced so a bad file cannot stall a
    /// project scan. The returned meta is not written to disk until <see cref="Save"/> is called.
    /// </summary>
    /// <param name="sourcePath">The source asset path.</param>
    /// <param name="importerId">The importer that should cook this source; recorded on the meta.</param>
    public static AssetMeta ReadOrCreate(string sourcePath, string importerId)
    {
        string metaPath = MetaPathFor(sourcePath);
        if (File.Exists(metaPath))
        {
            try
            {
                AssetMeta? meta = JsonSerializer.Deserialize<AssetMeta>(File.ReadAllText(metaPath), Options);
                if (meta is not null && !string.IsNullOrEmpty(meta.Guid))
                {
                    meta.Importer = importerId;
                    return meta;
                }

                Log.CoreWarn("Asset meta '{0}' has no guid; regenerating.", metaPath);
            }
            catch (Exception ex)
            {
                Log.CoreWarn("Failed to read asset meta '{0}': {1}. Regenerating.", metaPath, ex.Message);
            }
        }

        return new AssetMeta
        {
            Guid = System.Guid.NewGuid().ToString("N"),
            Importer = importerId,
        };
    }

    /// <summary>Writes this sidecar next to its source as indented JSON.</summary>
    /// <param name="sourcePath">The source asset path whose sidecar to write.</param>
    public void Save(string sourcePath)
    {
        File.WriteAllText(MetaPathFor(sourcePath), JsonSerializer.Serialize(this, Options));
    }

    /// <summary>
    /// Computes the content hash of a source file combined with its import settings, used to decide whether a
    /// cooked artifact is stale. Uses the file's bytes (not its timestamp) so the hash is stable across machines
    /// and git checkouts.
    /// </summary>
    /// <param name="sourcePath">The absolute source asset path.</param>
    /// <param name="settings">The import settings that also influence the cooked output, or <see langword="null"/>.</param>
    /// <returns>A lowercase hex SHA-256 digest.</returns>
    public static string ComputeSourceHash(string sourcePath, JsonObject? settings = null)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(sourcePath);
        byte[] fileDigest = sha.ComputeHash(stream);

        if (settings is null)
        {
            return Convert.ToHexStringLower(fileDigest);
        }

        byte[] settingsBytes = Encoding.UTF8.GetBytes(settings.ToJsonString());
        byte[] combined = new byte[fileDigest.Length + settingsBytes.Length];
        fileDigest.CopyTo(combined, 0);
        settingsBytes.CopyTo(combined, fileDigest.Length);
        return Convert.ToHexStringLower(SHA256.HashData(combined));
    }
}

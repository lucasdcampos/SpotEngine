using System.Text.Json;
using System.Text.Json.Serialization;
using Spot.Core;

namespace Spot.Assets;

/// <summary>One manifest record: where a cooked artifact lives (relative to Content) and what type it is.</summary>
public sealed class ManifestEntry
{
    /// <summary>Gets or sets the cooked file path relative to the Content root, using forward slashes.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>Gets or sets the asset type (e.g. <c>texture</c>, <c>model</c>, <c>material</c>).</summary>
    public string Type { get; set; } = string.Empty;
}

/// <summary>The serializable manifest document: a version tag and the guid-to-artifact map.</summary>
public sealed class ManifestDocument
{
    /// <summary>Gets or sets the manifest schema version.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Gets the map from asset guid to its cooked artifact record.</summary>
    public Dictionary<string, ManifestEntry> Entries { get; set; } = new();
}

/// <summary>
/// The runtime asset index: the shipped game loads this once, then resolves <c>guid:</c> references to cooked
/// files under <c>Content/</c>. It replaces the editor/build-time <see cref="AssetDatabase"/> at runtime — the
/// game never scans sources or runs importers. Loading never throws: a missing or malformed manifest logs and
/// yields an empty index so the game runs (rendering whatever assets do resolve) rather than crashing.
/// </summary>
public sealed class AssetManifest
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ManifestDocument _doc;

    private AssetManifest(ManifestDocument doc, string contentRoot)
    {
        _doc = doc;
        ContentRoot = contentRoot;
    }

    /// <summary>Gets the manifest the running game loaded, or <see langword="null"/> when none is active.</summary>
    public static AssetManifest? Active { get; private set; }

    /// <summary>Gets the absolute Content directory that cooked file paths resolve against.</summary>
    public string ContentRoot { get; }

    /// <summary>
    /// Loads the manifest at the given path and makes it <see cref="Active"/>. Returns an empty manifest (still
    /// active) when the file is missing or unreadable, so the game never crashes over a broken content drop.
    /// </summary>
    /// <param name="manifestPath">The absolute path to <c>manifest.json</c>.</param>
    /// <returns>The loaded manifest.</returns>
    public static AssetManifest Load(string manifestPath)
    {
        string contentRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? string.Empty;
        ManifestDocument doc = new();
        try
        {
            ManifestDocument? parsed = JsonSerializer.Deserialize<ManifestDocument>(File.ReadAllText(manifestPath), JsonOptions);
            if (parsed is not null)
            {
                doc = parsed;
            }
            else
            {
                Log.CoreWarn("Asset manifest '{0}' was empty; running with no cooked assets.", manifestPath);
            }
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to load asset manifest '{0}': {1}", manifestPath, ex.Message);
        }

        Active = new AssetManifest(doc, contentRoot);
        return Active;
    }

    /// <summary>Looks up a cooked artifact by guid.</summary>
    /// <param name="guid">The 32-hex asset guid (no <c>guid:</c> prefix).</param>
    /// <param name="cookedFile">The cooked file path relative to <see cref="ContentRoot"/>.</param>
    /// <param name="type">The asset type.</param>
    /// <returns><see langword="true"/> when the guid is present.</returns>
    public bool TryResolve(string guid, out string cookedFile, out string type)
    {
        if (_doc.Entries.TryGetValue(guid, out ManifestEntry? entry))
        {
            cookedFile = entry.File;
            type = entry.Type;
            return true;
        }

        cookedFile = string.Empty;
        type = string.Empty;
        return false;
    }

    /// <summary>Resolves a <c>guid:&lt;hex&gt;</c> reference to an absolute cooked file path under Content.</summary>
    /// <param name="guidRef">The stored reference, e.g. <c>guid:ab12...</c>.</param>
    /// <returns>The absolute cooked path, or <see langword="null"/> when the guid is unknown.</returns>
    public string? ResolveGuidRef(string guidRef)
    {
        string guid = AssetRef.StripGuidPrefix(guidRef);
        return TryResolve(guid, out string file, out _) ? Path.Combine(ContentRoot, file) : null;
    }
}

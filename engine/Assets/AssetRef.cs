namespace Spot.Assets;

/// <summary>
/// Helpers for the <c>guid:&lt;hex&gt;</c> reference form that scenes, materials, and components store instead of
/// a file path once a project uses the asset pipeline. Centralizing the prefix keeps every producer and consumer
/// (the database, the manifest, the serializer) in agreement on the format.
/// </summary>
public static class AssetRef
{
    /// <summary>The prefix that marks a stored value as a guid reference rather than a path or pseudo-path.</summary>
    public const string GuidPrefix = "guid:";

    /// <summary>Returns whether a stored value is a <c>guid:</c> reference.</summary>
    /// <param name="value">The stored reference to test.</param>
    public static bool IsGuidRef(string? value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(GuidPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds a <c>guid:</c> reference from a raw guid.</summary>
    /// <param name="guid">The 32-hex asset guid.</param>
    public static string MakeGuidRef(string guid) => GuidPrefix + guid;

    /// <summary>Strips the <c>guid:</c> prefix from a reference, returning the raw guid (or the input unchanged).</summary>
    /// <param name="guidRef">The stored reference.</param>
    public static string StripGuidPrefix(string guidRef) =>
        IsGuidRef(guidRef) ? guidRef[GuidPrefix.Length..] : guidRef;
}

namespace Spot.Assets;

/// <summary>
/// Cooks one category of source asset (by file extension) into an engine-native artifact the runtime can load
/// without the source file or its heavy dependencies. Cooking is pure computation with no GL calls, so it runs
/// off the render thread — during an editor import or a build — and its output is what the shipped game loads.
/// </summary>
public interface IAssetImporter
{
    /// <summary>Gets the stable importer id recorded in <c>.meta</c> and manifest entries (e.g. <c>texture</c>).</summary>
    string Id { get; }

    /// <summary>Gets the source file extensions this importer handles, each including the leading dot.</summary>
    IEnumerable<string> SourceExtensions { get; }

    /// <summary>Gets the extension of the cooked artifact this importer produces (e.g. <c>.spttex</c>).</summary>
    string CookedExtension { get; }

    /// <summary>Cooks a source file into an engine-native artifact.</summary>
    /// <param name="sourcePath">The absolute path to the source asset.</param>
    /// <param name="meta">The source's sidecar, carrying its guid and import settings.</param>
    /// <param name="resolver">Resolves referenced source paths to <c>guid:</c> references.</param>
    /// <returns>The cooked bytes and their manifest type.</returns>
    CookedArtifact Cook(string sourcePath, AssetMeta meta, IGuidResolver resolver);
}

/// <summary>The result of cooking a source asset: the encoded bytes and the manifest type they represent.</summary>
/// <param name="Bytes">The cooked artifact bytes, ready to write to the Library or Content.</param>
/// <param name="Type">The manifest asset type (e.g. <c>texture</c>, <c>model</c>, <c>material</c>).</param>
public readonly record struct CookedArtifact(byte[] Bytes, string Type);

/// <summary>
/// Turns a referenced source path (relative, absolute, or an already-resolved reference) into the stable
/// <c>guid:&lt;hex&gt;</c> reference used inside cooked assets, so nested references survive rename/move too.
/// </summary>
public interface IGuidResolver
{
    /// <summary>Resolves a referenced source path to a <c>guid:</c> reference, or returns it unchanged when it
    /// is already a reference (<c>guid:</c>/<c>primitive:</c>/<c>editor:</c>) or has no known guid.</summary>
    /// <param name="sourcePathOrRef">The referenced source path or existing reference.</param>
    string? ToGuidRef(string sourcePathOrRef);
}

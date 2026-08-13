using System.Text.Json;
using System.Text.Json.Nodes;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Assets;

/// <summary>
/// The outcome of a full asset cook: where the runtime <c>manifest.json</c> was written, how many sources
/// cooked, and how many failed. A non-zero <see cref="Failed"/> count means the manifest is missing those
/// entries, so callers should surface it (the <c>spot cook</c> CLI turns it into a non-zero exit code)
/// instead of silently shipping an incomplete build.
/// </summary>
public readonly record struct CookResult(string ManifestPath, int Cooked, int Failed);

/// <summary>
/// The editor/build-time asset database: it scans a project's <c>Assets/</c> tree, gives every source a stable
/// guid via its <c>.meta</c> sidecar, cooks sources into engine-native artifacts, and resolves references between
/// them. It is the authoring-side counterpart to the runtime <see cref="AssetManifest"/> — the shipped game never
/// touches it. Copy-through assets (scenes, fonts) are shipped by path rather than guid, since they are loaded by
/// name, not referenced.
/// </summary>
public static class AssetDatabase
{
    private static readonly Dictionary<string, IAssetImporter> s_importersByExt = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IAssetImporter> s_importers = new();

    // Bump when a cooked format changes so the editor's Library cache re-cooks unchanged sources. See EnsureCooked.
    private const string CookFormatVersion = "|cook3-skeletal-anim-nopivots";

    // Guid maps for the most recently refreshed project. Keys are full, normalized source paths.
    private static readonly Dictionary<string, string> s_sourceToGuid = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> s_guidToSource = new(StringComparer.OrdinalIgnoreCase);

    // Session memo of guid -> cooked artifact so the editor's resolver doesn't re-hash a source every frame.
    // Cleared by Refresh, which is the reimport point.
    private static readonly Dictionary<string, string> s_cookedByGuid = new(StringComparer.OrdinalIgnoreCase);
    private static string s_assetsRoot = string.Empty;

    private static readonly GuidResolver s_resolver = new();

    // Extensions copied verbatim into Content, preserving their relative path (loaded by name, not by guid).
    private static readonly string[] s_copyThroughExtensions = { ".sptscene", ".ttf", ".otf" };

    static AssetDatabase()
    {
        RegisterImporter(new TextureImporter());
        RegisterImporter(new ModelAssetImporter());
        RegisterImporter(new MaterialImporter());
        RegisterImporter(new PrefabImporter());
        RegisterImporter(new AudioImporter());
    }

    /// <summary>Registers an importer for each of its source extensions. Later registrations win for an extension.</summary>
    /// <param name="importer">The importer to register.</param>
    public static void RegisterImporter(IAssetImporter importer)
    {
        s_importers.Add(importer);
        foreach (string ext in importer.SourceExtensions)
        {
            s_importersByExt[ext] = importer;
        }
    }

    /// <summary>Returns the importer that handles a source file, or <see langword="null"/> when none is registered.</summary>
    /// <param name="sourcePath">The source asset path.</param>
    public static IAssetImporter? ImporterFor(string sourcePath) =>
        s_importersByExt.TryGetValue(Path.GetExtension(sourcePath), out IAssetImporter? importer) ? importer : null;

    /// <summary>
    /// Scans a project's asset tree, ensuring every importable source has a committed <c>.meta</c> with a guid,
    /// and rebuilds the in-memory guid maps. Call this when opening a project and before cooking. Never throws:
    /// a file that cannot be read is logged and skipped.
    /// </summary>
    /// <param name="assetsRoot">The absolute path to the project's <c>Assets/</c> directory.</param>
    public static void Refresh(string assetsRoot)
    {
        s_assetsRoot = Path.GetFullPath(assetsRoot);
        s_sourceToGuid.Clear();
        s_guidToSource.Clear();
        s_cookedByGuid.Clear();

        if (!Directory.Exists(s_assetsRoot))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(s_assetsRoot, "*", SearchOption.AllDirectories))
        {
            IAssetImporter? importer = ImporterFor(file);
            if (importer is null)
            {
                continue;
            }

            try
            {
                string full = Path.GetFullPath(file);
                AssetMeta meta = AssetMeta.ReadOrCreate(full, importer.Id);
                if (!File.Exists(AssetMeta.MetaPathFor(full)))
                {
                    meta.Save(full);
                }

                s_sourceToGuid[full] = meta.Guid;
                s_guidToSource[meta.Guid] = full;
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to index asset '{0}': {1}", file, ex.Message);
            }
        }
    }

    /// <summary>Gets the guid for a source path from the last <see cref="Refresh"/>.</summary>
    /// <param name="sourcePath">The source asset path (relative to the asset root or absolute).</param>
    /// <param name="guid">The resolved guid.</param>
    /// <returns><see langword="true"/> when the source is known.</returns>
    public static bool TryGetGuid(string sourcePath, out string guid) =>
        s_sourceToGuid.TryGetValue(ToFullSourcePath(sourcePath), out guid!);

    /// <summary>Gets the source path for a guid from the last <see cref="Refresh"/>.</summary>
    /// <param name="guid">The raw guid (no <c>guid:</c> prefix).</param>
    /// <param name="sourcePath">The resolved absolute source path.</param>
    /// <returns><see langword="true"/> when the guid is known.</returns>
    public static bool TryGetSource(string guid, out string sourcePath) =>
        s_guidToSource.TryGetValue(guid, out sourcePath!);

    /// <summary>Gets the shared <see cref="IGuidResolver"/> backed by this database.</summary>
    public static IGuidResolver Resolver => s_resolver;

    /// <summary>
    /// Converts a referenced source path to a <c>guid:</c> reference. References that are already
    /// <c>guid:</c>/<c>primitive:</c>/<c>editor:</c>, and paths with no known guid, are returned unchanged.
    /// </summary>
    /// <param name="sourcePathOrRef">The referenced source path or existing reference.</param>
    public static string? ToGuidRef(string? sourcePathOrRef)
    {
        if (string.IsNullOrEmpty(sourcePathOrRef)
            || AssetRef.IsGuidRef(sourcePathOrRef)
            || AssetPath.IsPseudoPath(sourcePathOrRef))
        {
            return sourcePathOrRef;
        }

        string full = ToFullSourcePath(sourcePathOrRef);
        if (s_sourceToGuid.TryGetValue(full, out string? guid))
        {
            return AssetRef.MakeGuidRef(guid);
        }

        // Index on demand: a source referenced before the next full scan (e.g. just dropped in the editor)
        // still gets a stable committed guid so the reference is portable immediately.
        if (File.Exists(full) && ImporterFor(full) is IAssetImporter importer)
        {
            AssetMeta meta = AssetMeta.ReadOrCreate(full, importer.Id);
            if (!File.Exists(AssetMeta.MetaPathFor(full)))
            {
                meta.Save(full);
            }

            s_sourceToGuid[full] = meta.Guid;
            s_guidToSource[meta.Guid] = full;
            return AssetRef.MakeGuidRef(meta.Guid);
        }

        Log.CoreWarn("No asset guid for reference '{0}'; storing it unchanged.", sourcePathOrRef);
        return sourcePathOrRef;
    }

    /// <summary>
    /// Resolves a stored reference to a human-facing source path for display and asset pickers: a <c>guid:</c>
    /// reference becomes its source path (so the UI shows a filename, not a hex guid), while pseudo-paths and
    /// plain paths are returned unchanged.
    /// </summary>
    /// <param name="storedRef">The stored reference from a component.</param>
    public static string? ToDisplayPath(string? storedRef)
    {
        if (string.IsNullOrEmpty(storedRef) || !AssetRef.IsGuidRef(storedRef))
        {
            return storedRef;
        }

        return s_guidToSource.TryGetValue(AssetRef.StripGuidPrefix(storedRef), out string? source) ? source : storedRef;
    }

    /// <summary>
    /// Installs an <see cref="AssetPath.ContentResolver"/> that cooks assets on demand into
    /// <paramref name="libraryRoot"/> and resolves <c>guid:</c> references to those cooked artifacts, so the
    /// editor renders the same cooked data a build ships. Call after <see cref="Refresh"/>.
    /// </summary>
    /// <param name="libraryRoot">The project's regenerable Library cache directory.</param>
    public static void InstallLibraryResolver(string libraryRoot)
    {
        AssetPath.ContentResolver = reference => ResolveGuidToCooked(reference, libraryRoot);
    }

    /// <summary>
    /// Cooks a single source into the given cache root (the editor's <c>Library/</c>), reusing the existing cooked
    /// artifact when the source is unchanged. Returns the absolute cooked path.
    /// </summary>
    /// <param name="sourcePath">The absolute source asset path.</param>
    /// <param name="libraryRoot">The cache root to cook into.</param>
    public static string EnsureCooked(string sourcePath, string libraryRoot)
    {
        IAssetImporter importer = ImporterFor(sourcePath)
            ?? throw new NotSupportedException($"No importer registered for '{Path.GetExtension(sourcePath)}'.");

        AssetMeta meta = AssetMeta.ReadOrCreate(sourcePath, importer.Id);
        if (!File.Exists(AssetMeta.MetaPathFor(sourcePath)))
        {
            meta.Save(sourcePath);
        }

        string shardDir = Path.Combine(libraryRoot, meta.Guid[..2]);
        string cooked = Path.Combine(shardDir, meta.Guid + importer.CookedExtension);
        string hashFile = cooked + ".src";
        // The cooked-format version is folded into the staleness key so a change in what an importer emits
        // (e.g. skeletons/clips added to .sptmesh, or file-based clip names) re-cooks the editor's Library
        // cache even when the source bytes are unchanged. Bump it whenever a cooked format changes.
        string hash = AssetMeta.ComputeSourceHash(sourcePath, meta.Settings) + CookFormatVersion;

        if (File.Exists(cooked) && File.Exists(hashFile) && File.ReadAllText(hashFile) == hash)
        {
            return cooked;
        }

        Directory.CreateDirectory(shardDir);
        CookedArtifact artifact = importer.Cook(sourcePath, meta, s_resolver);
        File.WriteAllBytes(cooked, artifact.Bytes);
        File.WriteAllText(hashFile, hash);
        return cooked;
    }

    /// <summary>
    /// Resolves a <c>guid:</c> reference to a cooked artifact in the given cache root, cooking it on demand. Used
    /// by the editor to load cooked assets from its <c>Library/</c>. Returns <see langword="null"/> when the guid
    /// is unknown or cooking fails.
    /// </summary>
    /// <param name="guidRef">The stored reference, e.g. <c>guid:ab12...</c>.</param>
    /// <param name="libraryRoot">The cache root to cook into.</param>
    public static string? ResolveGuidToCooked(string guidRef, string libraryRoot)
    {
        string guid = AssetRef.StripGuidPrefix(guidRef);
        if (s_cookedByGuid.TryGetValue(guid, out string? memo))
        {
            return memo;
        }

        if (!s_guidToSource.TryGetValue(guid, out string? source))
        {
            return null;
        }

        try
        {
            string cooked = EnsureCooked(source, libraryRoot);
            s_cookedByGuid[guid] = cooked;
            return cooked;
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to cook '{0}' ({1}): {2}", source, guidRef, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Cooks every source in a project's asset tree into a fresh <c>Content/</c> directory and writes the runtime
    /// <c>manifest.json</c>. Copy-through assets (scenes, fonts) are mirrored by path. This is the build step that
    /// produces exactly what a shipped game loads — no source files. The <c>Content/</c> tree is rebuilt from
    /// scratch each run so a deleted or renamed source can never leave a stale cooked artifact behind.
    /// </summary>
    /// <param name="assetsRoot">The project's <c>Assets/</c> directory.</param>
    /// <param name="contentRoot">The destination <c>Content/</c> directory (rebuilt on each cook).</param>
    /// <returns>Where the manifest was written and the cooked/failed source counts.</returns>
    public static CookResult CookAll(string assetsRoot, string contentRoot)
    {
        Refresh(assetsRoot);
        CleanContentRoot(contentRoot);
        Directory.CreateDirectory(contentRoot);

        int cooked = 0;
        int failed = 0;
        var doc = new ManifestDocument();
        foreach ((string source, string guid) in s_sourceToGuid)
        {
            IAssetImporter? importer = ImporterFor(source);
            if (importer is null)
            {
                continue;
            }

            try
            {
                AssetMeta meta = AssetMeta.ReadOrCreate(source, importer.Id);
                CookedArtifact artifact = importer.Cook(source, meta, s_resolver);

                string shard = guid[..2];
                string relative = $"{shard}/{guid}{importer.CookedExtension}";
                string outPath = Path.Combine(contentRoot, shard, guid + importer.CookedExtension);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                File.WriteAllBytes(outPath, artifact.Bytes);

                doc.Entries[guid] = new ManifestEntry { File = relative, Type = artifact.Type };
                cooked++;
            }
            catch (Exception ex)
            {
                failed++;
                Log.CoreError("Failed to cook '{0}': {1}", source, ex.Message);
            }
        }

        failed += CopyThrough(assetsRoot, contentRoot);

        string manifestPath = Path.Combine(contentRoot, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(doc, AssetManifest.JsonOptions));

        if (failed > 0)
        {
            Log.CoreWarn("Cook finished with {0} failure(s) out of {1} asset(s); the shipped content is missing those entries.", failed, cooked + failed);
        }

        return new CookResult(manifestPath, cooked, failed);
    }

    // Deletes the existing Content/ tree so each cook starts clean. Guarded so a mis-pointed destination
    // (a filesystem root, or a directory full of unrelated files) can never be wiped: only a folder that is
    // clearly cook output — named Content, empty, or already holding a manifest from a previous cook — is
    // rebuilt. Anything else is refused with a clear error.
    private static void CleanContentRoot(string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(contentRoot) || !Directory.Exists(contentRoot))
        {
            return;
        }

        string full = Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(Path.GetDirectoryName(full)))
        {
            // full has no parent -> it's a drive/filesystem root. Refuse rather than wipe it.
            throw new ArgumentException($"Refusing to clean content root at a filesystem root: '{contentRoot}'.", nameof(contentRoot));
        }

        bool looksLikeCookOutput =
            string.Equals(Path.GetFileName(full), ProjectStructure.ContentFolder, StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(full, "manifest.json"))
            || !Directory.EnumerateFileSystemEntries(full).Any();

        if (!looksLikeCookOutput)
        {
            throw new ArgumentException(
                $"Refusing to rebuild content in '{contentRoot}': it isn't a cook-output folder (no manifest.json and not named '{ProjectStructure.ContentFolder}'). Point the output at a dedicated content directory.",
                nameof(contentRoot));
        }

        Directory.Delete(full, recursive: true);
    }

    // Reference properties in scenes/materials whose relative source-path values migrate to guid: references.
    private static readonly string[] s_referenceKeys = { "ModelPath", "MaterialPath", "TexturePath", "NormalMapPath", "ClipPath" };

    /// <summary>
    /// One-time migration: rewrites every scene (<c>.sptscene</c>) and material (<c>.sptmat</c>) under a
    /// project's assets so their asset references become stable <c>guid:</c> references instead of relative
    /// source paths. Pseudo-paths (<c>primitive:</c>/<c>editor:</c>) and already-migrated references are left
    /// untouched, so it is safe to run repeatedly. Returns the number of files changed. When
    /// <paramref name="dryRun"/> is true, nothing is written.
    /// </summary>
    /// <param name="assetsRoot">The project's <c>Assets/</c> directory.</param>
    /// <param name="dryRun">When true, report what would change without writing.</param>
    public static int MigrateReferences(string assetsRoot, bool dryRun = false)
    {
        Refresh(assetsRoot);

        int changedFiles = 0;
        foreach (string file in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".sptscene" && ext != ".sptmat")
            {
                continue;
            }

            try
            {
                JsonNode? root = SceneJson.Parse(File.ReadAllText(file));
                int changes = RewriteReferences(root);
                if (changes == 0)
                {
                    continue;
                }

                changedFiles++;
                Log.CoreInfo("{0}: {1} reference(s) migrated{2}", Path.GetFileName(file), changes, dryRun ? " (dry run)" : string.Empty);
                if (!dryRun)
                {
                    File.WriteAllText(file, root!.ToJsonString(SceneJson.WriteOptions));
                }
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to migrate '{0}': {1}", file, ex.Message);
            }
        }

        return changedFiles;
    }

    // Recursively rewrites reference-property string values to guid: references anywhere in a JSON tree, so it
    // works regardless of scene/component schema (entities, nested Children, arbitrary components).
    private static int RewriteReferences(JsonNode? node)
    {
        int changed = 0;
        switch (node)
        {
            case JsonObject obj:
                foreach (string key in obj.Select(kv => kv.Key).ToList())
                {
                    if (Array.IndexOf(s_referenceKeys, key) >= 0
                        && obj[key] is JsonValue value
                        && value.TryGetValue(out string? path)
                        && !string.IsNullOrEmpty(path))
                    {
                        string? rewritten = ToGuidRef(path);
                        if (rewritten != path)
                        {
                            obj[key] = rewritten;
                            changed++;
                        }
                    }
                    else
                    {
                        changed += RewriteReferences(obj[key]);
                    }
                }

                break;

            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    changed += RewriteReferences(item);
                }

                break;
        }

        return changed;
    }

    // Mirrors copy-through assets (scenes, fonts) into Content/ by path. Returns how many failed to copy so
    // the cook can report them alongside importer failures rather than losing them to the log.
    private static int CopyThrough(string assetsRoot, string contentRoot)
    {
        int failed = 0;
        foreach (string file in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories))
        {
            if (Array.IndexOf(s_copyThroughExtensions, Path.GetExtension(file).ToLowerInvariant()) < 0)
            {
                continue;
            }

            try
            {
                string relative = Path.GetRelativePath(assetsRoot, file);
                string outPath = Path.Combine(contentRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

                // Scenes are shipped by path, but their asset references must go out as guid: references so the
                // runtime resolves them through the manifest. A raw source path — e.g. the .fbx and .sptmat that
                // a model dragged into the scene leaves behind — has no cooked artifact next to it in Content and
                // would fail to load in the built game. Rewrite references on the way out; the source scene on
                // disk is left untouched. Fonts (and any scene that won't parse) fall back to a verbatim copy.
                if (Path.GetExtension(file).Equals(".sptscene", StringComparison.OrdinalIgnoreCase)
                    && TryRewriteSceneReferences(file, out string rewritten))
                {
                    File.WriteAllText(outPath, rewritten);
                }
                else
                {
                    File.Copy(file, outPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                failed++;
                Log.CoreError("Failed to copy asset '{0}' into content: {1}", file, ex.Message);
            }
        }

        return failed;
    }

    // Reads a scene file and returns its JSON with every asset reference rewritten to a guid: reference (leaving
    // pseudo-paths and unknown paths as-is). Returns false so the caller copies the file verbatim when it can't
    // be parsed, per the engine's never-crash rule.
    private static bool TryRewriteSceneReferences(string sceneFile, out string json)
    {
        try
        {
            if (SceneJson.Parse(File.ReadAllText(sceneFile)) is not JsonNode root)
            {
                json = string.Empty;
                return false;
            }

            RewriteReferences(root);
            json = root.ToJsonString(SceneJson.WriteOptions);
            return true;
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to rewrite references in scene '{0}': {1}", sceneFile, ex.Message);
            json = string.Empty;
            return false;
        }
    }

    private static string ToFullSourcePath(string sourcePathOrRef)
    {
        string abs = Path.IsPathRooted(sourcePathOrRef)
            ? sourcePathOrRef
            : Path.Combine(s_assetsRoot, sourcePathOrRef);
        return Path.GetFullPath(abs);
    }

    private sealed class GuidResolver : IGuidResolver
    {
        public string? ToGuidRef(string sourcePathOrRef) => AssetDatabase.ToGuidRef(sourcePathOrRef);
    }
}

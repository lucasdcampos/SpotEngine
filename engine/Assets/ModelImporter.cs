using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Spot.Core;
using Spot.Rendering;

namespace Spot.Assets;

/// <summary>
/// The registry that maps model file formats to their <see cref="IModelImporter"/> and loads models
/// through them. Register additional importers to support more formats; nothing above this layer changes.
/// </summary>
/// <remarks>
/// Loading has two flavours. <see cref="Load"/> is synchronous — it parses the file and builds the GPU
/// buffers on the calling (render) thread — and is what the editor uses on explicit user action.
/// <see cref="RequestAsync"/> is non-blocking: it parses heavy files on a background worker and returns
/// <see langword="null"/> until the geometry has been uploaded to the GPU by <see cref="ProcessPendingUploads"/>
/// on the render thread. Scene loading uses the async path so a large model never freezes startup.
/// </remarks>
public static class ModelImporter
{
    private static readonly Dictionary<string, IModelImporter> s_importers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Model> s_cache = new(StringComparer.OrdinalIgnoreCase);

    // Async loading state. File parsing (the expensive part) runs on a background worker; the resulting
    // CPU geometry is queued back to the render thread, which owns the GL context, to build the GPU
    // buffers. Imports are serialized through a gate so several large files don't thrash disk and CPU at
    // once. The completed queue is the only structure touched from a background thread; the in-flight and
    // failed sets are render-thread only (RequestAsync reads/adds, ProcessPendingUploads removes/adds).
    private static readonly ConcurrentQueue<LoadResult> s_completed = new();
    private static readonly HashSet<string> s_inFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_failed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim s_importGate = new(1, 1);

    private sealed class LoadResult
    {
        public string FullPath { get; init; } = string.Empty;
        public CookedModel? Cooked { get; init; }
        public double ParseMs { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>
    /// Builds a drawable <see cref="Model"/> from CPU model data, uploading each submesh (rigid or skinned)
    /// to the GPU and carrying its bones and animation clips across. Must run on the render thread.
    /// </summary>
    /// <param name="cooked">The CPU-side geometry, skinning and clips.</param>
    /// <returns>The GPU-ready model.</returns>
    internal static Model BuildModel(CookedModel cooked)
    {
        var meshes = new List<Mesh>(cooked.Submeshes.Count);
        var bones = new IReadOnlyList<Spot.Animation.BoneInfo>?[cooked.Submeshes.Count];
        bool anySkinned = false;

        for (int i = 0; i < cooked.Submeshes.Count; i++)
        {
            MeshData md = cooked.Submeshes[i];
            meshes.Add(new Mesh(md.Vertices, md.Indices, md.Skinned));
            bones[i] = md.Bones;
            anySkinned |= md.Skinned;
        }

        return new Model(meshes, anySkinned ? bones : null, cooked.Animations);
    }

    /// <summary>
    /// Registers an importer for each of its supported extensions. Later registrations win for an extension.
    /// </summary>
    /// <param name="importer">The importer to register.</param>
    public static void Register(IModelImporter importer)
    {
        foreach (string extension in importer.SupportedExtensions)
        {
            s_importers[extension] = importer;
        }
    }

    /// <summary>
    /// Gets whether a model at the given path can be loaded by a registered importer.
    /// </summary>
    /// <param name="path">The model file path.</param>
    /// <returns><see langword="true"/> if an importer is registered for the file's extension.</returns>
    public static bool CanLoad(string path) => s_importers.ContainsKey(Path.GetExtension(path));

    /// <summary>
    /// Resolves a stored model reference to an absolute path to load from, and whether that path is a cooked
    /// <c>.spmesh</c> (loaded without Assimp) rather than a source model. A <c>guid:</c> reference resolves
    /// through the content host; anything else resolves as a source path. Returns <see langword="false"/> when a
    /// guid reference has no cooked artifact (unknown guid or no host installed), so callers can skip it.
    /// </summary>
    private static bool TryResolveModelPath(string path, out string fullPath, out bool cooked)
    {
        if (AssetRef.IsGuidRef(path))
        {
            string? content = AssetPath.ResolveContent(path);
            if (content is null)
            {
                fullPath = string.Empty;
                cooked = false;
                return false;
            }

            fullPath = Path.GetFullPath(content);
        }
        else
        {
            fullPath = Path.GetFullPath(AssetPath.Resolve(path));
        }

        cooked = fullPath.EndsWith(".spmesh", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    /// <summary>
    /// Loads a model from a file through the importer registered for its extension, caching by full path.
    /// This blocks the calling thread through both parsing and the GPU upload, so it must run on the
    /// render thread; prefer <see cref="RequestAsync"/> for anything that could be large.
    /// </summary>
    /// <param name="path">The model file path.</param>
    /// <returns>The loaded model.</returns>
    /// <exception cref="NotSupportedException">No importer is registered for the file's extension.</exception>
    public static Model Load(string path)
    {
        if (path.StartsWith("primitive:", StringComparison.OrdinalIgnoreCase))
        {
            if (s_cache.TryGetValue(path, out Model? primitiveCached))
            {
                return primitiveCached;
            }
            string typeName = path.Substring(10);
            Model primitive = PrimitiveModelFactory.Create(typeName);
            primitive.SourcePath = path;
            s_cache[path] = primitive;
            return primitive;
        }

        if (!TryResolveModelPath(path, out string fullPath, out bool cooked))
        {
            throw new FileNotFoundException($"Unresolved model reference '{path}'.");
        }

        if (s_cache.TryGetValue(fullPath, out Model? cached))
        {
            return cached;
        }

        var sw = Stopwatch.StartNew();
        Model model = cooked ? BuildFromSpMesh(fullPath) : ImportFromSource(fullPath);
        sw.Stop();
        model.SourcePath = fullPath;
        s_cache[fullPath] = model;
        Log.CoreInfo("Loaded model '{0}' in {1:0} ms", path, sw.Elapsed.TotalMilliseconds);
        return model;
    }

    private static Model BuildFromSpMesh(string fullPath) => BuildModel(SpMesh.ReadModelFile(fullPath));

    private static Model ImportFromSource(string fullPath)
    {
        string extension = Path.GetExtension(fullPath);
        if (!s_importers.TryGetValue(extension, out IModelImporter? importer))
        {
            throw new NotSupportedException($"No model importer is registered for '{extension}' files.");
        }

        return importer.Import(fullPath);
    }

    /// <summary>
    /// Requests a model without blocking. Returns the model immediately if it is already loaded (or a
    /// cheap primitive); otherwise kicks off a background parse and returns <see langword="null"/>. Call
    /// again on later frames — once <see cref="ProcessPendingUploads"/> has finished the GPU upload, this
    /// returns the ready model. A parse that fails is remembered and never retried, so it is safe to call
    /// every frame from a render loop.
    /// </summary>
    /// <param name="path">The model file path.</param>
    /// <returns>The model if ready; otherwise <see langword="null"/>.</returns>
    public static Model? RequestAsync(string path)
    {
        // Primitives are trivial to build; there's nothing to gain from deferring them.
        if (path.StartsWith("primitive:", StringComparison.OrdinalIgnoreCase))
        {
            return Load(path);
        }

        if (!TryResolveModelPath(path, out string fullPath, out bool cooked))
        {
            // Unknown guid or no content host yet: log once (keyed by the reference) and don't retry.
            if (s_failed.Add(path))
            {
                Log.CoreError("Unresolved model reference '{0}'.", path);
            }

            return null;
        }

        if (s_cache.TryGetValue(fullPath, out Model? cached))
        {
            return cached;
        }

        if (s_failed.Contains(fullPath) || s_inFlight.Contains(fullPath))
        {
            return null;
        }

        // Cooked meshes just need their blob read on the worker; source models go through Assimp there.
        Func<CookedModel> parse;
        if (cooked)
        {
            parse = () => SpMesh.ReadModelFile(fullPath);
        }
        else
        {
            string extension = Path.GetExtension(fullPath);
            if (!s_importers.TryGetValue(extension, out IModelImporter? importer))
            {
                Log.CoreError("No model importer is registered for '{0}' files.", extension);
                s_failed.Add(fullPath);
                return null;
            }

            parse = () => importer.ImportModel(fullPath);
        }

        s_inFlight.Add(fullPath);
        QueueParse(fullPath, parse);
        return null;
    }

    // Runs a CPU-only parse (Assimp or .spmesh read) on a gated background worker, then queues the resulting
    // geometry back for GPU upload on the render thread. The queue is the only cross-thread structure.
    private static void QueueParse(string fullPath, Func<CookedModel> parse)
    {
        _ = Task.Run(async () =>
        {
            await s_importGate.WaitAsync().ConfigureAwait(false);
            var sw = Stopwatch.StartNew();
            try
            {
                CookedModel data = parse();
                sw.Stop();
                s_completed.Enqueue(new LoadResult { FullPath = fullPath, Cooked = data, ParseMs = sw.Elapsed.TotalMilliseconds });
            }
            catch (Exception ex)
            {
                s_completed.Enqueue(new LoadResult { FullPath = fullPath, Error = ex.Message });
            }
            finally
            {
                s_importGate.Release();
            }
        });
    }

    /// <summary>
    /// Finishes any background model loads by building their GPU buffers on the render thread and caching
    /// the results. Call once per frame from the main loop. Work is time-budgeted so that several models
    /// completing on the same frame are spread across frames rather than causing a single visible stall.
    /// </summary>
    public static void ProcessPendingUploads()
    {
        if (s_completed.IsEmpty)
        {
            return;
        }

        const double budgetMs = 8.0;
        var budget = Stopwatch.StartNew();

        while (s_completed.TryDequeue(out LoadResult? result))
        {
            s_inFlight.Remove(result.FullPath);

            if (result.Error != null || result.Cooked is null)
            {
                Log.CoreError("Failed to load model '{0}': {1}", result.FullPath, result.Error ?? "no geometry");
                s_failed.Add(result.FullPath);
                continue;
            }

            // A synchronous Load for the same path may have cached it meanwhile; don't build twice.
            if (!s_cache.ContainsKey(result.FullPath))
            {
                var sw = Stopwatch.StartNew();
                Model model = BuildModel(result.Cooked.Value);
                model.SourcePath = result.FullPath;
                sw.Stop();
                s_cache[result.FullPath] = model;
                Log.CoreInfo("Loaded model '{0}' — parse {1:0} ms, GPU upload {2:0} ms",
                    result.FullPath, result.ParseMs, sw.Elapsed.TotalMilliseconds);
            }

            if (budget.Elapsed.TotalMilliseconds > budgetMs)
            {
                break;
            }
        }
    }
}

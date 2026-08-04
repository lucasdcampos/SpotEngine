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
        public IReadOnlyList<MeshData>? Data { get; init; }
        public double ParseMs { get; init; }
        public string? Error { get; init; }
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

        string fullPath = Path.GetFullPath(AssetPath.Resolve(path));
        if (s_cache.TryGetValue(fullPath, out Model? cached))
        {
            return cached;
        }

        string extension = Path.GetExtension(fullPath);
        if (!s_importers.TryGetValue(extension, out IModelImporter? importer))
        {
            throw new NotSupportedException($"No model importer is registered for '{extension}' files.");
        }

        var sw = Stopwatch.StartNew();
        Model model = importer.Import(fullPath);
        sw.Stop();
        model.SourcePath = fullPath;
        s_cache[fullPath] = model;
        Log.CoreInfo("Loaded model '{0}' in {1:0} ms", path, sw.Elapsed.TotalMilliseconds);
        return model;
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

        string fullPath = Path.GetFullPath(AssetPath.Resolve(path));
        if (s_cache.TryGetValue(fullPath, out Model? cached))
        {
            return cached;
        }

        if (s_failed.Contains(fullPath) || s_inFlight.Contains(fullPath))
        {
            return null;
        }

        string extension = Path.GetExtension(fullPath);
        if (!s_importers.TryGetValue(extension, out IModelImporter? importer))
        {
            Log.CoreError("No model importer is registered for '{0}' files.", extension);
            s_failed.Add(fullPath);
            return null;
        }

        s_inFlight.Add(fullPath);
        _ = Task.Run(async () =>
        {
            await s_importGate.WaitAsync().ConfigureAwait(false);
            var sw = Stopwatch.StartNew();
            try
            {
                IReadOnlyList<MeshData> data = importer.ImportMeshData(fullPath);
                sw.Stop();
                s_completed.Enqueue(new LoadResult { FullPath = fullPath, Data = data, ParseMs = sw.Elapsed.TotalMilliseconds });
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

        return null;
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

            if (result.Error != null || result.Data == null)
            {
                Log.CoreError("Failed to load model '{0}': {1}", result.FullPath, result.Error ?? "no geometry");
                s_failed.Add(result.FullPath);
                continue;
            }

            // A synchronous Load for the same path may have cached it meanwhile; don't build twice.
            if (!s_cache.ContainsKey(result.FullPath))
            {
                var sw = Stopwatch.StartNew();
                var meshes = new List<Mesh>(result.Data.Count);
                foreach (MeshData md in result.Data)
                {
                    meshes.Add(new Mesh(md.Vertices, md.Indices));
                }
                var model = new Model(meshes) { SourcePath = result.FullPath };
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

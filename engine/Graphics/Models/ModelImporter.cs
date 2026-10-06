using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Spot.Engine;
using Spot.Engine.IO;

namespace Spot.Engine.Graphics;

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
    private static readonly List<(string Prefix, Func<string, Model> Provider)> s_providers = new()
    {
        (PrimitiveModelFactory.ReferencePrefix, PrimitiveModelFactory.LoadReference),
    };

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
    /// Builds the GPU meshes for CPU-side model data (the result of <see cref="IModelImporter.ImportModel"/>).
    /// Must run on the render thread. Custom importers use it to implement <see cref="IModelImporter.Import"/>.
    /// </summary>
    /// <param name="cooked">The CPU-side geometry and animation data.</param>
    /// <returns>The model, with its GPU meshes uploaded.</returns>
    public static Model BuildModel(CookedModel cooked)
    {
        var meshes = new List<Mesh>(cooked.Submeshes.Count);
        var bones = new IReadOnlyList<Spot.Engine.Animation.BoneInfo>?[cooked.Submeshes.Count];
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
    public static bool CanLoad(string path) => FindProvider(path) is not null || s_importers.ContainsKey(Path.GetExtension(path));

    /// <summary>
    /// Registers a provider for model references that start with a prefix — models that are generated or built in
    /// rather than read from a file, such as the <c>primitive:</c> references the framework provides itself. The
    /// provider runs synchronously on the render thread, receives the whole reference, should cache the models it
    /// returns, and throws for a reference it does not know. Registering a prefix again replaces its provider.
    /// </summary>
    /// <param name="prefix">The reference prefix, such as <c>builtin:</c> (matched ignoring case).</param>
    /// <param name="provider">Returns the model a reference names.</param>
    public static void RegisterProvider(string prefix, Func<string, Model> provider)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentNullException.ThrowIfNull(provider);
        s_providers.RemoveAll(p => string.Equals(p.Prefix, prefix, StringComparison.OrdinalIgnoreCase));
        s_providers.Add((prefix, provider));
    }

    /// <summary>Gets whether a reference is served by a registered provider rather than read from a file.</summary>
    /// <param name="reference">The model reference.</param>
    /// <returns><see langword="true"/> when a provider's prefix matches.</returns>
    public static bool IsProvided(string reference) => FindProvider(reference) is not null;

    private static Func<string, Model>? FindProvider(string reference)
    {
        foreach ((string prefix, Func<string, Model> provider) in s_providers)
        {
            if (reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets or sets the mapping from a model reference (what <see cref="Load"/> and <see cref="RequestAsync"/>
    /// receive) to a file path, or <see langword="null"/> when the reference cannot be resolved. Defaults to
    /// <see cref="FileSystem.Resolve"/>; the engine installs one that also resolves <c>guid:</c> references to
    /// cooked content. <c>primitive:</c> references never reach it.
    /// </summary>
    public static Func<string, string?>? ReferenceResolver { get; set; }

    // Resolves a reference to an absolute file path, or false when the resolver cannot place it.
    private static bool TryResolveModelPath(string path, out string fullPath)
    {
        string? resolved = ReferenceResolver is { } resolver ? resolver(path) : FileSystem.Resolve(path);
        if (string.IsNullOrEmpty(resolved))
        {
            fullPath = string.Empty;
            return false;
        }

        fullPath = Path.GetFullPath(resolved);
        return true;
    }

    // The importer registered for a file's extension, or null.
    private static IModelImporter? ImporterFor(string fullPath) =>
        s_importers.TryGetValue(Path.GetExtension(fullPath), out IModelImporter? importer) ? importer : null;

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
        if (FindProvider(path) is { } provider)
        {
            return provider(path);
        }

        if (!TryResolveModelPath(path, out string fullPath))
        {
            throw new FileNotFoundException($"Unresolved model reference '{path}'.");
        }

        if (s_cache.TryGetValue(fullPath, out Model? cached))
        {
            return cached;
        }

        IModelImporter importer = ImporterFor(fullPath)
            ?? throw new NotSupportedException($"No model importer is registered for '{Path.GetExtension(fullPath)}' files.");
        Model model = importer.Import(fullPath);
        model.SourcePath = fullPath;
        s_cache[fullPath] = model;
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
        // The browser's WASM runtime is single-threaded, so the background parse worker never runs. Load
        // synchronously instead — cooked meshes are cheap and their bytes are already in the in-memory asset
        // store. Failures are cached (keyed by the reference) so a bad model isn't retried or re-logged.
        if (OperatingSystem.IsBrowser())
        {
            if (s_failed.Contains(path))
            {
                return null;
            }

            try
            {
                return Load(path);
            }
            catch (Exception ex)
            {
                s_failed.Add(path);
                Log.CoreError("Failed to load model '{0}': {1}", path, ex.Message);
                return null;
            }
        }

        // Provided models (primitives, built-ins) are cheap to build; there's nothing to gain from deferring them.
        if (FindProvider(path) is { } provider)
        {
            if (s_failed.Contains(path))
            {
                return null;
            }

            try
            {
                return provider(path);
            }
            catch (Exception ex)
            {
                s_failed.Add(path);
                Log.CoreError("Failed to load model '{0}': {1}", path, ex.Message);
                return null;
            }
        }

        if (!TryResolveModelPath(path, out string fullPath))
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

        // The importer's CPU-side parse (a cooked blob read, or a full source import) runs on the worker.
        if (ImporterFor(fullPath) is not { } importer)
        {
            Log.CoreError("No model importer is registered for '{0}' files.", Path.GetExtension(fullPath));
            s_failed.Add(fullPath);
            return null;
        }

        s_inFlight.Add(fullPath);
        QueueParse(fullPath, () => importer.ImportModel(fullPath));
        return null;
    }

    // Runs a CPU-only parse (Assimp or .sptmesh read) on a gated background worker, then queues the resulting
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
                Model model = BuildModel(result.Cooked.Value);
                model.SourcePath = result.FullPath;
                s_cache[result.FullPath] = model;
            }

            if (budget.Elapsed.TotalMilliseconds > budgetMs)
            {
                break;
            }
        }
    }
}

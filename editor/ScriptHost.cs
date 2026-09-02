using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Editor;

/// <summary>
/// Loads the active project's compiled assembly so the editor can resolve and instantiate its scripts, and —
/// unlike a plain <c>Assembly.Load</c> — can drop and reload it without restarting. The assembly lives in a
/// collectible <see cref="AssemblyLoadContext"/>; everything it references (the engine, Silk.NET, …) resolves
/// through the default context, so only the project's own script types are unloadable and every
/// <see cref="EntityBehaviour"/> subclass still shares the one engine <see cref="EntityBehaviour"/> type.
/// </summary>
/// <remarks>
/// The project assembly's generated <see cref="IScriptProvider"/> registers itself with
/// <see cref="ScriptRegistry"/> from a module initializer the moment the assembly is loaded; this host diffs
/// the registry's providers across the load to capture that instance and unregisters it before unloading, so
/// no descriptor keeps a soon-to-be-unloaded type alive. Every step is guarded — a failed load or unload logs
/// and leaves the editor running, per the engine's never-crash rule.
/// </remarks>
internal sealed class ScriptHost
{
    // A collectible context that resolves only the project assembly locally; all dependencies fall through to
    // the default context (returning null from Load defers to it), keeping engine types shared and identical.
    private sealed class ScriptLoadContext : AssemblyLoadContext
    {
        public ScriptLoadContext()
            : base("SpotProjectScripts", isCollectible: true)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }

    private ScriptLoadContext? _context;
    private readonly List<IScriptProvider> _providers = new();

    /// <summary>Gets the currently loaded project assembly, or <see langword="null"/> when none is loaded.</summary>
    public Assembly? Assembly { get; private set; }

    /// <summary>Gets the path of the assembly that is currently loaded, or <see langword="null"/>.</summary>
    public string? LoadedPath { get; private set; }

    /// <summary>
    /// Loads (or reloads) the assembly at <paramref name="dllPath"/> into a fresh collectible context, first
    /// unloading any previously loaded one. The bytes are copied in so the file on disk stays unlocked and can
    /// be rebuilt. Returns <see langword="true"/> on success; on failure it logs and leaves no assembly loaded.
    /// </summary>
    public bool Load(string dllPath)
    {
        Unload();

        if (!File.Exists(dllPath))
        {
            Log.CoreWarn("Cannot load project scripts: '{0}' does not exist.", dllPath);
            return false;
        }

        try
        {
            IReadOnlyList<IScriptProvider> before = ScriptRegistry.Providers;

            var context = new ScriptLoadContext();
            byte[] assemblyBytes = File.ReadAllBytes(dllPath);
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");

            Assembly asm;
            using (var assemblyStream = new MemoryStream(assemblyBytes))
            {
                if (File.Exists(pdbPath))
                {
                    using var pdbStream = new MemoryStream(File.ReadAllBytes(pdbPath));
                    asm = context.LoadFromStream(assemblyStream, pdbStream);
                }
                else
                {
                    asm = context.LoadFromStream(assemblyStream);
                }
            }

            // Force the module initializer to run now (it may otherwise wait for first type access), so the
            // generated script provider registers before we diff to capture it.
            try
            {
                RuntimeHelpers.RunModuleConstructor(asm.ManifestModule.ModuleHandle);
            }
            catch (Exception ex)
            {
                Log.CoreWarn("Project scripts module initializer threw: {0}", ex.Message);
            }

            // Whatever providers appeared during this load belong to this assembly; track them so they can be
            // dropped on the next reload.
            foreach (IScriptProvider provider in ScriptRegistry.Providers)
            {
                if (!before.Contains(provider))
                {
                    _providers.Add(provider);
                }
            }

            _context = context;
            Assembly = asm;
            LoadedPath = dllPath;
            Log.CoreInfo("Loaded project scripts: {0}", dllPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to load project scripts '{0}': {1}", dllPath, ex.Message);
            _context = null;
            Assembly = null;
            LoadedPath = null;
            return false;
        }
    }

    /// <summary>
    /// Unregisters this host's script providers and unloads the collectible context. Callers must first drop
    /// every reference to script instances from the loaded assembly (see the editor's reload flow) or the
    /// context cannot be collected — the unload request is honored once the last reference is gone.
    /// </summary>
    public void Unload()
    {
        foreach (IScriptProvider provider in _providers)
        {
            ScriptRegistry.Unregister(provider);
        }

        _providers.Clear();
        Assembly = null;
        LoadedPath = null;

        if (_context is null)
        {
            return;
        }

        try
        {
            _context.Unload();
        }
        catch (Exception ex)
        {
            Log.CoreWarn("Failed to unload project scripts: {0}", ex.Message);
        }

        _context = null;

        // Nudge the runtime to actually reclaim the collectible context now that references are dropped, so a
        // rapid rebuild/reload does not accumulate stale assemblies.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}

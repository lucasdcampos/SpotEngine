using System;
using System.Collections.Generic;
using System.Linq;
using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// One resolvable script: its stable <see cref="Guid"/> (from the <c>.cs.meta</c> sidecar), its class
/// <see cref="Name"/>, the concrete <see cref="EntityBehaviour"/> <see cref="Type"/>, and a
/// <see cref="Factory"/> that constructs an instance without reflection. Emitted by the script source
/// generator so the runtime never needs <c>Activator</c> or an assembly scan — the AOT/trimming-safe path
/// the browser build depends on.
/// </summary>
/// <param name="Guid">The stable 32-hex identity, or empty when the script has no sidecar.</param>
/// <param name="Name">The script's class name (the legacy serialized reference).</param>
/// <param name="Type">The concrete script type.</param>
/// <param name="Factory">Constructs a new instance of the script.</param>
public sealed record ScriptDescriptor(string Guid, string Name, Type Type, Func<EntityBehaviour> Factory);

/// <summary>
/// Supplies the scripts a single assembly contains. The script source generator emits one implementation
/// per game assembly and registers it with <see cref="ScriptRegistry"/> from a module initializer, so the
/// registry is populated the moment the assembly loads (including into a hot-reload load context).
/// </summary>
public interface IScriptProvider
{
    /// <summary>Returns every concrete script this provider knows about.</summary>
    IEnumerable<ScriptDescriptor> GetScripts();
}

/// <summary>
/// The process-wide catalog of resolvable scripts, aggregated from every registered
/// <see cref="IScriptProvider"/>. <see cref="ScriptResolver"/> consults it first (by guid, then by class
/// name) and only falls back to a reflection scan when it misses, so the generated, reflection-free path is
/// preferred while a project without the generator still works. Lookups are case-insensitive on name and
/// tolerate duplicate registrations (last wins, logged).
/// </summary>
public static class ScriptRegistry
{
    private static readonly object s_gate = new();
    private static readonly List<IScriptProvider> s_providers = new();
    private static Dictionary<string, ScriptDescriptor>? s_byGuid;
    private static Dictionary<string, ScriptDescriptor>? s_byName;

    /// <summary>
    /// Adds a provider (typically the generated registry of a game assembly). Idempotent per instance:
    /// registering the same provider twice is ignored. Invalidates the cached lookup maps.
    /// </summary>
    public static void Register(IScriptProvider provider)
    {
        lock (s_gate)
        {
            if (!s_providers.Contains(provider))
            {
                s_providers.Add(provider);
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Removes a previously registered provider. Used by the editor's hot-reload host before unloading a
    /// script load context so descriptors pointing at soon-to-be-unloaded types are dropped.
    /// </summary>
    public static void Unregister(IScriptProvider provider)
    {
        lock (s_gate)
        {
            if (s_providers.Remove(provider))
            {
                Invalidate();
            }
        }
    }

    /// <summary>
    /// A snapshot of the currently registered providers. The editor's hot-reload host captures this before and
    /// after loading a project assembly to learn which provider the assembly registered from its module
    /// initializer, so it can drop exactly that provider before unloading the assembly's load context.
    /// </summary>
    public static IReadOnlyList<IScriptProvider> Providers
    {
        get
        {
            lock (s_gate)
            {
                return s_providers.ToList();
            }
        }
    }

    /// <summary>Drops the cached lookup maps so the next query rebuilds them from the current providers.</summary>
    public static void Invalidate()
    {
        lock (s_gate)
        {
            s_byGuid = null;
            s_byName = null;
        }
    }

    /// <summary>Every script known to the registry across all providers.</summary>
    public static IEnumerable<ScriptDescriptor> All
    {
        get
        {
            lock (s_gate)
            {
                EnsureMaps();
                // Prefer the guid-keyed set (it is the authoritative identity); scripts without a guid still
                // appear via the name map. Union by type so an entry present in both is yielded once.
                return s_byGuid!.Values
                    .Concat(s_byName!.Values)
                    .Distinct()
                    .ToList();
            }
        }
    }

    /// <summary>Finds a script by its stable guid. Returns <see langword="false"/> when unknown or the guid is empty.</summary>
    public static bool TryGetByGuid(string? guid, out ScriptDescriptor? descriptor)
    {
        descriptor = null;
        if (string.IsNullOrEmpty(guid))
        {
            return false;
        }

        lock (s_gate)
        {
            EnsureMaps();
            return s_byGuid!.TryGetValue(guid, out descriptor);
        }
    }

    /// <summary>Finds a script by its class name (case-insensitive). Returns <see langword="false"/> when unknown.</summary>
    public static bool TryGetByName(string? name, out ScriptDescriptor? descriptor)
    {
        descriptor = null;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        lock (s_gate)
        {
            EnsureMaps();
            return s_byName!.TryGetValue(name, out descriptor);
        }
    }

    private static void EnsureMaps()
    {
        if (s_byGuid is not null && s_byName is not null)
        {
            return;
        }

        var byGuid = new Dictionary<string, ScriptDescriptor>(StringComparer.Ordinal);
        var byName = new Dictionary<string, ScriptDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (IScriptProvider provider in s_providers)
        {
            IEnumerable<ScriptDescriptor> scripts;
            try
            {
                scripts = provider.GetScripts();
            }
            catch (Exception ex)
            {
                // A faulty provider must never take resolution down; skip it.
                Log.CoreWarn("Script provider '{0}' failed to enumerate scripts: {1}", provider.GetType().Name, ex.Message);
                continue;
            }

            foreach (ScriptDescriptor descriptor in scripts)
            {
                if (!string.IsNullOrEmpty(descriptor.Guid))
                {
                    byGuid[descriptor.Guid] = descriptor;
                }

                byName[descriptor.Name] = descriptor;
            }
        }

        s_byGuid = byGuid;
        s_byName = byName;
    }
}

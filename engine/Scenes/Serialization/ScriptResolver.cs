using System;
using System.Linq;
using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// Resolves script references to <see cref="EntityBehaviour"/> types and instances. A reference is a stable
/// guid (from the script's <c>.cs.meta</c> sidecar) with the class name kept as a human-readable, legacy
/// fallback. Resolution consults the reflection-free <see cref="ScriptRegistry"/> first — by guid, then by
/// name — and only falls back to scanning every loaded assembly when the registry misses, so a project built
/// with the script source generator never pays reflection while one without it still works. Shared by scene
/// loading and the editor's "Add Script" flow so both resolve identically.
/// </summary>
internal static class ScriptResolver
{
    /// <summary>
    /// When set, a script whose type cannot be found is logged at the quiet trace level instead of a warning.
    /// The editor turns this on: gameplay scripts are compiled into the game's assembly, not the editor, so a
    /// scene opened for authoring routinely references types this process hasn't loaded. The reference is kept
    /// intact (the inspector shows the script) and resolves normally at play time, so it is not a real fault
    /// while editing — only a genuinely missing type in a running game deserves the warning.
    /// </summary>
    public static bool QuietMissingScripts { get; set; }

    /// <summary>
    /// Finds the <see cref="EntityBehaviour"/> subclass for a reference, preferring the stable
    /// <paramref name="guid"/> and falling back to <paramref name="className"/>. Returns <see langword="null"/>
    /// when neither resolves.
    /// </summary>
    public static Type? Resolve(string? guid, string className)
    {
        if (ScriptRegistry.TryGetByGuid(guid, out ScriptDescriptor? byGuid))
        {
            return byGuid!.Type;
        }

        return Resolve(className);
    }

    /// <summary>
    /// Finds the <see cref="EntityBehaviour"/> subclass named <paramref name="className"/> (an optional
    /// <c>.cs</c> suffix is tolerated), consulting the registry first and then scanning every loaded
    /// assembly. Returns <see langword="null"/> when no such type exists.
    /// </summary>
    public static Type? Resolve(string className)
    {
        string name = className.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            ? className[..^3]
            : className;

        if (ScriptRegistry.TryGetByName(name, out ScriptDescriptor? byName))
        {
            return byName!.Type;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch
            {
                // A partially-loadable assembly must never take resolution down; just skip it.
                continue;
            }

            Type? match = types.FirstOrDefault(t => t.Name == name && t.IsSubclassOf(typeof(EntityBehaviour)));
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves and instantiates the script, attaching it to <paramref name="entity"/>. Prefers the stable
    /// <paramref name="guid"/>, falling back to <paramref name="className"/>. Returns <see langword="null"/>
    /// (logging on failure) when the reference is unresolved or construction throws, so callers keep the
    /// reference as an unresolved entry rather than crashing.
    /// </summary>
    public static EntityBehaviour? Create(string? guid, string className, Entity entity)
    {
        // A generated descriptor carries a reflection-free factory; use it when the guid or name resolves.
        if (ScriptRegistry.TryGetByGuid(guid, out ScriptDescriptor? descriptor)
            || ScriptRegistry.TryGetByName(StripCsSuffix(className), out descriptor))
        {
            return Construct(descriptor!, entity, className);
        }

        return Create(className, entity);
    }

    /// <summary>
    /// Resolves and instantiates a script by class name, attaching it to <paramref name="entity"/>. Returns
    /// <see langword="null"/> (logging on failure) when the type is unresolved or construction throws.
    /// </summary>
    public static EntityBehaviour? Create(string className, Entity entity)
    {
        if (ScriptRegistry.TryGetByName(StripCsSuffix(className), out ScriptDescriptor? descriptor))
        {
            return Construct(descriptor!, entity, className);
        }

        Type? type = Resolve(className);
        if (type == null)
        {
            // In an authoring host (the editor) the script lives in the game assembly and simply isn't loaded
            // here — an expected, non-fault condition the inspector already surfaces visually, so stay silent.
            // In a running game a missing type is a real problem worth a warning.
            if (!QuietMissingScripts)
            {
                Log.CoreWarn("Failed to load script '{0}'. Type not found.", className);
            }

            return null;
        }

        try
        {
            var instance = (EntityBehaviour)Activator.CreateInstance(type)!;
            instance.Entity = entity;
            return instance;
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to instantiate script '{0}': {1}", className, ex.Message);
            return null;
        }
    }

    private static EntityBehaviour? Construct(ScriptDescriptor descriptor, Entity entity, string className)
    {
        try
        {
            EntityBehaviour instance = descriptor.Factory();
            instance.Entity = entity;
            return instance;
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to instantiate script '{0}': {1}", className, ex.Message);
            return null;
        }
    }

    private static string StripCsSuffix(string className) =>
        className.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? className[..^3] : className;
}

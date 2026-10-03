using System;
using System.Linq;
using Spot.Framework;

namespace Spot.Engine.Scenes;

/// <summary>
/// Resolves references to user components (see <see cref="Component.IsUserComponent"/>) to types and
/// instances. A reference is a stable guid (from the script's <c>.cs.meta</c> sidecar) with the class name kept
/// as a human-readable fallback. Resolution consults the reflection-free <see cref="ScriptRegistry"/> first — by
/// guid, then by name — and only falls back to scanning every loaded assembly when the registry misses, so a
/// project built with the script source generator never pays reflection while one without it still works.
/// </summary>
internal static class ScriptResolver
{
    /// <summary>
    /// When set, a component whose type cannot be found is not logged. The editor turns this on: gameplay
    /// components are compiled into the game's assembly, not the editor, so a scene opened for authoring
    /// routinely references types this process hasn't loaded yet. The reference is kept intact (as
    /// <see cref="MissingComponents"/>) and resolves once the scripts load, so it is not a real fault while
    /// editing — only a genuinely missing type in a running game deserves the warning.
    /// </summary>
    public static bool QuietMissingScripts { get; set; }

    /// <summary>
    /// Finds the user component type for a reference, preferring the stable <paramref name="guid"/> and falling
    /// back to <paramref name="className"/>. Returns <see langword="null"/> when neither resolves.
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
    /// Finds the user component type named <paramref name="className"/> (an optional <c>.cs</c> suffix is
    /// tolerated), consulting the registry first and then scanning every loaded assembly. Returns
    /// <see langword="null"/> when no such type exists.
    /// </summary>
    public static Type? Resolve(string className)
    {
        string name = StripCsSuffix(className);
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

            Type? match = types.FirstOrDefault(t => t.Name == name && IsUserComponentType(t));
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates the user <see cref="Component"/> a reference names, preferring the stable
    /// <paramref name="guid"/> and falling back to <paramref name="className"/>. Returns
    /// <see langword="null"/> (logging unless <see cref="QuietMissingScripts"/>) when the type cannot be found
    /// or its constructor throws.
    /// </summary>
    public static Component? CreateComponent(string? guid, string className)
    {
        try
        {
            // A generated descriptor carries a reflection-free factory; use it when the guid or name resolves.
            if (ScriptRegistry.TryGetByGuid(guid, out ScriptDescriptor? descriptor)
                || ScriptRegistry.TryGetByName(StripCsSuffix(className), out descriptor))
            {
                return descriptor!.Factory();
            }

            Type? type = Resolve(className);
            if (type is not null)
            {
                return (Component)Activator.CreateInstance(type)!;
            }
        }
        catch (Exception ex)
        {
            Log.CoreError("Failed to instantiate component '{0}': {1}", className, ex.Message);
            return null;
        }

        if (!QuietMissingScripts)
        {
            Log.CoreWarn("Failed to load component '{0}'. Type not found.", className);
        }

        return null;
    }

    private static bool IsUserComponentType(Type type) =>
        !type.IsAbstract && type.IsSubclassOf(typeof(Component)) && Component.IsUserType(type);

    private static string StripCsSuffix(string className) =>
        className.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? className[..^3] : className;
}

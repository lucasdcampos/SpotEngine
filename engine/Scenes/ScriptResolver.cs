using System;
using System.Linq;
using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// Resolves script class names to <see cref="EntityBehaviour"/> types and instances. Scripts are
/// referenced by class-name string (so game scripts can live in the project assembly), and this searches
/// every loaded assembly to find them. Shared by scene loading and the editor's "Add Script" flow so
/// both resolve identically.
/// </summary>
internal static class ScriptResolver
{
    /// <summary>
    /// Finds the <see cref="EntityBehaviour"/> subclass named <paramref name="className"/> (an optional
    /// <c>.cs</c> suffix is tolerated), searching every loaded assembly. Returns <see langword="null"/>
    /// when no such type exists.
    /// </summary>
    public static Type? Resolve(string className)
    {
        string name = className.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            ? className[..^3]
            : className;

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
    /// Resolves and instantiates the script, attaching it to <paramref name="entity"/>. Returns
    /// <see langword="null"/> (logging on failure) when the type is unresolved or construction throws, so
    /// callers keep the class name as an unresolved entry rather than crashing.
    /// </summary>
    public static EntityBehaviour? Create(string className, Entity entity)
    {
        Type? type = Resolve(className);
        if (type == null)
        {
            Log.CoreWarn("Failed to load script '{0}'. Type not found.", className);
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
}

using Spot.Engine.Scenes;

namespace SolarSystem;

/// <summary>Looks up script instances on entities, for scripts that work together.</summary>
public static class SceneScripts
{
    /// <summary>Returns the first script of type <typeparamref name="T"/> on an entity, if it has one.</summary>
    public static T? Find<T>(Entity entity) where T : EntityBehaviour
    {
        if (!entity.TryGetComponent(out ScriptComponent? scripts))
        {
            return null;
        }

        foreach (ScriptInstance item in scripts.Items)
        {
            if (item.Instance is T script)
            {
                return script;
            }
        }

        return null;
    }

    /// <summary>Returns every script of type <typeparamref name="T"/> in a scene.</summary>
    public static List<T> All<T>(Scene scene) where T : EntityBehaviour
    {
        var found = new List<T>();
        foreach (Entity entity in scene.View<ScriptComponent>())
        {
            T? script = Find<T>(entity);
            if (script is not null)
            {
                found.Add(script);
            }
        }

        return found;
    }
}

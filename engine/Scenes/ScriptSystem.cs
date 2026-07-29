namespace Spot.Scenes;

/// <summary>
/// Drives per-entity scripts. Run automatically by the engine for the active scene: each frame it
/// starts newly attached scripts (<see cref="EntityBehaviour.OnCreate"/>) and updates them
/// (<see cref="EntityBehaviour.OnUpdate"/>).
/// </summary>
internal static class ScriptSystem
{
    public static void Update(Scene scene, float deltaTime)
    {
        foreach (Entity entity in scene.View<ScriptComponent>())
        {
            foreach (EntityBehaviour script in entity.GetComponent<ScriptComponent>().Scripts)
            {
                if (!script.Started)
                {
                    script.OnCreate();
                    script.Started = true;
                }

                script.OnUpdate(deltaTime);
            }
        }
    }

    public static void DestroyAll(Scene scene)
    {
        foreach (Entity entity in scene.View<ScriptComponent>())
        {
            foreach (EntityBehaviour script in entity.GetComponent<ScriptComponent>().Scripts)
            {
                if (script.Started)
                {
                    script.OnDestroy();
                }
            }
        }
    }
}

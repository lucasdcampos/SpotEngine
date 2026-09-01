using System;
using System.Collections.Generic;
using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// Advances every <see cref="AnimatorComponent"/> each frame in play mode, sampling its current clip and
/// posing the bone entities. Mirrors the other systems: a static <c>Update</c> called from
/// <see cref="Scene.UpdateRuntime"/>, iterating a component view and quarantining any animator that throws so
/// a bad clip never takes the engine down.
/// </summary>
public static class AnimationSystem
{
    /// <summary>Ticks every active animator in the scene.</summary>
    /// <param name="scene">The scene to update.</param>
    /// <param name="deltaTime">The elapsed time in seconds since the previous frame.</param>
    public static void Update(Scene scene, float deltaTime)
    {
        foreach ((Entity entity, AnimatorComponent animator) in scene.ViewActive<AnimatorComponent>())
        {
            try
            {
                animator.Tick(entity, deltaTime);
            }
            catch (Exception ex)
            {
                // Quarantine a faulty animator (disable it) so it never spams or crashes the frame loop.
                Log.CoreError("Animator on '{0}' threw and was disabled: {1}", entity.Name, ex.Message);
                animator.Enabled = false;
            }
        }
    }

    /// <summary>
    /// Builds a name → entity map of <paramref name="root"/> and all its descendants (depth-first, first
    /// occurrence wins), keyed by <see cref="NormalizeBoneName"/>. Used to bind animation channels and skinning
    /// bones to the entities of matching name — including across Mixamo exports whose skeleton namespace differs.
    /// </summary>
    /// <param name="root">The subtree root to map.</param>
    /// <returns>A dictionary from normalized entity name to entity.</returns>
    internal static Dictionary<string, Entity> MapDescendantsByName(Entity root)
    {
        var map = new Dictionary<string, Entity>(StringComparer.Ordinal);
        Collect(root, map);
        return map;
    }

    /// <summary>Canonicalizes a bone/node name so animation channels retarget across Mixamo exports.</summary>
    /// <param name="name">The raw node or bone name.</param>
    /// <returns>The name with its Mixamo skeleton namespace canonicalized to <c>mixamorig:</c>.</returns>
    internal static string NormalizeBoneName(string name) => Spot.Animation.BoneName.Normalize(name);

    private static void Collect(Entity entity, Dictionary<string, Entity> map)
    {
        string name = entity.Name;
        if (!string.IsNullOrEmpty(name))
        {
            map.TryAdd(NormalizeBoneName(name), entity);
        }

        foreach (Entity child in entity.Children)
        {
            Collect(child, map);
        }
    }
}

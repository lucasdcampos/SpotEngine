using System;
using System.Collections.Generic;
using Spot.Core;

namespace Spot.Scenes;

/// <summary>
/// The load-time context that resolves <see cref="Entity"/>-typed references within a scene or prefab. Entity
/// references serialize as the target's stable id (see <see cref="Entity.PersistentId"/>), but the target may
/// not be created yet when a referencing script is read, so assignments are deferred: entities register their
/// id as they are created, references are queued, and <see cref="ResolveDeferred"/> binds them once the whole
/// subtree has loaded. Unresolved ids (a reference to a deleted entity) are logged and left at their default,
/// never thrown, per the engine's never-crash rule.
/// </summary>
internal sealed class SceneReferences
{
    private readonly Dictionary<string, Entity> _byId = new(StringComparer.Ordinal);
    private readonly List<(string Id, Action<Entity> Assign)> _deferred = new();

    /// <summary>Records that <paramref name="entity"/> was loaded under the serialized id <paramref name="id"/>.</summary>
    public void Register(string id, Entity entity)
    {
        if (!string.IsNullOrEmpty(id))
        {
            _byId[id] = entity;
        }
    }

    /// <summary>
    /// Queues an assignment to run once loading finishes, passing the entity that was registered under
    /// <paramref name="id"/>. A no-op when the id is empty.
    /// </summary>
    public void Defer(string id, Action<Entity> assign)
    {
        if (!string.IsNullOrEmpty(id))
        {
            _deferred.Add((id, assign));
        }
    }

    /// <summary>
    /// Runs every deferred assignment, binding each reference to the entity registered under its id. A
    /// reference whose id was never registered (its target is gone) is logged and skipped.
    /// </summary>
    public void ResolveDeferred()
    {
        foreach ((string id, Action<Entity> assign) in _deferred)
        {
            if (!_byId.TryGetValue(id, out Entity entity))
            {
                Log.CoreWarn("Entity reference '{0}' could not be resolved; leaving it unset.", id);
                continue;
            }

            try
            {
                assign(entity);
            }
            catch (Exception ex)
            {
                Log.CoreWarn("Failed to bind entity reference '{0}': {1}", id, ex.Message);
            }
        }

        _deferred.Clear();
    }
}

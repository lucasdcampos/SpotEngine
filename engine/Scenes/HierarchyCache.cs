namespace Spot.Scenes;

/// <summary>
/// Memoizes <c>IsActiveInHierarchy</c> per entity id for one scene. An entity's active state — its
/// <see cref="LabelComponent"/>.<c>Enabled</c> flag walked up the parent chain — is queried many times per
/// frame (every <c>ViewActive</c> and each <see cref="RenderSystem"/> pass), so it is cached and recomputed
/// only when something that affects it changes.
/// </summary>
/// <remarks>
/// Composed and driven by the owning <see cref="EntityRegistry"/>, which calls <see cref="Invalidate"/> on
/// every component add/remove, reparent, or <c>Enabled</c> toggle. It reads component state back through the
/// same registry, so there is no separate copy of the hierarchy to keep in sync.
/// </remarks>
internal sealed class HierarchyCache
{
    private readonly EntityRegistry _registry;
    private readonly Dictionary<int, bool> _memo = new();

    public HierarchyCache(EntityRegistry registry) => _registry = registry;

    /// <summary>Drops the cached results so the next query recomputes.</summary>
    public void Invalidate() => _memo.Clear();

    public bool IsActiveInHierarchy(int entityId)
    {
        if (_memo.TryGetValue(entityId, out bool cached))
        {
            return cached;
        }

        bool active = Compute(entityId);
        _memo[entityId] = active;
        return active;
    }

    private bool Compute(int entityId)
    {
        int currentId = entityId;
        while (true)
        {
            // An entity with no label, or a disabled label anywhere up the chain, is inactive.
            if (_registry.Get(typeof(LabelComponent), currentId) is not LabelComponent label)
            {
                return false;
            }
            if (!label.Enabled)
            {
                return false;
            }

            if (_registry.Get(typeof(RelationshipComponent), currentId) is RelationshipComponent rel &&
                rel.Parent is Entity parent)
            {
                currentId = parent.Id;
                continue;
            }
            break;
        }

        return true;
    }
}

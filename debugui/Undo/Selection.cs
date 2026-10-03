using Spot.Engine.Scenes;
using Spot.Engine.UI;

namespace Spot.DebugUI.Undo;

/// <summary>
/// What was selected at a point in the history. Restored alongside the state an action reverts, so an
/// undo puts the user back in front of the thing that changed instead of clearing their selection.
/// </summary>
/// <param name="Target">Which tree the Hierarchy was showing.</param>
/// <param name="EntityIds">
/// The selected entities as stable <see cref="Entity.PersistentId"/> values, in selection order. The
/// primary selection is the last element, matching <see cref="ISelectionContext.SelectedEntities"/>.
/// Stable ids are used because every runtime <c>int</c> id is re-minted by a scene re-hydration.
/// </param>
/// <param name="WidgetPath">
/// The selected UI widget as a path of child indices from the document root, or <see langword="null"/>
/// when no widget was selected. Widgets have no stable identity (<see cref="Widget.Name"/> is not
/// unique), so position in the tree is the best available key.
/// </param>
/// <param name="AssetPath">The inspected asset path, or <see langword="null"/>.</param>
public sealed record SelectionSnapshot(
    HierarchyTarget Target,
    string[] EntityIds,
    int[]? WidgetPath,
    string? AssetPath)
{
    /// <summary>An empty selection, used as the baseline before anything has been selected.</summary>
    public static readonly SelectionSnapshot Empty =
        new(HierarchyTarget.Scene, Array.Empty<string>(), null, null);
}

/// <summary>
/// Captures and restores the editor's selection for the undo history. Abstracted so the history core
/// stays testable without an editor context.
/// </summary>
public interface ISelectionStore
{
    /// <summary>Records what is selected right now.</summary>
    SelectionSnapshot Capture();

    /// <summary>Restores a recorded selection, silently dropping anything that no longer exists.</summary>
    void Apply(SelectionSnapshot snapshot);
}

/// <summary>
/// The real selection store, working against the shared <see cref="ISelectionContext"/> that every
/// panel already receives.
/// </summary>
public sealed class ContextSelectionStore(ISelectionContext context) : ISelectionStore
{
    /// <inheritdoc />
    public SelectionSnapshot Capture()
    {
        var ids = new List<string>();
        foreach (Entity entity in context.SelectedEntities)
        {
            if (entity.IsValid)
            {
                // Allocating the id here is safe and idempotent: it is the same id the serializer would
                // assign on the next save.
                ids.Add(entity.EnsurePersistentId());
            }
        }

        return new SelectionSnapshot(
            context.HierarchyTarget,
            ids.ToArray(),
            PathOf(context.SelectedWidget, context.EditingDocument),
            context.SelectedAssetPath);
    }

    /// <inheritdoc />
    public void Apply(SelectionSnapshot snapshot)
    {
        Scene? scene = context.ActiveScene;
        if (scene != null && snapshot.EntityIds.Length > 0)
        {
            var entities = new List<Entity>();
            foreach (string id in snapshot.EntityIds)
            {
                if (scene.EntityByPersistentId(id) is Entity entity)
                {
                    entities.Add(entity);
                }
            }

            context.SetSelectedEntities(entities);
        }
        else
        {
            context.SetSelectedEntities(Array.Empty<Entity>());
        }

        context.HierarchyTarget = snapshot.Target;

        // Only touch the widget/asset selection when this snapshot owned one, so restoring a scene edit
        // does not clear an unrelated asset the user is inspecting.
        if (snapshot.WidgetPath != null)
        {
            context.SelectedWidget = Resolve(snapshot.WidgetPath, context.EditingDocument);
        }

        if (snapshot.AssetPath != null)
        {
            context.SelectedAssetPath = snapshot.AssetPath;
        }
    }

    // Walks up from a widget to the document root, recording the child index at each step.
    private static int[]? PathOf(Widget? widget, UIRoot? document)
    {
        if (widget == null || document == null)
        {
            return null;
        }

        var path = new List<int>();
        Widget? current = widget;
        while (current != null && !ReferenceEquals(current, document))
        {
            Widget? parent = current.Parent;
            if (parent == null)
            {
                // Detached from the document; there is no meaningful path to record.
                return null;
            }

            int index = IndexOf(parent, current);
            if (index < 0)
            {
                return null;
            }

            path.Add(index);
            current = parent;
        }

        if (current == null)
        {
            return null;
        }

        path.Reverse();
        return path.ToArray();
    }

    // Follows a recorded child-index path back down from the root.
    private static Widget? Resolve(int[] path, UIRoot? document)
    {
        if (document == null)
        {
            return null;
        }

        Widget current = document;
        foreach (int index in path)
        {
            IReadOnlyList<Widget> children = current.Children;
            if (index < 0 || index >= children.Count)
            {
                return null;
            }

            current = children[index];
        }

        return ReferenceEquals(current, document) ? null : current;
    }

    private static int IndexOf(Widget parent, Widget child)
    {
        IReadOnlyList<Widget> children = parent.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], child))
            {
                return i;
            }
        }

        return -1;
    }
}

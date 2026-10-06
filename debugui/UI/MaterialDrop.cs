using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Spot.DebugUI.Undo;
using Spot.Engine.Assets;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace Spot.DebugUI.UI;

/// <summary>
/// Applies a material dragged from the Asset Browser to what it is dropped on — the mesh under the cursor in
/// the viewport, or an entity in the Hierarchy — so restyling an object takes one drag instead of selecting it
/// and finding the slot in the Inspector.
/// </summary>
/// <remarks>
/// Each drop is one undo step made of precise <see cref="ComponentValueAction"/>s, one per painted mesh.
/// </remarks>
public static class MaterialDrop
{
    /// <summary>The drag-drop payload type the Asset Browser gives a material.</summary>
    public const string PayloadType = "MATERIAL_FILE";

    // Swaps the reference and drops the loaded material, so the renderer resolves the new one on its next draw.
    // Undo and redo write through the same accessor, so they swap the material exactly the same way.
    private static readonly MemberAccessor s_materialAccessor = MemberAccessor.FromDelegates(
        nameof(MeshRenderer.MaterialPath), typeof(string), "MeshRenderer.MaterialDrop",
        target => ((MeshRenderer)target).MaterialPath,
        (target, value) =>
        {
            var mesh = (MeshRenderer)target;
            mesh.MaterialPath = (string?)value;
            mesh.Material = null;
        });

    /// <summary>Whether the drag in progress (if any) carries a material.</summary>
    public static bool IsDragging()
    {
        ImGuiPayloadPtr payload = ImGui.GetDragDropPayload();
        unsafe
        {
            return payload.NativePtr != null && payload.IsDataType(PayloadType);
        }
    }

    /// <summary>
    /// The meshes a material dropped on <paramref name="entity"/> paints: its own mesh or, for an entity without
    /// one (the root of an imported model, a group), every mesh beneath it.
    /// </summary>
    public static List<Entity> TargetsFor(Entity entity)
    {
        var targets = new List<Entity>();
        if (!entity.IsValid)
        {
            return targets;
        }

        if (entity.HasComponent<MeshRenderer>())
        {
            targets.Add(entity);
            return targets;
        }

        CollectMeshes(entity, targets);
        return targets;
    }

    /// <summary>
    /// Accepts a material payload on the current drag-drop target (call between <c>ImGui.BeginDragDropTarget</c>
    /// and <c>ImGui.EndDragDropTarget</c>).
    /// </summary>
    /// <param name="path">The dragged material's path or built-in reference.</param>
    /// <param name="delivered">Whether the mouse was released, i.e. the material was dropped here this frame.</param>
    /// <param name="preview">Also report the payload while it merely hovers the target.</param>
    /// <param name="drawRect">Whether ImGui highlights the whole target; off when the caller draws its own feedback.</param>
    public static bool Accept(out string path, out bool delivered, bool preview = false, bool drawRect = true)
    {
        path = string.Empty;
        delivered = false;

        ImGuiDragDropFlags flags = (preview ? ImGuiDragDropFlags.AcceptBeforeDelivery : ImGuiDragDropFlags.None)
            | (drawRect ? ImGuiDragDropFlags.None : ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
        ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(PayloadType, flags);
        unsafe
        {
            if (payload.NativePtr == null) return false;
        }

        string? data = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
        if (string.IsNullOrEmpty(data)) return false;

        path = data;
        delivered = payload.IsDelivery();
        return true;
    }

    /// <summary>
    /// Assigns the material to every target that does not already use it, recorded as one undo step.
    /// </summary>
    /// <param name="targets">Entities with a <see cref="MeshRenderer"/>; others are skipped.</param>
    /// <param name="materialPath">The material's source path or built-in reference.</param>
    /// <param name="history">The history to record into; the editor's own when <see langword="null"/>.</param>
    /// <returns>How many meshes changed.</returns>
    public static int Apply(IEnumerable<Entity> targets, string materialPath, UndoHistory? history = null)
    {
        history ??= EditorHistory.Current;
        string? reference = BuiltinAssets.Canonicalize(AssetDatabase.ToGuidRef(materialPath));

        List<Entity> changed = targets
            .Where(e => e.IsValid && e.HasComponent<MeshRenderer>()
                && e.GetComponent<MeshRenderer>().MaterialPath != reference)
            .ToList();
        if (changed.Count == 0)
        {
            return 0;
        }

        string label = $"Assign Material '{AssetSpawner.NameFor(materialPath)}'";
        using (history.Group(label))
        {
            foreach (Entity entity in changed)
            {
                var mesh = entity.GetComponent<MeshRenderer>();
                string? before = mesh.MaterialPath;
                s_materialAccessor.Set(mesh, reference);

                Scene scene = entity.Scene;
                history.Push(new ComponentValueAction(
                    label, scene, entity.EnsurePersistentId(), typeof(MeshRenderer), s_materialAccessor,
                    before, mesh.MaterialPath, EditorHistory.DocumentFor(scene)));
            }
        }

        return changed.Count;
    }

    private static void CollectMeshes(Entity entity, List<Entity> into)
    {
        foreach (Entity child in entity.Children)
        {
            if (child.HasComponent<MeshRenderer>()) into.Add(child);
            CollectMeshes(child, into);
        }
    }
}

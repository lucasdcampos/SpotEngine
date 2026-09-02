using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.DebugUI.UI;
using Spot.UI;
using Spot.UI.Serialization;

namespace Spot.DebugUI.Panels;

/// <summary>
/// The widget tree of the open UI document (a <c>.sptui</c>), mirroring the scene hierarchy for entities:
/// select, add, delete, rename, duplicate and drag-drop reparent widgets. It edits the live tree the UI
/// Canvas renders and the Inspector shows, so the three panels stay in lockstep.
/// </summary>
public class UIHierarchyPanel
{
    private readonly ISelectionContext _context;

    private Widget? _renaming;
    private string _renameBuffer = "";
    private bool _renameFocusPending;

    // The widget currently being dragged (ImGui payloads can't carry a managed reference, so we stash it).
    private Widget? _dragSource;

    public UIHierarchyPanel(ISelectionContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Draws the open document's widget tree into the current window. The editor hosts it inside the shared
    /// "Hierarchy" panel (switched to via the UI Canvas), so this draws no window of its own.
    /// </summary>
    public void DrawContents()
    {
        UIRoot? doc = _context.EditingDocument;
        if (doc == null)
        {
            ImGui.TextDisabled("No UI document open.");
            ImGui.TextWrapped("Double-click a .sptui asset, or create one from the Asset Browser, to author a UI.");
            return;
        }

        var style = ImGui.GetStyle();
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 20.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(style.ItemSpacing.X, 5.0f));

        // Toolbar: an add menu that targets the selection (or the root when nothing is selected).
        if (ImGui.Button($"{EditorIcons.Circle}  Add Widget"))
            ImGui.OpenPopup("AddWidget");
        if (ImGui.BeginPopup("AddWidget"))
        {
            DrawAddMenu(_context.SelectedWidget ?? (Widget)doc);
            ImGui.EndPopup();
        }

        ImGui.Separator();

        foreach (Widget child in doc.Children.ToList())
            DrawWidgetNode(child);

        // Click empty space clears the selection; drop on empty space reparents to the root.
        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered())
            _context.SelectedWidget = null;

        if (ImGui.BeginDragDropTarget())
        {
            if (AcceptWidgetDrop(out Widget? dragged) && dragged != null && !IsSelfOrDescendant(doc, dragged))
                doc.Add(dragged);
            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopupContextWindow("UIHierarchyContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            DrawAddMenu(doc);
            ImGui.EndPopup();
        }

        HandleShortcuts(doc);

        ImGui.PopStyleVar(2);
    }

    private void DrawAddMenu(Widget parent)
    {
        if (ImGui.MenuItem("Panel")) Create(parent, parent.Panel(), "Panel");
        if (ImGui.MenuItem("Button")) Create(parent, parent.Button("Button"), "Button");
        if (ImGui.MenuItem("Text")) Create(parent, parent.Text("Text"), "Text");
        if (ImGui.MenuItem("Image")) Create(parent, parent.Add(new Image()), "Image");
        if (ImGui.MenuItem("Slider")) Create(parent, parent.Slider(), "Slider");
        if (ImGui.MenuItem("Toggle")) Create(parent, parent.Toggle("Toggle"), "Toggle");
    }

    private void Create(Widget parent, Widget widget, string defaultName)
    {
        _ = parent;
        widget.Name = defaultName;
        _context.SelectedWidget = widget;
        BeginRename(widget);
    }

    private void DrawWidgetNode(Widget widget)
    {
        bool isRenaming = _renaming == widget;
        bool selected = _context.SelectedWidget == widget;

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow;
        if (selected) flags |= ImGuiTreeNodeFlags.Selected;
        if (!isRenaming) flags |= ImGuiTreeNodeFlags.SpanAvailWidth;
        if (widget.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;

        string glyph = WidgetGlyph(widget);
        string label = isRenaming ? glyph + "   " : $"{glyph}   {(string.IsNullOrEmpty(widget.Name) ? TypeName(widget) : widget.Name)}";

        if (!widget.Visible) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        bool opened = ImGui.TreeNodeEx((IntPtr)widget.GetHashCode(), flags, label);
        if (!widget.Visible) ImGui.PopStyleColor();

        if (isRenaming)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 4);
            bool focusThisFrame = _renameFocusPending;
            if (_renameFocusPending)
            {
                ImGui.SetKeyboardFocusHere();
                _renameFocusPending = false;
            }
            bool submitted = ImGui.InputText("##uirename", ref _renameBuffer, 128,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
            bool lostFocus = !focusThisFrame && !ImGui.IsItemActive();
            if (submitted || lostFocus)
            {
                string trimmed = _renameBuffer.Trim();
                if (!string.IsNullOrEmpty(trimmed)) widget.Name = trimmed;
                _renaming = null;
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _renaming = null;
            }
        }
        else if (ImGui.IsItemClicked())
        {
            _context.SelectedWidget = widget;
        }

        if (ImGui.BeginDragDropSource())
        {
            _dragSource = widget;
            unsafe
            {
                byte marker = 1;
                ImGui.SetDragDropPayload("UIWIDGET", (IntPtr)(&marker), 1);
            }
            ImGui.Text(string.IsNullOrEmpty(widget.Name) ? TypeName(widget) : widget.Name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            if (AcceptWidgetDrop(out Widget? dragged) && dragged != null && dragged != widget && !IsSelfOrDescendant(dragged, widget))
                widget.Add(dragged);
            ImGui.EndDragDropTarget();
        }

        bool deleteRequested = false;
        if (ImGui.BeginPopupContextItem())
        {
            _context.SelectedWidget = widget;
            if (ImGui.BeginMenu("Add Child"))
            {
                DrawAddMenu(widget);
                ImGui.EndMenu();
            }
            if (ImGui.MenuItem("Rename", "F2")) BeginRename(widget);
            if (ImGui.MenuItem("Duplicate", "Ctrl+D")) Duplicate(widget);
            ImGui.Separator();
            if (ImGui.MenuItem("Delete", "Del")) deleteRequested = true;
            ImGui.EndPopup();
        }

        if (opened)
        {
            foreach (Widget child in widget.Children.ToList())
                DrawWidgetNode(child);
            ImGui.TreePop();
        }

        if (deleteRequested)
            Delete(widget);
    }

    private void HandleShortcuts(UIRoot doc)
    {
        _ = doc;
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || _renaming != null || ImGui.IsAnyItemActive())
            return;

        Widget? sel = _context.SelectedWidget;
        if (sel == null) return;

        if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.D)) Duplicate(sel);
        else if (ImGui.IsKeyPressed(ImGuiKey.F2)) BeginRename(sel);
        else if (ImGui.IsKeyPressed(ImGuiKey.Delete)) Delete(sel);
    }

    private void BeginRename(Widget widget)
    {
        _renaming = widget;
        _renameBuffer = widget.Name;
        _renameFocusPending = true;
    }

    private void Duplicate(Widget widget)
    {
        Widget? parent = widget.Parent;
        if (parent == null) return;
        try
        {
            Widget copy = UISerializer.Clone(widget);
            parent.Add(copy);
            _context.SelectedWidget = copy;
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Failed to duplicate widget: {0}", ex.Message);
        }
    }

    private void Delete(Widget widget)
    {
        widget.Parent?.Remove(widget);
        if (_context.SelectedWidget == widget) _context.SelectedWidget = null;
        if (_renaming == widget) _renaming = null;
    }

    private bool AcceptWidgetDrop(out Widget? dragged)
    {
        dragged = null;
        unsafe
        {
            ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("UIWIDGET");
            if (payload.NativePtr != null)
            {
                dragged = _dragSource;
                return dragged != null;
            }
        }
        return false;
    }

    private static bool IsSelfOrDescendant(Widget node, Widget ancestor)
    {
        Widget? current = node;
        while (current != null)
        {
            if (current == ancestor) return true;
            current = current.Parent;
        }
        return false;
    }

    private static string TypeName(Widget widget) => UISerializer.TypeName(widget) is { Length: > 0 } name ? name : "Widget";

    private static string WidgetGlyph(Widget widget) => widget switch
    {
        Panel => EditorIcons.File,
        Image => EditorIcons.Image,
        Text => EditorIcons.Code,
        Slider => EditorIcons.Move,
        _ => EditorIcons.Circle,
    };
}

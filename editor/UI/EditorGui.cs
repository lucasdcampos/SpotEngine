using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.Assets;
using Spot.Rendering;
using Spot.Scenes;
using Spot.Core;

namespace Spot.Editor.UI;

/// <summary>
/// Reusable ImGui widgets for the editor's inspector-style panels. Centralizing these here keeps
/// panels declarative (one call per property) and gives the whole editor a consistent look: labels
/// live in a left column, editors fill the right column, and vector fields get color-coded X/Y/Z
/// badges. Every helper returns whether the value changed so callers can persist edits.
/// </summary>
public static class EditorGui
{
    /// <summary>Default width, in pixels, reserved for a property's label column.</summary>
    public const float LabelColumnWidth = 110.0f;

    private static EditorPalette Palette => EditorThemeManager.Current.Palette;

    // ----- Property rows ---------------------------------------------------------------------------

    /// <summary>
    /// A three-axis field (position/rotation/scale, or any <see cref="Vector3"/>). Each axis has a
    /// colored badge — red X, green Y, blue Z — that resets that component to <paramref name="resetValue"/>
    /// when clicked, matching the axis colors used by the viewport gizmo.
    /// </summary>
    public static bool Vector3Control(string label, ref Vector3 value, float resetValue = 0.0f, float speed = 0.1f)
    {
        ImGui.PushID(label);
        BeginLabel(label);

        bool changed = false;
        var p = Palette;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(AxisSpacing, 0.0f));
        Vector2 badge = BadgeSize();
        float dragWidth = AxisDragWidth(3, badge.X);

        changed |= Axis("X", p.AxisX, badge, dragWidth, ref value.X, resetValue, speed);
        ImGui.SameLine();
        changed |= Axis("Y", p.AxisY, badge, dragWidth, ref value.Y, resetValue, speed);
        ImGui.SameLine();
        changed |= Axis("Z", p.AxisZ, badge, dragWidth, ref value.Z, resetValue, speed);

        ImGui.PopStyleVar();
        EndLabel();
        return changed;
    }

    /// <summary>A two-axis field for any <see cref="Vector2"/>, with color-coded X/Y badges.</summary>
    public static bool Vector2Control(string label, ref Vector2 value, float resetValue = 0.0f, float speed = 0.1f)
    {
        ImGui.PushID(label);
        BeginLabel(label);

        bool changed = false;
        var p = Palette;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(AxisSpacing, 0.0f));
        Vector2 badge = BadgeSize();
        float dragWidth = AxisDragWidth(2, badge.X);

        changed |= Axis("X", p.AxisX, badge, dragWidth, ref value.X, resetValue, speed);
        ImGui.SameLine();
        changed |= Axis("Y", p.AxisY, badge, dragWidth, ref value.Y, resetValue, speed);

        ImGui.PopStyleVar();
        EndLabel();
        return changed;
    }

    /// <summary>A labeled single float drag.</summary>
    public static bool DragFloat(string label, ref float value, float speed = 0.1f, float min = 0.0f, float max = 0.0f, string format = "%.2f")
    {
        ImGui.PushID(label);
        BeginLabel(label);
        ImGui.SetNextItemWidth(-1.0f);
        bool changed = ImGui.DragFloat("##v", ref value, speed, min, max, format);
        EndLabel();
        return changed;
    }

    /// <summary>A labeled checkbox.</summary>
    public static bool Checkbox(string label, ref bool value)
    {
        ImGui.PushID(label);
        BeginLabel(label);
        bool changed = ImGui.Checkbox("##v", ref value);
        EndLabel();
        return changed;
    }

    /// <summary>A labeled RGB color picker.</summary>
    public static bool Color3(string label, ref Vector3 value)
    {
        ImGui.PushID(label);
        BeginLabel(label);
        ImGui.SetNextItemWidth(-1.0f);
        bool changed = ImGui.ColorEdit3("##v", ref value);
        EndLabel();
        return changed;
    }

    /// <summary>A labeled RGBA color picker.</summary>
    public static bool Color4(string label, ref Vector4 value)
    {
        ImGui.PushID(label);
        BeginLabel(label);
        ImGui.SetNextItemWidth(-1.0f);
        bool changed = ImGui.ColorEdit4("##v", ref value);
        EndLabel();
        return changed;
    }

    /// <summary>A labeled dropdown over the given options.</summary>
    public static bool Combo(string label, ref int current, string[] options)
    {
        ImGui.PushID(label);
        BeginLabel(label);
        ImGui.SetNextItemWidth(-1.0f);
        bool changed = ImGui.Combo("##v", ref current, options, options.Length);
        EndLabel();
        return changed;
    }

    /// <summary>A labeled text input.</summary>
    public static bool InputText(string label, ref string value, uint maxLength = 256)
    {
        ImGui.PushID(label);
        BeginLabel(label);
        ImGui.SetNextItemWidth(-1.0f);
        bool changed = ImGui.InputText("##v", ref value, maxLength);
        EndLabel();
        return changed;
    }

    // ----- Component header ------------------------------------------------------------------------

    /// <summary>
    /// Draws a collapsible component header (with an optional remove menu) and invokes
    /// <paramref name="drawContents"/> with the component when expanded. Does nothing if the entity
    /// has no component of type <typeparamref name="T"/>. This collapses the header/settings-popup/
    /// tree-pop boilerplate that every component block in the inspector used to repeat.
    /// </summary>
    public static void Component<T>(Entity entity, string title, Action<T> drawContents,
                                    bool removable = true, bool defaultOpen = true) where T : class
    {
        if (!entity.HasComponent<T>()) return;
        T component = entity.GetComponent<T>();
        Component(entity, typeof(T), title, removable, () => drawContents(component), defaultOpen);
    }

    /// <summary>
    /// Type-erased counterpart of <see cref="Component{T}"/>, for callers that only know the component's
    /// <see cref="Type"/> at runtime (the reflection-based inspector). Draws the collapsible header (with an
    /// optional remove menu) and invokes <paramref name="drawContents"/> when expanded. Does nothing if the
    /// entity has no component of <paramref name="type"/>.
    /// </summary>
    public static void Component(Entity entity, Type type, string title, bool removable, Action drawContents,
                                 bool defaultOpen = true)
    {
        if (!entity.HasComponent(type)) return;

        var p = Palette;
        ImGui.PushID(type.Name);

        // Each component is its own card: a rounded, hairline-bordered surface that auto-sizes to its
        // content, with a little air between cards so sections read as distinct groups rather than one
        // continuous list. A child window (not a draw-list channel split) is used deliberately — the
        // property rows below use ImGui.Columns, which owns the window's draw-list splitter, so wrapping
        // them in our own split would corrupt rendering. The child gives each card its own draw list.
        ImGui.Dummy(new Vector2(0.0f, 3.0f));

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Lighten(p.WindowBg, 0.02f));
        ImGui.PushStyleColor(ImGuiCol.Border, p.Border);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10.0f, 8.0f));

        ImGui.BeginChild("card", new Vector2(0.0f, 0.0f),
            ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.Border);

        // Header row: a plain (unframed) collapsing node so the title sits on the card surface, with a
        // hairline divider under it — matching the "▼ Title / ---- / fields" layout of pro inspectors.
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.AllowOverlap
            | ImGuiTreeNodeFlags.FramePadding;
        if (defaultOpen) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, WithAlpha(p.Text, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, WithAlpha(p.Text, 0.10f));
        EditorFonts.PushTitle();
        bool opened = ImGui.TreeNodeEx(title, flags);
        EditorFonts.Pop();
        ImGui.PopStyleColor(3);

        bool removeRequested = false;
        if (removable)
        {
            float size = ImGui.GetFrameHeight();
            ImGui.SameLine(ImGui.GetWindowWidth() - size - ImGui.GetStyle().WindowPadding.X);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, WithAlpha(p.Text, 0.10f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, WithAlpha(p.Text, 0.16f));
            if (ImGui.Button(EditorIcons.EllipsisV, new Vector2(size, size)))
                ImGui.OpenPopup("ComponentSettings");
            ImGui.PopStyleColor(3);
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeRequested = true;
                ImGui.EndPopup();
            }
        }

        if (opened)
        {
            // Divider between the header and the properties, inset to the card's padding.
            ImGui.Spacing();
            var dl = ImGui.GetWindowDrawList();
            Vector2 lineStart = ImGui.GetCursorScreenPos();
            float innerWidth = ImGui.GetContentRegionAvail().X;
            dl.AddLine(lineStart, lineStart + new Vector2(innerWidth, 0.0f),
                ImGui.GetColorU32(p.Separator), 1.0f);
            ImGui.Dummy(new Vector2(0.0f, 4.0f));

            drawContents();
            ImGui.TreePop();
        }

        ImGui.EndChild();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);

        if (removeRequested)
            entity.RemoveComponent(type);

        ImGui.PopID();
    }

    /// <summary>
    /// Menu item for the "Add Component" popup that adds a fresh <typeparamref name="T"/> when chosen.
    /// The item is hidden while the entity already carries that component.
    /// </summary>
    public static void AddComponentItem<T>(Entity entity, string label) where T : Component, new()
    {
        if (entity.HasComponent<T>()) return;
        if (ImGui.MenuItem(label))
        {
            entity.AddComponent(new T());
            ImGui.CloseCurrentPopup();
        }
    }

    // ----- Entity icons ----------------------------------------------------------------------------

    /// <summary>A broad visual category for an entity, derived from its most defining component.</summary>
    public enum EntityIcon { Empty, Mesh, Camera, Light, Sprite, Skybox }

    /// <summary>Picks the icon that best represents what an entity is.</summary>
    public static EntityIcon IconFor(Entity entity)
    {
        if (entity.HasComponent<CameraComponent>()) return EntityIcon.Camera;
        if (entity.HasComponent<LightComponent>()) return EntityIcon.Light;
        if (entity.HasComponent<DynamicCloudsComponent>() || entity.HasComponent<SkyboxComponent>()) return EntityIcon.Skybox;
        if (entity.HasComponent<MeshComponent>()) return EntityIcon.Mesh;
        if (entity.HasComponent<Sprite2DComponent>()) return EntityIcon.Sprite;
        return EntityIcon.Empty;
    }

    /// <summary>The monochrome icon-font glyph that best represents what an entity is.</summary>
    public static string EntityGlyph(Entity entity) => IconFor(entity) switch
    {
        EntityIcon.Camera => EditorIcons.Camera,
        EntityIcon.Light => EditorIcons.Lightbulb,
        EntityIcon.Mesh => EditorIcons.Cube,
        EntityIcon.Sprite => EditorIcons.Image,
        EntityIcon.Skybox => EditorIcons.Cloud,
        _ => EditorIcons.Circle,
    };

    /// <summary>
    /// A string of spaces at least <paramref name="width"/> pixels wide, used to reserve room at the
    /// start of a tree-node label so an icon can be drawn over it via the window draw list.
    /// </summary>
    public static string IconPadding(float width)
    {
        float space = ImGui.CalcTextSize(" ").X;
        int count = space > 0.0f ? (int)MathF.Ceiling(width / space) : 2;
        return new string(' ', Math.Max(count, 1));
    }

    /// <summary>
    /// Draws a small vector glyph for the given entity category, centered at <paramref name="center"/>
    /// with the given radius. Uses the same primitive-drawing style as the launcher's icons so no icon
    /// font is required.
    /// </summary>
    public static void DrawEntityIcon(ImDrawListPtr dl, EntityIcon icon, Vector2 center, float radius, float alpha = 1.0f)
    {
        var p = Palette;
        Vector4 tint = icon switch
        {
            EntityIcon.Mesh => p.Accent,
            EntityIcon.Camera => p.Text,
            EntityIcon.Light => p.GizmoHover,
            EntityIcon.Sprite => p.AxisY,
            EntityIcon.Skybox => new Vector4(0.62f, 0.80f, 1.0f, 1.0f),
            _ => p.TextDisabled,
        };
        uint col = ImGui.GetColorU32(new Vector4(tint.X, tint.Y, tint.Z, tint.W * alpha));

        switch (icon)
        {
            case EntityIcon.Mesh:
            {
                // A cube seen corner-on: hexagon silhouette with three spokes to the shared vertex.
                Span<Vector2> hex = stackalloc Vector2[6];
                for (int i = 0; i < 6; i++)
                {
                    float a = (MathF.PI / 180.0f) * (30.0f + 60.0f * i);
                    hex[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
                }
                for (int i = 0; i < 6; i++)
                    dl.AddLine(hex[i], hex[(i + 1) % 6], col, 1.4f);
                dl.AddLine(center, hex[1], col, 1.4f);
                dl.AddLine(center, hex[3], col, 1.4f);
                dl.AddLine(center, hex[5], col, 1.4f);
                break;
            }
            case EntityIcon.Camera:
            {
                Vector2 bMin = center + new Vector2(-radius, -radius * 0.6f);
                Vector2 bMax = center + new Vector2(radius * 0.35f, radius * 0.6f);
                dl.AddRectFilled(bMin, bMax, col, 2.0f);
                dl.AddTriangleFilled(
                    center + new Vector2(radius * 0.45f, -radius * 0.55f),
                    center + new Vector2(radius, 0.0f),
                    center + new Vector2(radius * 0.45f, radius * 0.55f),
                    col);
                break;
            }
            case EntityIcon.Light:
            {
                dl.AddCircleFilled(center, radius * 0.45f, col);
                for (int i = 0; i < 8; i++)
                {
                    float a = (MathF.PI / 4.0f) * i;
                    Vector2 d = new(MathF.Cos(a), MathF.Sin(a));
                    dl.AddLine(center + d * radius * 0.7f, center + d * radius, col, 1.3f);
                }
                break;
            }
            case EntityIcon.Sprite:
            {
                Vector2 mn = center - new Vector2(radius * 0.9f, radius * 0.9f);
                Vector2 mx = center + new Vector2(radius * 0.9f, radius * 0.9f);
                dl.AddRect(mn, mx, col, 2.0f, ImDrawFlags.None, 1.4f);
                dl.AddCircleFilled(new Vector2(mn.X + radius * 0.55f, mn.Y + radius * 0.5f), radius * 0.2f, col);
                dl.AddLine(new Vector2(mn.X, mx.Y - radius * 0.25f), center, col, 1.4f);
                dl.AddLine(center, new Vector2(mx.X, mx.Y - radius * 0.25f), col, 1.4f);
                break;
            }
            case EntityIcon.Skybox:
            {
                // A cloud silhouette from overlapping puffs on a flat base.
                float r = radius;
                dl.AddRectFilled(center + new Vector2(-r * 0.7f, r * 0.1f), center + new Vector2(r * 0.7f, r * 0.5f), col, r * 0.25f);
                dl.AddCircleFilled(center + new Vector2(-r * 0.45f, r * 0.15f), r * 0.42f, col, 12);
                dl.AddCircleFilled(center + new Vector2(r * 0.45f, r * 0.15f), r * 0.40f, col, 12);
                dl.AddCircleFilled(center + new Vector2(0.0f, -r * 0.18f), r * 0.55f, col, 14);
                break;
            }
            default:
                dl.AddCircle(center, radius * 0.7f, col, 0, 1.5f);
                break;
        }
    }

    // ----- Internals -------------------------------------------------------------------------------

    private const float AxisSpacing = 4.0f;

    // Opens a two-column row: label on the left, the following widget filling the right column. The
    // caller is responsible for the item width (scalar helpers request the full column via
    // ImGui.SetNextItemWidth(-1); vector controls size their fields explicitly).
    private static void BeginLabel(string label)
    {
        ImGui.Columns(2, "row", false);
        ImGui.SetColumnWidth(0, LabelColumnWidth);
        ImGui.AlignTextToFramePadding();
        // Labels sit one notch below the value text so the eye lands on the editable field first.
        ImGui.PushStyleColor(ImGuiCol.Text, WithAlpha(Palette.Text, 0.82f));
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.NextColumn();
    }

    private static void EndLabel()
    {
        ImGui.Columns(1);
        ImGui.PopID();
    }

    // A colored, clickable axis badge followed by its drag field. Clicking the badge resets the value.
    private static bool Axis(string name, Vector4 color, Vector2 size, float dragWidth, ref float value, float resetValue, float speed)
    {
        bool changed = false;
        ImGui.PushStyleColor(ImGuiCol.Button, color);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Lighten(color, 0.1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, color);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 1.0f, 1.0f));
        if (ImGui.Button(name, size))
        {
            value = resetValue;
            changed = true;
        }
        ImGui.PopStyleColor(4);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(dragWidth);
        if (ImGui.DragFloat("##" + name, ref value, speed, 0.0f, 0.0f, "%.2f"))
            changed = true;
        return changed;
    }

    // Width of each drag field so that N badges + N drags + the spacing between them fill the column.
    private static float AxisDragWidth(int axes, float badgeWidth)
    {
        float avail = ImGui.GetContentRegionAvail().X;
        float spacings = (2 * axes - 1) * AxisSpacing;
        float width = (avail - axes * badgeWidth - spacings) / axes;
        return MathF.Max(width, 1.0f);
    }

    // A square badge sized to match the current frame height so it lines up with the drag field.
    private static Vector2 BadgeSize()
    {
        float h = ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.Y * 2.0f;
        return new Vector2(h + 2.0f, h);
    }

    private static Vector4 Lighten(Vector4 c, float amount) => new(
        Math.Clamp(c.X + amount, 0.0f, 1.0f),
        Math.Clamp(c.Y + amount, 0.0f, 1.0f),
        Math.Clamp(c.Z + amount, 0.0f, 1.0f),
        c.W);

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    // ----- Asset Slots -----------------------------------------------------------------------------

    // A single search buffer is fine: only one picker popup can be open at a time. The "just opened"
    // flag lets us focus the search box on the frame the popup appears.
    private static string _assetSearchFilter = string.Empty;
    private static bool _assetPickerJustOpened;

    /// <summary>
    /// Lets a caller contribute extra choices (engine primitives, built-in materials) at the top of an
    /// <see cref="AssetSlot"/> picker. Set <paramref name="selectedPath"/> and <paramref name="changed"/>
    /// when the user picks one.
    /// </summary>
    public delegate void AssetSlotCustomItems(ref string? selectedPath, ref bool changed);

    /// <summary>
    /// A generic slot for an asset reference. Shows the current asset (glyph/thumbnail + name), accepts a
    /// drag-and-drop of the given payload type, and — when clicked — opens a searchable project-wide picker
    /// so the asset can be chosen without dragging. When a value is set, a trailing ✕ clears it. Returns
    /// true (with <paramref name="outPath"/> set) on any change, including clearing (to <c>null</c>).
    /// </summary>
    public static bool AssetSlot(
        string label,
        string payloadType,
        string[] searchPatterns,
        string? currentPath,
        out string? outPath,
        AssetSlotCustomItems? drawCustomItems = null)
    {
        outPath = currentPath;
        bool changed = false;

        ImGui.PushID(label);
        BeginLabel(label);

        bool hasValue = !string.IsNullOrEmpty(currentPath);

        // Reserve room on the right for the clear button when there's something to clear, so the two
        // controls share the value column instead of wrapping.
        float clearWidth = hasValue ? ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X : 0.0f;
        float buttonWidth = ImGui.GetContentRegionAvail().X - clearWidth;

        bool clicked = AssetButton(currentPath, buttonWidth);
        // A drop sets the value directly; it must not also open the picker, so its result isn't OR'd in.
        AcceptAssetDrop(payloadType, ref outPath, ref changed);

        if (hasValue)
        {
            ImGui.SameLine();
            if (ImGui.Button(EditorIcons.Times, new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight())))
            {
                outPath = null;
                changed = true;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear");
        }

        string popupName = "SelectAssetPopup";
        if (clicked)
        {
            _assetSearchFilter = string.Empty;
            _assetPickerJustOpened = true;
            ImGui.OpenPopup(popupName);
        }

        DrawAssetPicker(popupName, searchPatterns, currentPath, ref outPath, ref changed, drawCustomItems);

        EndLabel();
        ImGui.PopID();

        return changed;
    }

    /// <summary>
    /// The value button for a slot: a full-width, left-aligned button showing the asset's kind glyph
    /// (or a live thumbnail for images) and name, or a dim "None" when empty. Returns true when clicked.
    /// </summary>
    private static bool AssetButton(string? path, float width)
    {
        var p = Palette;
        bool empty = string.IsNullOrEmpty(path);
        float h = ImGui.GetFrameHeight();

        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.0f, 0.5f));
        if (empty)
            ImGui.PushStyleColor(ImGuiCol.Text, p.TextDisabled);
        // The label carries a leading glyph and a gap the thumbnail/icon is drawn over.
        string name = empty ? "None" : AssetDisplayName(path!);
        Vector2 btnMin = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.Button("  " + IconPadding(h) + name, new Vector2(width, 0.0f));
        if (empty)
            ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        if (!empty)
        {
            var dl = ImGui.GetWindowDrawList();
            Vector2 iconCenter = btnMin + new Vector2(4.0f + h * 0.5f, h * 0.5f);
            Texture2D? thumb = IsImagePath(path!) ? EditorThumbnails.Get(AssetPath.Resolve(path!)) : null;
            if (thumb != null)
            {
                float s = h - 6.0f;
                Vector2 tl = iconCenter - new Vector2(s * 0.5f, s * 0.5f);
                dl.AddImageRounded((IntPtr)thumb.Handle, tl, tl + new Vector2(s, s),
                    new Vector2(0, 1), new Vector2(1, 0), 0xFFFFFFFF, 3.0f);
            }
            else
            {
                (string glyph, Vector4 color) = AssetGlyph(path!);
                DrawGlyphCentered(dl, EditorFonts.Icons, h * 0.62f, iconCenter, glyph, color);
            }
        }

        if (!empty && ImGui.IsItemHovered())
            ImGui.SetTooltip(path!);
        return clicked;
    }

    // Consumes a drag-drop payload dropped on the last-submitted item. Returns true if the drop set a value.
    private static bool AcceptAssetDrop(string payloadType, ref string? outPath, ref bool changed)
    {
        bool dropped = false;
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload(payloadType);
                if (payload.NativePtr != null)
                {
                    string? filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                    if (filepath != null)
                    {
                        outPath = filepath;
                        changed = true;
                        dropped = true;
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }
        return dropped;
    }

    // The searchable project-wide picker popup shared by every asset slot.
    private static void DrawAssetPicker(
        string popupName,
        string[] searchPatterns,
        string? currentPath,
        ref string? outPath,
        ref bool changed,
        AssetSlotCustomItems? drawCustomItems)
    {
        if (!ImGui.BeginPopup(popupName))
            return;

        ImGui.SetNextItemWidth(320);
        if (_assetPickerJustOpened)
        {
            ImGui.SetKeyboardFocusHere();
            _assetPickerJustOpened = false;
        }
        ImGui.InputTextWithHint("##Search", $"{EditorIcons.Search}  Search assets...", ref _assetSearchFilter, 128);
        ImGui.Separator();

        ImGui.BeginChild("AssetList", new Vector2(320, 340), ImGuiChildFlags.None);

        if (PickerRow(EditorIcons.Times, Palette.TextDisabled, "None", null, string.IsNullOrEmpty(currentPath)))
        {
            outPath = null;
            changed = true;
            ImGui.CloseCurrentPopup();
        }

        drawCustomItems?.Invoke(ref outPath, ref changed);
        if (changed) ImGui.CloseCurrentPopup();

        var assets = EnumerateProjectAssets(searchPatterns);
        if (assets.Count > 0)
            ImGui.Separator();

        string? assetRoot = Project.Active?.GetAssetDirectory();
        bool foundAny = false;
        foreach (string path in assets)
        {
            string filename = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(_assetSearchFilter) &&
                !filename.Contains(_assetSearchFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foundAny = true;
            bool isSelected = string.Equals(currentPath, path, StringComparison.OrdinalIgnoreCase);
            string? subtitle = RelativeFolder(assetRoot, path);
            (string glyph, Vector4 color) = AssetGlyph(path);
            string? thumbPath = IsImagePath(path) ? path : null;

            if (PickerRow(glyph, color, filename, subtitle, isSelected, thumbPath))
            {
                outPath = path;
                changed = true;
                ImGui.CloseCurrentPopup();
            }
        }

        if (!foundAny)
            ImGui.TextDisabled(assets.Count == 0 ? "No assets found." : "No matching assets.");

        ImGui.EndChild();
        ImGui.EndPopup();
    }

    /// <summary>
    /// One row of the asset picker: a thumbnail or kind glyph, the asset name, and a dim folder subtitle.
    /// Returns true when clicked. When <paramref name="thumbPath"/> is set and loads, an image preview is
    /// drawn instead of the glyph.
    /// </summary>
    private static bool PickerRow(string glyph, Vector4 glyphColor, string title, string? subtitle, bool selected, string? thumbPath = null)
    {
        float rowH = MathF.Max(ImGui.GetTextLineHeight() * 2.0f + 6.0f, 34.0f);
        Vector2 min = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.Selectable($"##{title}{subtitle}", selected, ImGuiSelectableFlags.None, new Vector2(0.0f, rowH));

        var dl = ImGui.GetWindowDrawList();
        float pad = 6.0f;
        float thumb = rowH - pad * 2.0f;
        Vector2 iconCenter = min + new Vector2(pad + thumb * 0.5f, rowH * 0.5f);

        Texture2D? tex = thumbPath != null ? EditorThumbnails.Get(AssetPath.Resolve(thumbPath)) : null;
        if (tex != null)
        {
            Vector2 tl = iconCenter - new Vector2(thumb * 0.5f, thumb * 0.5f);
            dl.AddRectFilled(tl, tl + new Vector2(thumb, thumb), 0x40000000, 3.0f);
            dl.AddImageRounded((IntPtr)tex.Handle, tl, tl + new Vector2(thumb, thumb),
                new Vector2(0, 1), new Vector2(1, 0), 0xFFFFFFFF, 3.0f);
        }
        else
        {
            DrawGlyphCentered(dl, EditorFonts.Icons, thumb * 0.7f, iconCenter, glyph, glyphColor);
        }

        float textX = min.X + pad * 2.0f + thumb;
        uint titleCol = ImGui.GetColorU32(Palette.Text);
        if (string.IsNullOrEmpty(subtitle))
        {
            dl.AddText(new Vector2(textX, min.Y + (rowH - ImGui.GetTextLineHeight()) * 0.5f), titleCol, title);
        }
        else
        {
            float lineH = ImGui.GetTextLineHeight();
            float top = min.Y + (rowH - lineH * 2.0f) * 0.5f;
            dl.AddText(new Vector2(textX, top), titleCol, title);
            dl.AddText(new Vector2(textX, top + lineH), ImGui.GetColorU32(Palette.TextDisabled), subtitle);
        }
        return clicked;
    }

    // ----- Script slot -----------------------------------------------------------------------------

    /// <summary>
    /// A picker for an <see cref="EntityBehaviour"/> script by class name. Mirrors <see cref="AssetSlot"/>:
    /// the button accepts a dragged script file and, when clicked, opens a searchable list of every script
    /// type discovered across the loaded assemblies. Returns the chosen class name via
    /// <paramref name="chosenClass"/> when the user picks one; existing selections in
    /// <paramref name="alreadyAdded"/> are shown as disabled so a script isn't attached twice.
    /// </summary>
    public static bool ScriptSlot(string label, IReadOnlyCollection<string> alreadyAdded, out string? chosenClass)
    {
        chosenClass = null;
        bool changed = false;

        ImGui.PushID(label);

        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
        bool clicked = ImGui.Button($"{EditorIcons.Code}  {label}", new Vector2(-1, 30));
        ImGui.PopStyleVar();

        // Drag-drop: the asset browser sends a script's file name (e.g. "Player.cs").
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("SCRIPT_FILE");
                if (payload.NativePtr != null)
                {
                    string? filename = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                    if (filename != null)
                    {
                        chosenClass = filename.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                            ? filename[..^3] : filename;
                        changed = true;
                    }
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (clicked)
        {
            _assetSearchFilter = string.Empty;
            _assetPickerJustOpened = true;
            ImGui.OpenPopup("SelectScriptPopup");
        }

        if (ImGui.BeginPopup("SelectScriptPopup"))
        {
            ImGui.SetNextItemWidth(320);
            if (_assetPickerJustOpened)
            {
                ImGui.SetKeyboardFocusHere();
                _assetPickerJustOpened = false;
            }
            ImGui.InputTextWithHint("##Search", $"{EditorIcons.Search}  Search scripts...", ref _assetSearchFilter, 128);
            ImGui.Separator();

            ImGui.BeginChild("ScriptList", new Vector2(320, 340), ImGuiChildFlags.None);

            var scripts = DiscoverScriptTypes();
            bool foundAny = false;
            foreach (string className in scripts)
            {
                if (!string.IsNullOrEmpty(_assetSearchFilter) &&
                    !className.Contains(_assetSearchFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foundAny = true;
                bool added = alreadyAdded.Contains(className);
                ImGui.BeginDisabled(added);
                if (PickerRow(EditorIcons.Code, ScriptGlyphColor, className, added ? "already attached" : null, false))
                {
                    chosenClass = className;
                    changed = true;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndDisabled();
            }

            if (!foundAny)
                ImGui.TextDisabled(scripts.Count == 0 ? "No scripts found." : "No matching scripts.");

            ImGui.EndChild();
            ImGui.EndPopup();
        }

        ImGui.PopID();
        return changed;
    }

    private static readonly Vector4 ScriptGlyphColor = new(0.36f, 0.66f, 0.98f, 1.0f);

    /// <summary>
    /// The scripts the editor can offer, by class name. The project's scripts live as <c>.cs</c> files under
    /// its asset directory and are only compiled when the game is built, so the editor can't see them by
    /// reflection — it lists them by file name (the same name the serializer resolves at load). Any concrete
    /// <see cref="EntityBehaviour"/> that <em>is</em> loaded (e.g. when running a project in-process) is folded
    /// in too, so built-in scripts still appear.
    /// </summary>
    private static List<string> DiscoverScriptTypes()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in EnumerateProjectAssets(new[] { "*.cs" }))
            names.Add(System.IO.Path.GetFileNameWithoutExtension(path));

        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; } // A partially-loadable assembly must never take the editor down.

            foreach (Type type in types)
            {
                if (!type.IsAbstract && type.IsSubclassOf(typeof(EntityBehaviour)))
                    names.Add(type.Name);
            }
        }
        return names.ToList();
    }

    /// <summary>
    /// Whether a script class name is backed by a <c>.cs</c> file in the project (or a loaded
    /// <see cref="EntityBehaviour"/> type). Used by the inspector to flag attached scripts it can't find.
    /// </summary>
    public static bool ScriptExists(string className)
    {
        string name = className.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            ? className[..^3] : className;

        foreach (string path in EnumerateProjectAssets(new[] { "*.cs" }))
        {
            if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }
            foreach (Type type in types)
            {
                if (!type.IsAbstract && type.Name == name && type.IsSubclassOf(typeof(EntityBehaviour)))
                    return true;
            }
        }
        return false;
    }

    // ----- Asset helpers ---------------------------------------------------------------------------

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };

    private static bool IsImagePath(string path)
    {
        // Pseudo paths ("primitive:Cube", "editor:Checkerboard") never point at image files.
        if (path.Contains(':') && !System.IO.Path.IsPathRooted(path))
            return false;
        return Array.IndexOf(ImageExtensions, System.IO.Path.GetExtension(path).ToLowerInvariant()) >= 0;
    }

    // The display name for a slot value: the tail of a pseudo path ("primitive:Cube" -> "Cube") or the file name.
    private static string AssetDisplayName(string path)
    {
        int colon = path.IndexOf(':');
        if (colon > 0 && !System.IO.Path.IsPathRooted(path))
            return path[(colon + 1)..];
        return System.IO.Path.GetFileName(path);
    }

    // The kind glyph + tint for an asset path, matching the asset browser's color coding.
    private static (string Glyph, Vector4 Color) AssetGlyph(string path)
    {
        if (path.StartsWith("primitive:", StringComparison.OrdinalIgnoreCase))
            return (EditorIcons.Cube, new Vector4(0.98f, 0.62f, 0.26f, 1.0f));
        if (path.StartsWith("editor:", StringComparison.OrdinalIgnoreCase))
            return (EditorIcons.Palette, new Vector4(0.42f, 0.72f, 1.00f, 1.0f));

        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => (EditorIcons.Code, new Vector4(0.36f, 0.66f, 0.98f, 1.0f)),
            ".sptscene" => (EditorIcons.Cubes, new Vector4(0.66f, 0.40f, 0.98f, 1.0f)),
            ".sptmat" => (EditorIcons.Palette, new Vector4(0.42f, 0.72f, 1.00f, 1.0f)),
            ".obj" or ".fbx" or ".gltf" or ".glb" or ".dae" or ".ply" or ".stl"
                => (EditorIcons.Cube, new Vector4(0.98f, 0.62f, 0.26f, 1.0f)),
            _ when IsImagePath(path) => (EditorIcons.Image, new Vector4(0.30f, 0.80f, 0.55f, 1.0f)),
            _ => (EditorIcons.File, Palette.TextDisabled),
        };
    }

    // The folder holding an asset, relative to the project's asset root, for a picker subtitle. Null at root.
    private static string? RelativeFolder(string? assetRoot, string fullPath)
    {
        string? folder = System.IO.Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(folder))
            return null;
        if (!string.IsNullOrEmpty(assetRoot))
        {
            string rel = System.IO.Path.GetRelativePath(assetRoot, folder);
            if (rel == ".") return null;
            return rel.Replace('\\', '/');
        }
        return System.IO.Path.GetFileName(folder);
    }

    // Draws an icon-font glyph centered on a point.
    private static void DrawGlyphCentered(ImDrawListPtr dl, ImFontPtr font, float pixelSize, Vector2 center, string glyph, Vector4 color)
    {
        Vector2 ts = font.CalcTextSizeA(pixelSize, float.MaxValue, 0.0f, glyph);
        dl.AddText(font, pixelSize, center - ts * 0.5f, ImGui.GetColorU32(color), glyph);
    }

    private static System.Collections.Generic.List<string> EnumerateProjectAssets(string[] patterns)
    {
        var result = new System.Collections.Generic.List<string>();
        string? dir = Project.Active?.GetAssetDirectory();
        if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
        {
            try
            {
                foreach (string pattern in patterns)
                {
                    result.AddRange(System.IO.Directory.EnumerateFiles(dir, pattern, System.IO.SearchOption.AllDirectories));
                }
            }
            catch
            {
                // Enumeration failures (permissions, race with deletion) just yield no assets.
            }
        }
        return result;
    }
}

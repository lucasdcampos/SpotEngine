using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.Engine;
using Spot.Engine.Assets;
using Spot.Engine.Scenes;
using Spot.Engine.Graphics;

namespace Spot.DebugUI.UI;

/// <summary>
/// Reusable ImGui widgets for the editor's inspector-style panels. Centralizing these here keeps
/// panels declarative (one call per property) and gives the whole editor a consistent look: labels
/// live in a left column, editors fill the right column, and vector fields get color-coded X/Y/Z
/// badges. Every helper returns whether the value changed so callers can persist edits.
/// </summary>
public static class EditorGui
{
    /// <summary>
    /// Width, in pixels, of a property's label column for a row <paramref name="rowWidth"/> wide: about a third of
    /// the row, clamped so a narrow panel keeps room for values and a wide one doesn't strand labels far from
    /// their fields. Every row of a panel gets the same width, so the value column lines up down the panel.
    /// </summary>
    public static float LabelColumnWidth(float rowWidth) => Math.Clamp(MathF.Round(rowWidth * 0.36f), 88.0f, 160.0f);

    private static EditorPalette Palette => EditorThemeManager.Current.Palette;

    // ----- Property rows ---------------------------------------------------------------------------

    /// <summary>
    /// A three-axis field (position/rotation/scale, or any <see cref="Vector3"/>). Each number box carries a
    /// colored cap — red X, green Y, blue Z, matching the viewport gizmo — that resets that component to
    /// <paramref name="resetValue"/> when clicked.
    /// </summary>
    public static bool Vector3Control(string label, ref Vector3 value, float resetValue = 0.0f, float speed = 0.1f)
    {
        ImGui.PushID(label);
        BeginLabel(label);

        bool changed = false;
        var p = Palette;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(AxisSpacing, 0.0f));
        float badge = BadgeWidth();
        float dragWidth = AxisDragWidth(3, badge);

        changed |= Axis("X", p.AxisX, badge, dragWidth, ref value.X, resetValue, speed);
        ImGui.SameLine();
        changed |= Axis("Y", p.AxisY, badge, dragWidth, ref value.Y, resetValue, speed);
        ImGui.SameLine();
        changed |= Axis("Z", p.AxisZ, badge, dragWidth, ref value.Z, resetValue, speed);

        ImGui.PopStyleVar();
        EndLabel();
        return changed;
    }

    /// <summary>A two-axis field for any <see cref="Vector2"/>, with color-coded X/Y caps.</summary>
    public static bool Vector2Control(string label, ref Vector2 value, float resetValue = 0.0f, float speed = 0.1f)
    {
        ImGui.PushID(label);
        BeginLabel(label);

        bool changed = false;
        var p = Palette;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(AxisSpacing, 0.0f));
        float badge = BadgeWidth();
        float dragWidth = AxisDragWidth(2, badge);

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
    /// entity has no component of <paramref name="type"/>. <paramref name="id"/> distinguishes several cards of
    /// one type (it defaults to the type's name), and <paramref name="onRemove"/> replaces the default
    /// "remove the component of <paramref name="type"/>" action.
    /// </summary>
    public static void Component(Entity entity, Type type, string title, bool removable, Action drawContents,
                                 bool defaultOpen = true, string? id = null, Action? onRemove = null)
    {
        if (!entity.HasComponent(type)) return;

        var p = Palette;
        ImGui.PushID(id ?? type.Name);

        // Each component is its own card: a rounded, hairline-bordered surface that auto-sizes to its
        // content, with a little air between cards so sections read as distinct groups rather than one
        // continuous list. A child window is used deliberately — the property rows below use
        // ImGui.Columns, which owns the window's draw-list splitter, so wrapping them in our own split
        // would corrupt rendering. The child gives each card its own draw list.
        ImGui.Dummy(new Vector2(0.0f, 2.0f));

        Vector4 cardBg = Lighten(p.WindowBg, 0.02f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, cardBg);
        ImGui.PushStyleColor(ImGuiCol.Border, p.Border);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(CardPadding.X, CardHeaderInset));

        ImGui.BeginChild("card", new Vector2(0.0f, 0.0f),
            ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.Border);

        // Header: a title strip a step lighter than the card body, edge to edge, holding an unframed
        // collapsing node and the "⋮" menu. The strip's corners depend on whether the node is open, so the
        // header is drawn on the front channel and the strip filled in behind it afterwards. No Columns run
        // inside this split; the property rows below come after the merge.
        var dl = ImGui.GetWindowDrawList();
        Vector2 cardMin = ImGui.GetWindowPos();
        float cardWidth = ImGui.GetWindowWidth();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        // NoTreePushOnOpen: the properties sit flush with the card padding instead of a tree indent, which
        // would only eat into the value column.
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.AllowOverlap
            | ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (defaultOpen) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, WithAlpha(p.Text, 0.05f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, WithAlpha(p.Text, 0.09f));
        EditorFonts.PushTitle();
        bool opened = ImGui.TreeNodeEx(title, flags);
        EditorFonts.Pop();
        ImGui.PopStyleColor(3);
        float headerBottom = ImGui.GetItemRectMax().Y;

        bool removeRequested = false;
        if (removable)
        {
            float size = ImGui.GetFrameHeight();
            ImGui.SameLine(ImGui.GetWindowWidth() - size - ImGui.GetStyle().WindowPadding.X + 4.0f);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, WithAlpha(p.Text, 0.10f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, WithAlpha(p.Text, 0.16f));
            ImGui.PushStyleColor(ImGuiCol.Text, WithAlpha(p.Text, 0.7f));
            if (ImGui.Button(EditorIcons.EllipsisV, new Vector2(size, size)))
                ImGui.OpenPopup("ComponentSettings");
            ImGui.PopStyleColor(4);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
                ImGui.SetTooltip("Component options");
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeRequested = true;
                ImGui.EndPopup();
            }
        }

        dl.ChannelsSetCurrent(0);
        float stripBottom = headerBottom + CardHeaderInset;
        var stripMin = cardMin + new Vector2(1.0f, 1.0f);   // inside the card's 1px border
        var stripMax = new Vector2(cardMin.X + cardWidth - 1.0f, stripBottom);
        dl.PushClipRect(cardMin, new Vector2(cardMin.X + cardWidth, stripBottom + 1.0f), false);
        dl.AddRectFilled(stripMin, stripMax, ImGui.GetColorU32(Lighten(cardBg, 0.025f)),
            MathF.Max(ImGui.GetStyle().ChildRounding - 1.0f, 0.0f),
            opened ? ImDrawFlags.RoundCornersTop : ImDrawFlags.RoundCornersAll);
        if (opened)
            dl.AddLine(new Vector2(stripMin.X, stripBottom), new Vector2(stripMax.X, stripBottom),
                ImGui.GetColorU32(WithAlpha(p.Border, 0.6f)), 1.0f);
        dl.PopClipRect();
        dl.ChannelsMerge();

        if (opened)
        {
            // The properties start a little below the strip, inset by the card padding.
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, stripBottom + CardPadding.Y));
            drawContents();
            // Matches the gap above the properties: the last row's item spacing plus the window padding.
            ImGui.Dummy(new Vector2(0.0f, MathF.Max(CardPadding.Y - CardHeaderInset - ImGui.GetStyle().ItemSpacing.Y, 0.0f)));
        }

        ImGui.EndChild();

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);

        if (removeRequested)
        {
            if (onRemove is not null)
                onRemove();
            else
                entity.RemoveComponent(type);
        }

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
    public enum EntityIcon { Empty, Mesh, Camera, Light, Sprite, Skybox, Particles }

    /// <summary>Picks the icon that best represents what an entity is.</summary>
    public static EntityIcon IconFor(Entity entity)
    {
        if (entity.HasComponent<CameraComponent>()) return EntityIcon.Camera;
        if (entity.HasComponent<LightComponent>()) return EntityIcon.Light;
        if (entity.HasComponent<ParticleSystemComponent>()) return EntityIcon.Particles;
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
        EntityIcon.Particles => EditorIcons.Fire,
        _ => EditorIcons.Circle,
    };

    /// <summary>
    /// A quiet per-kind tint for an entity's glyph in lists: each hue is blended halfway into the theme's text
    /// color, so icons tell kinds apart at a glance without competing with the names beside them, on dark and
    /// light themes alike.
    /// </summary>
    public static Vector4 EntityGlyphColor(Entity entity)
    {
        var p = Palette;
        Vector4 hue = IconFor(entity) switch
        {
            EntityIcon.Mesh => new Vector4(0.48f, 0.68f, 0.96f, 1.0f),
            EntityIcon.Camera => new Vector4(0.62f, 0.86f, 0.80f, 1.0f),
            EntityIcon.Light => new Vector4(0.98f, 0.80f, 0.38f, 1.0f),
            EntityIcon.Sprite => new Vector4(0.56f, 0.84f, 0.56f, 1.0f),
            EntityIcon.Skybox => new Vector4(0.62f, 0.80f, 1.00f, 1.0f),
            EntityIcon.Particles => new Vector4(0.96f, 0.58f, 0.40f, 1.0f),
            _ => p.TextDisabled,
        };
        return Vector4.Lerp(p.Text, hue, 0.6f);
    }

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
            EntityIcon.Particles => new Vector4(1.0f, 0.6f, 0.2f, 1.0f),
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
            case EntityIcon.Particles:
            {
                // A stylized asterisk/sparkle: three crossed lines.
                for (int i = 0; i < 3; i++)
                {
                    float a = (MathF.PI / 3.0f) * i;
                    Vector2 d = new(MathF.Cos(a), MathF.Sin(a));
                    dl.AddLine(center - d * radius * 0.75f, center + d * radius * 0.75f, col, 1.8f);
                }
                break;
            }
            default:
                dl.AddCircle(center, radius * 0.7f, col, 0, 1.5f);
                break;
        }
    }

    // ----- Internals -------------------------------------------------------------------------------

    // ----- Floating toolbars and overlays ---------------------------------------------------------

    /// <summary>Padding between a floating toolbar cluster's edge and its buttons.</summary>
    public const float ClusterInset = 3.0f;

    // Where the open cluster started; clusters don't nest, so one slot is enough.
    private static Vector2 s_clusterMin;

    /// <summary>
    /// Paints the light, translucent rounded surface used by everything that floats over a viewport —
    /// toolbars, readouts, hints, drop labels — so overlays read as chrome laid over the scene rather than
    /// opaque black bars cut out of it.
    /// </summary>
    public static void OverlayPanel(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding = 5.0f)
    {
        var p = Palette;
        dl.AddRectFilled(min, max, ImGui.GetColorU32(WithAlpha(p.HeaderBg, 0.80f)), rounding);
        dl.AddRect(min, max, ImGui.GetColorU32(WithAlpha(p.Text, 0.08f)), rounding, ImDrawFlags.None, 1.0f);
    }

    /// <summary>
    /// Draws <paramref name="text"/> with a soft one-pixel shadow, for readouts painted straight onto a
    /// viewport where the scene behind them can be any brightness.
    /// </summary>
    public static void OverlayText(ImDrawListPtr dl, Vector2 pos, Vector4 color, string text)
    {
        dl.AddText(pos + new Vector2(1.0f, 1.0f), ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.55f * color.W)), text);
        dl.AddText(pos, ImGui.GetColorU32(color), text);
    }

    /// <summary>
    /// Starts a floating toolbar cluster whose top-left corner is <paramref name="min"/>: submit compact
    /// buttons (<see cref="ToolbarButton"/>) on one line, then call <see cref="EndToolbarCluster"/>, which
    /// paints the cluster's <see cref="OverlayPanel"/> behind them at their measured size. Uses a draw-list
    /// channel split, so don't open one inside ImGui columns or another split.
    /// </summary>
    public static void BeginToolbarCluster(Vector2 min)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);
        s_clusterMin = min;
        ImGui.SetCursorScreenPos(min + new Vector2(ClusterInset, ClusterInset));
        ImGui.BeginGroup();
    }

    /// <summary>Closes the cluster opened by <see cref="BeginToolbarCluster"/>; returns its bottom-right corner.</summary>
    public static Vector2 EndToolbarCluster()
    {
        ImGui.EndGroup();
        Vector2 max = ImGui.GetItemRectMax() + new Vector2(ClusterInset, ClusterInset);
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSetCurrent(0);
        OverlayPanel(dl, s_clusterMin, max);
        dl.ChannelsMerge();
        return max;
    }

    /// <summary>
    /// A thin vertical rule between groups of buttons inside a toolbar cluster; continues the line with a
    /// matching gap on both sides.
    /// </summary>
    public static void ToolbarDivider()
    {
        const float Gap = 9.0f;
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        float x = MathF.Round(max.X + Gap * 0.5f);
        ImGui.GetWindowDrawList().AddLine(new Vector2(x, min.Y + 4.0f), new Vector2(x, max.Y - 4.0f),
            ImGui.GetColorU32(WithAlpha(Palette.Text, 0.14f)), 1.0f);
        ImGui.SameLine(0.0f, Gap);
    }

    /// <summary>
    /// A compact toolbar button with three clear states: transparent at rest, a neutral lift under the
    /// cursor (deeper while pressed), and an accent fill while <paramref name="active"/> — the selected tool
    /// or an enabled toggle. Shows <paramref name="tooltip"/> after a short hover. Returns true when clicked.
    /// </summary>
    public static bool ToolbarButton(string label, bool active, string? tooltip, Vector2 size)
    {
        var p = Palette;
        ImGui.PushStyleColor(ImGuiCol.Button, active ? WithAlpha(p.Accent, 0.90f) : new Vector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, active ? p.AccentHovered : WithAlpha(p.Text, 0.11f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, active ? p.AccentActive : WithAlpha(p.Text, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.Text, active ? new Vector4(1.0f, 1.0f, 1.0f, 1.0f) : WithAlpha(p.Text, 0.86f));
        bool clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        if (tooltip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip(tooltip);
        return clicked;
    }

    /// <summary>
    /// Call right after <c>ImGui.Begin</c> of a dockable panel. While the panel is docked and has focus, a
    /// thin accent line runs along the top of its tab, which is how the editor shows where keyboard input goes
    /// (ImGui 1.90 has no tab overline of its own). It relies on Begin leaving a docked window's tab as the
    /// last item; the line is drawn on the panel's own draw list, which renders above its dock node's tabs.
    /// </summary>
    public static void MarkFocusedTab()
    {
        if (!ImGui.IsWindowDocked() || !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            return;

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        if (max.X - min.X < 4.0f || max.Y - min.Y < 4.0f)
            return;

        var dl = ImGui.GetWindowDrawList();
        dl.PushClipRect(min, max, false);
        dl.AddRectFilled(new Vector2(min.X + 1.0f, min.Y), new Vector2(max.X - 1.0f, min.Y + 2.0f),
            ImGui.GetColorU32(Palette.Accent), 1.0f, ImDrawFlags.RoundCornersTop);
        dl.PopClipRect();
    }

    // ----- Internals -------------------------------------------------------------------------------

    private const float AxisSpacing = 4.0f;

    // Inner padding of a component card, and the smaller inset of its header row inside the title strip.
    private static readonly Vector2 CardPadding = new(10.0f, 8.0f);
    private const float CardHeaderInset = 4.0f;

    // Opens a two-column row: label on the left, the following widget filling the right column. The
    // caller is responsible for the item width (scalar helpers request the full column via
    // ImGui.SetNextItemWidth(-1); vector controls size their fields explicitly).
    private static void BeginLabel(string label)
    {
        float labelWidth = LabelColumnWidth(ImGui.GetContentRegionAvail().X);
        ImGui.Columns(2, "row", false);
        ImGui.SetColumnWidth(0, labelWidth);
        ImGui.AlignTextToFramePadding();

        // Labels sit one notch below the value text so the eye lands on the editable field first. One that
        // doesn't fit its column is cut short with an ellipsis and shown whole on hover, rather than running
        // under the field.
        float room = ImGui.GetContentRegionAvail().X - 4.0f;
        string shown = FitText(label, room);
        ImGui.PushStyleColor(ImGuiCol.Text, WithAlpha(Palette.Text, 0.74f));
        ImGui.TextUnformatted(shown);
        ImGui.PopStyleColor();
        if (!ReferenceEquals(shown, label) && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip(label);
        ImGui.NextColumn();
    }

    private static void EndLabel()
    {
        ImGui.Columns(1);
        ImGui.PopID();
    }

    // The text itself when it fits in `width` pixels, otherwise its longest prefix that fits with "..."
    // appended. Returns the same instance when nothing was cut.
    private static string FitText(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
            return text;

        const string Ellipsis = "...";
        float budget = width - ImGui.CalcTextSize(Ellipsis).X;
        int length = text.Length;
        while (length > 1 && ImGui.CalcTextSize(text.Substring(0, length)).X > budget)
            length--;
        return text.Substring(0, length).TrimEnd() + Ellipsis;
    }

    // One axis of a vector field: a colored cap fused to the left of its number box. Clicking the cap resets
    // the component. The cap is drawn after the field and overlaps it by the frame rounding, hiding the
    // field's rounded left corners so the pair reads as a single control.
    private static bool Axis(string name, Vector4 color, float badgeWidth, float dragWidth, ref float value, float resetValue, float speed)
    {
        bool changed = false;
        float h = ImGui.GetFrameHeight();
        float rounding = ImGui.GetStyle().FrameRounding;
        Vector2 capMin = ImGui.GetCursorScreenPos();

        if (ImGui.InvisibleButton(name, new Vector2(badgeWidth, h)))
        {
            value = resetValue;
            changed = true;
        }
        bool capHovered = ImGui.IsItemHovered();
        bool capHeld = ImGui.IsItemActive();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.DelayShort))
            ImGui.SetTooltip($"Reset {name} to {resetValue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}");

        ImGui.SameLine(0.0f, 0.0f);
        ImGui.SetNextItemWidth(dragWidth);
        if (ImGui.DragFloat("##" + name, ref value, speed, 0.0f, 0.0f, "%.2f"))
            changed = true;

        Vector4 fill = capHeld ? Darken(color, 0.06f) : capHovered ? Lighten(color, 0.08f) : color;
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(capMin, capMin + new Vector2(badgeWidth + rounding, h), ImGui.GetColorU32(fill),
            rounding, ImDrawFlags.RoundCornersLeft);
        Vector2 textSize = ImGui.CalcTextSize(name);
        dl.AddText(capMin + new Vector2((badgeWidth + rounding - textSize.X) * 0.5f, (h - textSize.Y) * 0.5f),
            ImGui.GetColorU32(ReadableOn(fill)), name);
        return changed;
    }

    // Width of each number box so that N caps + N boxes + the gaps between axes fill the column.
    private static float AxisDragWidth(int axes, float badgeWidth)
    {
        float avail = ImGui.GetContentRegionAvail().X;
        float spacings = (axes - 1) * AxisSpacing;
        float width = (avail - axes * badgeWidth - spacings) / axes;
        return MathF.Max(width, 1.0f);
    }

    // A narrow cap: room for one capital letter.
    private static float BadgeWidth() => MathF.Round(ImGui.GetFontSize() * 1.05f);

    // Near-black or white, whichever reads better on `background` (by relative luminance).
    private static Vector4 ReadableOn(Vector4 background)
    {
        static float Linear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        float luminance = 0.2126f * Linear(background.X) + 0.7152f * Linear(background.Y) + 0.0722f * Linear(background.Z);
        return luminance > 0.18f ? new Vector4(0.06f, 0.06f, 0.06f, 0.88f) : new Vector4(1.0f, 1.0f, 1.0f, 0.95f);
    }

    private static Vector4 Lighten(Vector4 c, float amount) => new(
        Math.Clamp(c.X + amount, 0.0f, 1.0f),
        Math.Clamp(c.Y + amount, 0.0f, 1.0f),
        Math.Clamp(c.Z + amount, 0.0f, 1.0f),
        c.W);

    private static Vector4 Darken(Vector4 c, float amount) => Lighten(c, -amount);

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
        AssetSlotCustomItems? drawCustomItems = null,
        BuiltinAssetKind? builtins = null)
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

        DrawAssetPicker(popupName, searchPatterns, currentPath, ref outPath, ref changed, drawCustomItems, builtins);

        // EndLabel pops the ID pushed above. Never pop again here: with ImGui's asserts compiled out, an extra
        // PopID drives the window's ID stack negative, the next PushID writes in front of its heap buffer, and
        // the process dies with STATUS_HEAP_CORRUPTION once ImGui frees it (~60 s after the window goes idle).
        EndLabel();

        return changed;
    }

    /// <summary>
    /// A slot for an <see cref="Entity"/> reference (an entity-typed script field). Shows the referenced
    /// entity's name (or a dim "None"), accepts an entity dragged from the hierarchy, and offers a trailing ✕
    /// to clear it. Returns true (with <paramref name="value"/> updated) on any change, including clearing.
    /// </summary>
    /// <param name="label">The field label shown in the left column.</param>
    /// <param name="scene">The scene the dragged entity id is resolved against.</param>
    /// <param name="value">The current reference; updated in place when the user sets or clears it.</param>
    public static bool EntityField(string label, Scene scene, ref Entity value)
    {
        bool changed = false;

        ImGui.PushID(label);
        BeginLabel(label);

        bool hasValue = value.IsValid;
        float clearWidth = hasValue ? ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X : 0.0f;
        float buttonWidth = ImGui.GetContentRegionAvail().X - clearWidth;

        var p = Palette;
        float h = ImGui.GetFrameHeight();
        Vector2 btnMin = ImGui.GetCursorScreenPos();
        string name = hasValue ? value.Name : "None";

        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.0f, 0.5f));
        if (!hasValue)
            ImGui.PushStyleColor(ImGuiCol.Text, p.TextDisabled);
        ImGui.Button("  " + IconPadding(h) + name, new Vector2(buttonWidth, 0.0f));
        if (!hasValue)
            ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        // Accept an entity dragged from the hierarchy (the "ENTITY" payload carries the entity's int id).
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("ENTITY");
                if (payload.NativePtr != null)
                {
                    int id = *(int*)payload.Data;
                    value = new Entity(id, scene);
                    changed = true;
                }
            }

            ImGui.EndDragDropTarget();
        }

        if (hasValue)
        {
            var dl = ImGui.GetWindowDrawList();
            Vector2 iconCenter = btnMin + new Vector2(4.0f + h * 0.5f, h * 0.5f);
            DrawGlyphCentered(dl, EditorFonts.Icons, h * 0.62f, iconCenter, EditorIcons.Cube, p.Text);

            ImGui.SameLine();
            if (ImGui.Button(EditorIcons.Times, new Vector2(h, h)))
            {
                value = default;
                changed = true;
            }

            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear");
        }

        EndLabel();
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
            nint thumb = ResolveThumbnail(path!);
            if (thumb != 0)
            {
                float s = h - 6.0f;
                Vector2 tl = iconCenter - new Vector2(s * 0.5f, s * 0.5f);
                dl.AddImageRounded(thumb, tl, tl + new Vector2(s, s),
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
        AssetSlotCustomItems? drawCustomItems,
        BuiltinAssetKind? builtins)
    {
        if (!ImGui.BeginPopup(popupName))
            return;

        const float width = 340.0f;
        ImGui.SetNextItemWidth(width);
        if (_assetPickerJustOpened)
        {
            ImGui.SetKeyboardFocusHere();
            _assetPickerJustOpened = false;
        }
        ImGui.InputTextWithHint("##Search", $"{EditorIcons.Search}  Search assets...", ref _assetSearchFilter, 128);
        ImGui.Separator();

        ImGui.BeginChild("AssetList", new Vector2(width, 360), ImGuiChildFlags.None);

        if (PickerRow(EditorIcons.Times, Palette.TextDisabled, "None", null, string.IsNullOrEmpty(currentPath)))
        {
            outPath = null;
            changed = true;
            ImGui.CloseCurrentPopup();
        }

        drawCustomItems?.Invoke(ref outPath, ref changed);
        if (changed) ImGui.CloseCurrentPopup();

        // The engine's built-in assets of the slot's kind, ahead of the project's files.
        if (builtins is { } kind && !changed)
        {
            bool listed = false;
            bool hasCurrent = BuiltinAssets.TryGet(currentPath, out BuiltinAsset current);
            foreach (BuiltinAsset asset in BuiltinAssets.OfKind(kind))
            {
                if (!string.IsNullOrEmpty(_assetSearchFilter) &&
                    !asset.Name.Contains(_assetSearchFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!listed)
                {
                    ImGui.Separator();
                    listed = true;
                }

                bool isSelected = hasCurrent && ReferenceEquals(current, asset);
                (string glyph, Vector4 color) = AssetGlyph(asset.Reference);
                if (PickerRow(glyph, color, asset.Name, "Built-in", isSelected, ResolveThumbnail(asset.Reference)))
                {
                    // Picking the shape already in the slot keeps its parameters.
                    if (!isSelected)
                    {
                        outPath = asset.Reference;
                        changed = true;
                    }

                    ImGui.CloseCurrentPopup();
                }

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(asset.Description);
            }
        }

        var assets = EnumerateProjectAssets(searchPatterns);
        if (assets.Count > 0)
            ImGui.Separator();

        string? assetRoot = Spot.Engine.Assets.AssetPath.Root;
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

            if (PickerRow(glyph, color, filename, subtitle, isSelected, ResolveThumbnail(path)))
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
    /// One row of the asset picker: a thumbnail tile (an image preview, a rendered material sphere, or a
    /// kind glyph), the asset name, and a dim folder subtitle. Returns true when clicked. When
    /// <paramref name="thumbTex"/> is non-zero it is drawn as the preview; otherwise the glyph is used.
    /// </summary>
    private static bool PickerRow(string glyph, Vector4 glyphColor, string title, string? subtitle, bool selected, nint thumbTex = 0)
    {
        float rowH = MathF.Max(ImGui.GetTextLineHeight() * 2.0f + 8.0f, 40.0f);
        Vector2 min = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.Selectable($"##{title}{subtitle}", selected, ImGuiSelectableFlags.None, new Vector2(0.0f, rowH));

        var dl = ImGui.GetWindowDrawList();
        float pad = 7.0f;
        float thumb = rowH - pad * 2.0f;
        Vector2 tl = min + new Vector2(pad, pad);
        Vector2 br = tl + new Vector2(thumb, thumb);
        Vector2 iconCenter = (tl + br) * 0.5f;

        // A consistent rounded tile behind every row so glyphs and previews line up in the same footprint.
        dl.AddRectFilled(tl, br, ImGui.GetColorU32(Palette.FrameBg), 4.0f);
        if (thumbTex != 0)
        {
            // Framebuffer- and file-backed textures are both bottom-up, so flip V for an upright preview.
            dl.AddImageRounded(thumbTex, tl, br, new Vector2(0, 1), new Vector2(1, 0), 0xFFFFFFFF, 4.0f);
        }
        else
        {
            DrawGlyphCentered(dl, EditorFonts.Icons, thumb * 0.62f, iconCenter, glyph, glyphColor);
        }
        dl.AddRect(tl, br, ImGui.GetColorU32(WithAlpha(Palette.Border, 0.6f)), 4.0f);

        float textX = br.X + pad;
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

    // The preview texture handle for a picker row / asset slot: a cached image thumbnail, a rendered
    // material sphere, or 0 to fall back to a kind glyph.
    private static nint ResolveThumbnail(string path)
    {
        if (BuiltinAssets.TryGet(path, out BuiltinAsset builtin))
        {
            try
            {
                return builtin.Kind switch
                {
                    BuiltinAssetKind.Texture => (nint)BuiltinAssets.LoadTexture(builtin.Reference).Handle.Id,
                    BuiltinAssetKind.Material => MaterialThumbnails.Get(builtin.Reference),
                    _ => 0,
                };
            }
            catch
            {
                return 0;
            }
        }

        if (IsImagePath(path))
        {
            Texture2D? tex = EditorThumbnails.Get(AssetPath.Resolve(path));
            return tex != null ? (nint)tex.Handle.Id : 0;
        }
        if (path.EndsWith(".sptmat", StringComparison.OrdinalIgnoreCase))
            return MaterialThumbnails.Get(AssetPath.Resolve(path));
        return 0;
    }

    // ----- Scripts ---------------------------------------------------------------------------------

    /// <summary>
    /// Whether a script class name is backed by a <c>.cs</c> file in the project (or a loaded user component
    /// type). Used by the inspector to tell a component waiting for its script to compile from a missing one.
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
            if (assembly.IsCollectible) continue; // the game's script loads: the registry knows the current one
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }
            foreach (Type type in types)
            {
                if (!type.IsAbstract && type.Name == name && type.IsSubclassOf(typeof(Spot.Engine.Component)) && Spot.Engine.Component.IsUserType(type))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the stable guid for a script class, creating its <c>.cs.meta</c> sidecar on first use (the same
    /// guid+.meta identity the asset pipeline uses) so the reference survives a class rename. Returns an empty
    /// string when the backing <c>.cs</c> file can't be located, in which case the script resolves by name.
    /// </summary>
    public static string GetOrCreateScriptGuid(string className)
    {
        string name = className.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? className[..^3] : className;

        foreach (string path in EnumerateProjectAssets(new[] { "*.cs" }))
        {
            if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                string metaPath = Spot.Engine.Assets.AssetMeta.MetaPathFor(path);
                Spot.Engine.Assets.AssetMeta meta = Spot.Engine.Assets.AssetMeta.ReadOrCreate(path, "script");
                if (!System.IO.File.Exists(metaPath))
                {
                    meta.Save(path);
                }

                return meta.Guid;
            }
            catch
            {
                // Never let a sidecar read/write take the editor down; fall back to name-based resolution.
                return string.Empty;
            }
        }

        return string.Empty;
    }

    // ----- Asset helpers ---------------------------------------------------------------------------

    /// <summary>
    /// A readable label for a built-in reference: its catalog name, followed by any mesh parameters that differ
    /// from the shape's defaults, such as <c>Capsule (radius 0.3, height 1.7)</c>.
    /// </summary>
    /// <param name="reference">The built-in reference.</param>
    /// <returns>The label, or the reference itself when it is not a known built-in.</returns>
    public static string BuiltinLabel(string reference)
    {
        if (!BuiltinAssets.TryGet(reference, out BuiltinAsset asset))
            return reference;
        if (asset.Kind != BuiltinAssetKind.Mesh || !BuiltinAssets.TryGetPrimitive(reference, out PrimitiveSpec spec))
            return asset.Name;

        PrimitiveSpec defaults = PrimitiveSpec.For(spec.Shape);
        var changed = PrimitiveSpec.ParametersOf(spec.Shape)
            .Where(p => spec.Get(p) != defaults.Get(p))
            .Select(p => $"{PrimitiveSpec.KeyOf(p)} {spec.Get(p).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}")
            .ToList();
        return changed.Count == 0 ? asset.Name : $"{asset.Name} ({string.Join(", ", changed)})";
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };

    private static bool IsImagePath(string path)
    {
        // Pseudo paths ("primitive:Cube", "editor:Checkerboard") never point at image files.
        if (path.Contains(':') && !System.IO.Path.IsPathRooted(path))
            return false;
        return Array.IndexOf(ImageExtensions, System.IO.Path.GetExtension(path).ToLowerInvariant()) >= 0;
    }

    // The display name for a slot value: a built-in's name (with any mesh parameters), the tail of another pseudo
    // path, or the file name.
    private static string AssetDisplayName(string path)
    {
        if (BuiltinAssets.TryGet(path, out _))
            return BuiltinLabel(path);

        int colon = path.IndexOf(':');
        if (colon > 0 && !System.IO.Path.IsPathRooted(path))
            return path[(colon + 1)..];
        return System.IO.Path.GetFileName(path);
    }

    // The kind glyph + tint for an asset path, matching the asset browser's color coding.
    private static (string Glyph, Vector4 Color) AssetGlyph(string path)
    {
        if (BuiltinAssets.TryGet(path, out BuiltinAsset builtin))
        {
            return builtin.Kind switch
            {
                BuiltinAssetKind.Mesh => (EditorIcons.Cube, new Vector4(0.98f, 0.62f, 0.26f, 1.0f)),
                BuiltinAssetKind.Texture => (EditorIcons.Image, new Vector4(0.30f, 0.80f, 0.55f, 1.0f)),
                _ => (EditorIcons.Palette, new Vector4(0.42f, 0.72f, 1.00f, 1.0f)),
            };
        }

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
        string? dir = Spot.Engine.Assets.AssetPath.Root;
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

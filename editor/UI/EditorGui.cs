using System;
using System.Numerics;
using ImGuiNET;
using Spot.Scenes;

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

        ImGui.PushID(type.Name);

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.AllowOverlap
            | ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.FramePadding;
        if (defaultOpen) flags |= ImGuiTreeNodeFlags.DefaultOpen;

        var p = Palette;
        ImGui.PushStyleColor(ImGuiCol.Header, p.HeaderBg);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Lighten(p.HeaderBg, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Lighten(p.HeaderBg, 0.1f));
        bool opened = ImGui.TreeNodeEx(title, flags);
        ImGui.PopStyleColor(3);

        bool removeRequested = false;
        if (removable)
        {
            float size = ImGui.GetFrameHeight();
            ImGui.SameLine(ImGui.GetWindowWidth() - size - ImGui.GetStyle().FramePadding.X * 2.0f);
            if (ImGui.Button("...", new Vector2(size, size)))
                ImGui.OpenPopup("ComponentSettings");
            if (ImGui.BeginPopup("ComponentSettings"))
            {
                if (ImGui.MenuItem("Remove component"))
                    removeRequested = true;
                ImGui.EndPopup();
            }
        }

        if (opened)
        {
            drawContents();
            ImGui.TreePop();
        }

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
        if (entity.HasComponent<DynamicCloudsComponent>()) return EntityIcon.Skybox;
        if (entity.HasComponent<MeshComponent>()) return EntityIcon.Mesh;
        if (entity.HasComponent<Sprite2DComponent>()) return EntityIcon.Sprite;
        return EntityIcon.Empty;
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
        ImGui.TextUnformatted(label);
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
}

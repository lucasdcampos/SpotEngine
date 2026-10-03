using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Spot.Engine.Physics;
using Spot.Engine.Scenes;

namespace Spot.DebugUI.UI;

/// <summary>One component the "Add Component" menu can offer: its type, label, group, and whether it is the game's own.</summary>
internal sealed record ComponentEntry(Type Type, string DisplayName, string Category, bool IsUser);

/// <summary>
/// How the editor presents component types in lists: each one's label and group (from
/// <see cref="ComponentMenuAttribute"/>, with the game's own components under <see cref="Scripts"/> unless they
/// name a category), the order groups appear in, and an icon and tint per component and per group.
/// </summary>
internal static class ComponentCatalog
{
    /// <summary>The group a game's own components are listed under unless they name another.</summary>
    public const string Scripts = "Scripts";

    // The groups in menu order; any other category follows alphabetically.
    private static readonly string[] s_categoryOrder =
    {
        Scripts, "Rendering", "Environment", "Effects", "Physics", "Physics 2D", "Audio", "Animation", "UI", "Network",
    };

    private static readonly Dictionary<string, (string Glyph, Vector4 Hue)> s_categories = new(StringComparer.Ordinal)
    {
        [Scripts] = (EditorIcons.Code, new Vector4(0.36f, 0.66f, 0.98f, 1.0f)),
        ["Rendering"] = (EditorIcons.Palette, new Vector4(0.48f, 0.68f, 0.96f, 1.0f)),
        ["Environment"] = (EditorIcons.Mountain, new Vector4(0.62f, 0.82f, 1.00f, 1.0f)),
        ["Effects"] = (EditorIcons.Fire, new Vector4(0.96f, 0.58f, 0.40f, 1.0f)),
        ["Physics"] = (EditorIcons.Atom, new Vector4(0.56f, 0.84f, 0.56f, 1.0f)),
        ["Physics 2D"] = (EditorIcons.Atom, new Vector4(0.50f, 0.82f, 0.72f, 1.0f)),
        ["Audio"] = (EditorIcons.Music, new Vector4(0.78f, 0.64f, 0.98f, 1.0f)),
        ["Animation"] = (EditorIcons.Film, new Vector4(0.94f, 0.60f, 0.78f, 1.0f)),
        ["UI"] = (EditorIcons.WindowMaximize, new Vector4(0.98f, 0.80f, 0.38f, 1.0f)),
        ["Network"] = (EditorIcons.NetworkWired, new Vector4(0.42f, 0.82f, 0.82f, 1.0f)),
    };

    private static readonly Dictionary<Type, string> s_glyphs = new()
    {
        [typeof(MeshComponent)] = EditorIcons.Cube,
        [typeof(SkinnedMeshComponent)] = EditorIcons.Cubes,
        [typeof(Sprite2DComponent)] = EditorIcons.Image,
        [typeof(TextComponent)] = EditorIcons.Font,
        [typeof(CameraComponent)] = EditorIcons.Camera,
        [typeof(LightComponent)] = EditorIcons.Lightbulb,
        [typeof(SkyboxComponent)] = EditorIcons.Sun,
        [typeof(DynamicCloudsComponent)] = EditorIcons.Cloud,
        [typeof(PostProcessingComponent)] = EditorIcons.Wand,
        [typeof(ParticleSystemComponent)] = EditorIcons.Fire,
        [typeof(PhysicsBody3DComponent)] = EditorIcons.WeightHanging,
        [typeof(PhysicsBody2DComponent)] = EditorIcons.WeightHanging,
        [typeof(CharacterController3DComponent)] = EditorIcons.PersonRunning,
        [typeof(BoxCollider3DComponent)] = EditorIcons.VectorSquare,
        [typeof(SphereCollider3DComponent)] = EditorIcons.Circle,
        [typeof(CapsuleCollider3DComponent)] = EditorIcons.Capsules,
        [typeof(BoxCollider2DComponent)] = EditorIcons.Square,
        [typeof(CircleCollider2DComponent)] = EditorIcons.Circle,
        [typeof(AudioSourceComponent)] = EditorIcons.VolumeHigh,
        [typeof(AudioListenerComponent)] = EditorIcons.Headphones,
        [typeof(AnimatorComponent)] = EditorIcons.Film,
        [typeof(UICanvasComponent)] = EditorIcons.WindowMaximize,
    };

    /// <summary>Describes a component type: its attribute's label and category, or its spaced class name under <see cref="Scripts"/>.</summary>
    public static ComponentEntry Describe(Type type)
    {
        var menu = type.GetCustomAttribute<ComponentMenuAttribute>();
        bool isUser = Component.IsUserType(type);
        string category = menu?.Category is { Length: > 0 } c ? c : isUser ? Scripts : "Other";
        return new ComponentEntry(type, menu?.DisplayName ?? ComponentInspector.Humanize(type.Name), category, isUser);
    }

    /// <summary>Orders categories for display: the known groups first, in menu order, then the rest alphabetically.</summary>
    public static int CompareCategories(string a, string b)
    {
        int ra = Rank(a), rb = Rank(b);
        return ra != rb ? ra.CompareTo(rb) : string.CompareOrdinal(a, b);
    }

    /// <summary>The icon of a component: its own when the editor knows one, otherwise its category's.</summary>
    public static string Glyph(ComponentEntry entry) =>
        s_glyphs.TryGetValue(entry.Type, out string? glyph) ? glyph : CategoryGlyph(entry.Category);

    /// <summary>The icon of a category.</summary>
    public static string CategoryGlyph(string category) =>
        s_categories.TryGetValue(category, out var info) ? info.Glyph : EditorIcons.Folder;

    /// <summary>
    /// A category's quiet tint: its hue blended toward the theme's text color, so icons tell groups apart at a
    /// glance without shouting over the names beside them.
    /// </summary>
    public static Vector4 CategoryColor(string category)
    {
        Vector4 text = EditorThemeManager.Current.Palette.Text;
        Vector4 hue = s_categories.TryGetValue(category, out var info) ? info.Hue : text;
        return Vector4.Lerp(hue, text, 0.3f) with { W = 1.0f };
    }

    private static int Rank(string category)
    {
        int index = Array.IndexOf(s_categoryOrder, category);
        return index >= 0 ? index : s_categoryOrder.Length;
    }
}

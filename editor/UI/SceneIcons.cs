using System.Numerics;
using ImGuiNET;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Editor.UI;

/// <summary>
/// Draws billboard icons in the viewport for entities that would otherwise be invisible — cameras,
/// directional lights and the sky/clouds entity — so they can be seen and picked in the editor.
/// Each icon is placed at the entity's world position, projected through the camera's view-projection
/// (the same math the transform gizmo uses) and drawn with the ImGui window draw list over the
/// rendered viewport image.
/// </summary>
public sealed class SceneIcons
{
    private const float GlyphRadiusPx = 13.0f;
    private const float DiscRadiusPx = 18.0f;

    /// <summary>
    /// Draws the icons and returns the entity whose icon is under the cursor (nearest one wins), so the
    /// viewport can select it on click. Returns <see langword="null"/> when no icon is hovered.
    /// </summary>
    public Entity? Draw(Scene scene, EditorCamera camera, Vector2 viewportPos, Vector2 viewportSize, bool viewportHovered)
    {
        Matrix4x4 vp = camera.ViewProjection;
        var drawList = ImGui.GetWindowDrawList();
        var palette = EditorThemeManager.Current.Palette;
        Vector2 mouse = ImGui.GetIO().MousePos;

        Entity? hovered = null;
        float hoveredDist = DiscRadiusPx;

        foreach (var entity in scene.View<Transform>())
        {
            if (!TryPickIcon(entity, out EditorGui.EntityIcon kind))
            {
                continue;
            }

            Vector3 world = entity.GetComponent<Transform>().WorldPosition;
            if (!WorldToScreen(vp, world, viewportPos, viewportSize, out Vector2 screen))
            {
                continue; // behind the camera
            }

            float dist = Vector2.Distance(mouse, screen);
            bool isHovered = viewportHovered && dist < DiscRadiusPx;
            if (isHovered && dist < hoveredDist)
            {
                hoveredDist = dist;
                hovered = entity;
            }

            // A dark disc keeps the glyph legible against any scene background.
            uint disc = ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, isHovered ? 0.7f : 0.5f));
            drawList.AddCircleFilled(screen, DiscRadiusPx, disc, 20);
            if (isHovered)
            {
                drawList.AddCircle(screen, DiscRadiusPx, ImGui.GetColorU32(palette.GizmoHover), 20, 2.0f);
            }

            EditorGui.DrawEntityIcon(drawList, kind, screen, GlyphRadiusPx);
        }

        if (hovered != null)
        {
            ImGui.SetTooltip(hovered.Value.Name);
        }

        return hovered;
    }

    // Maps an entity to an icon only if it is one of the otherwise-invisible types we mark.
    private static bool TryPickIcon(Entity entity, out EditorGui.EntityIcon kind)
    {
        if (entity.HasComponent<CameraComponent>()) { kind = EditorGui.EntityIcon.Camera; return true; }
        if (entity.HasComponent<DirectionalLightComponent>()) { kind = EditorGui.EntityIcon.Light; return true; }
        if (entity.HasComponent<DynamicCloudsComponent>()) { kind = EditorGui.EntityIcon.Skybox; return true; }
        kind = EditorGui.EntityIcon.Empty;
        return false;
    }

    private static bool WorldToScreen(Matrix4x4 vp, Vector3 world, Vector2 viewportPos, Vector2 viewportSize, out Vector2 screen)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1.0f), vp);
        if (clip.W <= 1e-5f)
        {
            screen = default;
            return false;
        }
        Vector3 ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        screen = new Vector2(
            viewportPos.X + (ndc.X * 0.5f + 0.5f) * viewportSize.X,
            viewportPos.Y + (1.0f - (ndc.Y * 0.5f + 0.5f)) * viewportSize.Y);
        return true;
    }
}

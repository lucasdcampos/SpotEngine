using System.Numerics;
using Spot.Core;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// Draws every entity's <see cref="TextComponent"/> as world-space quads through the shared glyph atlas,
/// billboarded to the camera by default. Reuses the particle renderer's blended, depth-tested,
/// depth-write-off path so text blends correctly and is occluded by solid geometry.
/// </summary>
public static class TextRenderSystem
{
    // Reused across frames so the per-frame world-text path allocates nothing.
    private static readonly List<PositionedGlyph> s_glyphs = new();

    /// <summary>Draws all world-space <see cref="TextComponent"/> entities in the scene.</summary>
    /// <param name="scene">The scene whose text components are drawn.</param>
    /// <param name="viewProjection">The view-projection matrix to render with.</param>
    public static void Render(Scene scene, Matrix4x4 viewProjection)
    {
        bool hasText = false;
        foreach (Entity entity in scene.View<TransformComponent, TextComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var text = entity.GetComponent<TextComponent>();
            if (text.Enabled && !string.IsNullOrEmpty(text.Text)) { hasText = true; break; }
        }

        if (!hasText) return;

        // Screen-aligned world axes for billboards, derived from the inverse view-projection.
        Vector3 camRight = Vector3.UnitX;
        Vector3 camUp = Vector3.UnitY;
        if (Matrix4x4.Invert(viewProjection, out Matrix4x4 invVP))
        {
            Vector3 c = ClipToWorld(invVP, 0f, 0f);
            camRight = SpotMath.SafeNormalize(ClipToWorld(invVP, 1f, 0f) - c, Vector3.UnitX);
            camUp = SpotMath.SafeNormalize(ClipToWorld(invVP, 0f, 1f) - c, Vector3.UnitY);
        }

        ParticleRenderer.BeginScene(viewProjection);

        foreach (Entity entity in scene.View<TransformComponent, TextComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var transform = entity.GetComponent<TransformComponent>();
            var text = entity.GetComponent<TextComponent>();
            if (!transform.Enabled || !text.Enabled || string.IsNullOrEmpty(text.Text)) continue;

            Font font = ResolveFont(text);

            Vector3 right, up;
            if (text.Billboard)
            {
                right = camRight;
                up = camUp;
            }
            else
            {
                right = SpotMath.SafeNormalize(Vector3.TransformNormal(Vector3.UnitX, transform.Matrix), Vector3.UnitX);
                up = SpotMath.SafeNormalize(Vector3.TransformNormal(Vector3.UnitY, transform.Matrix), Vector3.UnitY);
            }

            var options = new TextLayoutOptions { MaxWidth = 0f, Align = text.Alignment, LineSpacing = 1f };
            Vector2 block = TextLayout.Build(font, text.Text, text.FontSize, options, s_glyphs);

            float s = text.WorldScale;
            float halfW = block.X * 0.5f;
            float halfH = block.Y * 0.5f;
            Vector3 origin = transform.WorldPosition;

            foreach (PositionedGlyph g in s_glyphs)
            {
                // Glyph center relative to the block center (layout is y-down, so world +up is glyph -y).
                float cx = g.Position.X + g.Size.X * 0.5f - halfW;
                float cy = g.Position.Y + g.Size.Y * 0.5f - halfH;
                Vector3 center = origin + right * (cx * s) - up * (cy * s);
                Vector3 axisX = right * (g.Size.X * 0.5f * s);
                Vector3 axisY = up * (g.Size.Y * 0.5f * s);
                ParticleRenderer.SubmitTextured(center, axisX, axisY, text.Color, font.Atlas, ParticleBlend.Alpha, g.Uv);
            }
        }

        ParticleRenderer.EndScene();
    }

    // Resolves a text component's font, lazily loading from its stored reference and caching it back
    // onto the component. Never throws — bad references log once and fall back to the built-in default.
    private static Font ResolveFont(TextComponent text)
    {
        if (text.Font is not null) return text.Font;

        if (!string.IsNullOrEmpty(text.FontPath))
        {
            try
            {
                text.Font = Font.Load(text.FontPath);
                return text.Font;
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to load font '{0}': {1}", text.FontPath, ex.Message);
                text.FontPath = null;
            }
        }

        return Font.Default;
    }

    private static Vector3 ClipToWorld(Matrix4x4 invViewProjection, float clipX, float clipY)
    {
        Vector4 p = Vector4.Transform(new Vector4(clipX, clipY, 0f, 1f), invViewProjection);
        return p.W != 0f ? new Vector3(p.X, p.Y, p.Z) / p.W : new Vector3(p.X, p.Y, p.Z);
    }
}

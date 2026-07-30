using System.Numerics;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// Draws every entity that has both a <see cref="Transform"/> and a <see cref="Sprite2D"/>.
/// </summary>
/// <remarks>
/// This is the convenient, automatic path — the engine renders your sprites for you. It is entirely
/// optional: you can skip it and drive <see cref="Renderer2D"/> (batched quads), <see cref="Renderer"/>
/// (draw calls), or the raw API via <see cref="Renderer.Api"/> yourself for full control over what and
/// how you render. <see cref="Render"/> opens and closes its own scene, so mix custom rendering in
/// separate <see cref="Renderer2D.BeginScene"/>/<see cref="Renderer2D.EndScene"/> passes.
/// </remarks>
public static class RenderSystem
{
    /// <summary>
    /// Draws all sprite entities in the scene through the given camera.
    /// </summary>
    /// <param name="scene">The scene whose sprites are drawn.</param>
    /// <param name="viewProjection">The view-projection matrix to render with.</param>
    public static void Render(Scene scene, Matrix4x4 viewProjection)
    {
        Renderer2D.BeginScene(viewProjection);

        foreach (Entity entity in scene.View<Transform, Sprite2D>())
        {
            Transform transform = entity.GetComponent<Transform>();
            Sprite2D sprite = entity.GetComponent<Sprite2D>();

            if (sprite.Texture is not null)
            {
                Renderer2D.DrawQuad(transform.Matrix, sprite.Texture, sprite.Color);
            }
            else
            {
                Renderer2D.DrawQuad(transform.Matrix, sprite.Color);
            }
        }

        Renderer2D.EndScene();
    }
}

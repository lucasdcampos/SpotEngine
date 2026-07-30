using System.Numerics;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// Draws every entity's 3D mesh (<see cref="MeshRenderer"/>) and 2D sprite (<see cref="Sprite2D"/>),
/// each together with its <see cref="Transform"/>.
/// </summary>
/// <remarks>
/// This is the convenient, automatic path — the engine renders your meshes and sprites for you. It is
/// entirely optional: you can skip it and drive <see cref="Renderer3D"/> (meshes), <see cref="Renderer2D"/>
/// (batched quads), <see cref="Renderer"/> (draw calls), or the raw API via <see cref="Renderer.Api"/>
/// yourself for full control over what and how you render. <see cref="Render"/> opens and closes its own
/// scenes, so mix custom rendering in separate Begin/End passes.
/// </remarks>
public static class RenderSystem
{
    /// <summary>
    /// Draws all mesh and sprite entities in the scene through the given camera.
    /// </summary>
    /// <param name="scene">The scene whose meshes and sprites are drawn.</param>
    /// <param name="viewProjection">The view-projection matrix to render with.</param>
    public static void Render(Scene scene, Matrix4x4 viewProjection)
    {
        Renderer3D.BeginScene(viewProjection);

        foreach (Entity entity in scene.View<Transform, MeshRenderer>())
        {
            MeshRenderer meshRenderer = entity.GetComponent<MeshRenderer>();
            if (meshRenderer.Model is null)
            {
                continue;
            }

            Matrix4x4 world = entity.GetComponent<Transform>().Matrix;
            foreach (Mesh mesh in meshRenderer.Model.Meshes)
            {
                Renderer3D.DrawMesh(world, mesh, meshRenderer.Color);
            }
        }

        Renderer3D.EndScene();

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

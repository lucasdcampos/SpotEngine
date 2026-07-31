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
        bool hasLight = false;
        Vector3 lightDir = new Vector3(0, -1, 0);
        Vector3 lightColor = Vector3.One;
        float ambientIntensity = 0.3f;

        foreach (Entity entity in scene.View<Transform, DirectionalLightComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var transform = entity.GetComponent<Transform>();
            var light = entity.GetComponent<DirectionalLightComponent>();
            if (!transform.Enabled || !light.Enabled) continue;
            hasLight = true;
            lightColor = light.Color * light.Intensity;
            ambientIntensity = light.AmbientIntensity;
            
            // We want lightDir to point TOWARDS the light source for shader math.
            // If the entity's forward (-Z) is where it shines, then the source is in the opposite direction (+Z).
            lightDir = Vector3.Normalize(Vector3.TransformNormal(new Vector3(0, 0, 1), transform.Matrix));
            break; // only use the first one for now
        }

        Renderer3D.BeginScene(viewProjection, hasLight, lightDir, lightColor, ambientIntensity);
        Renderer3D.DrawSkybox();

        foreach (Entity entity in scene.View<DynamicCloudsComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var clouds = entity.GetComponent<DynamicCloudsComponent>();
            if (!clouds.Enabled) continue;
            
            Renderer3D.DrawDynamicClouds(
                clouds.ColorTop.X, clouds.ColorTop.Y, clouds.ColorTop.Z,
                clouds.ColorBottom.X, clouds.ColorBottom.Y, clouds.ColorBottom.Z,
                clouds.Speed, clouds.Density, clouds.Height, 
                clouds.Opacity, clouds.Volume, Spot.Core.Application.Instance.Time);
            break; // only draw the first one
        }

        foreach (Entity entity in scene.View<Transform, MeshRenderer>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            MeshRenderer meshRenderer = entity.GetComponent<MeshRenderer>();
            var transform = entity.GetComponent<Transform>();
            if (!meshRenderer.Enabled || !transform.Enabled) continue;
            
            if (meshRenderer.Model is null)
            {
                continue;
            }

            Matrix4x4 world = entity.GetComponent<Transform>().Matrix;
            Vector4 color = meshRenderer.Material?.Color ?? meshRenderer.Color;
            Texture2D? texture = meshRenderer.Material?.Texture;
            int shaderType = (int)(meshRenderer.Material?.ShaderType ?? Spot.Assets.MaterialShaderType.Standard);
            foreach (Mesh mesh in meshRenderer.Model.Meshes)
            {
                Renderer3D.DrawMesh(world, mesh, color, texture, shaderType, meshRenderer.Material);
            }
        }

        Renderer3D.EndScene();

        Renderer2D.BeginScene(viewProjection);

        foreach (Entity entity in scene.View<Transform, Sprite2D>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            Transform transform = entity.GetComponent<Transform>();
            Sprite2D sprite = entity.GetComponent<Sprite2D>();
            if (!transform.Enabled || !sprite.Enabled) continue;

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

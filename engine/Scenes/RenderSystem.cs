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
    private static Framebuffer? s_hdrFramebuffer;

    /// <summary>
    /// Draws all mesh and sprite entities in the scene through the given camera.
    /// </summary>
    /// <param name="scene">The scene whose meshes and sprites are drawn.</param>
    /// <param name="viewProjection">The view-projection matrix to render with.</param>
    public static void Render(Scene scene, Matrix4x4 viewProjection)
    {
        PostProcessingComponent? postProcess = null;
        foreach (Entity entity in scene.View<PostProcessingComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var pp = entity.GetComponent<PostProcessingComponent>();
            if (pp.Enabled)
            {
                postProcess = pp;
                break;
            }
        }

        int[] currentFbo = new int[1];
        int[] viewport = new int[4];
        float[] clearColor = new float[4];

        if (postProcess != null)
        {
            unsafe
            {
                fixed (int* ptr = currentFbo) Renderer.Api.GetInteger(Silk.NET.OpenGL.GLEnum.FramebufferBinding, ptr);
                fixed (int* ptr = viewport) Renderer.Api.GetInteger(Silk.NET.OpenGL.GLEnum.Viewport, ptr);
                fixed (float* ptr = clearColor) Renderer.Api.GetFloat(Silk.NET.OpenGL.GLEnum.ColorClearValue, ptr);
            }

            if (s_hdrFramebuffer == null || s_hdrFramebuffer.Width != viewport[2] || s_hdrFramebuffer.Height != viewport[3])
            {
                s_hdrFramebuffer?.Dispose();
                s_hdrFramebuffer = new Framebuffer((uint)viewport[2], (uint)viewport[3], FramebufferFormat.RGBA16F);
            }

            s_hdrFramebuffer.Bind();
            Renderer.SetClearColor(clearColor[0], clearColor[1], clearColor[2], clearColor[3]);
            Renderer.Clear();
            Renderer.Api.Viewport(0, 0, (uint)viewport[2], (uint)viewport[3]);
        }

        bool hasDirLight = false;
        Vector3 dirLightDir = new Vector3(0, -1, 0);
        Vector3 dirLightColor = Vector3.One;
        float ambientIntensity = 0.3f;
        bool castShadows = false;
        Matrix4x4 lightSpaceMatrix = Matrix4x4.Identity;
        
        Span<Renderer3D.PointLightData> pointLights = stackalloc Renderer3D.PointLightData[4];
        int pointLightCount = 0;

        foreach (Entity entity in scene.View<Transform, LightComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var transform = entity.GetComponent<Transform>();
            var light = entity.GetComponent<LightComponent>();
            if (!transform.Enabled || !light.Enabled) continue;
            
            if (light.Type == LightType.Directional)
            {
                if (!hasDirLight)
                {
                    hasDirLight = true;
                    dirLightColor = light.Color * light.Intensity;
                    ambientIntensity = light.AmbientIntensity;
                    
                    // We want lightDir to point TOWARDS the light source for shader math.
                    dirLightDir = Vector3.Normalize(Vector3.TransformNormal(new Vector3(0, 0, 1), transform.Matrix));
                    
                    if (light.CastShadows)
                    {
                        castShadows = true;
                        Vector3 lightPos = dirLightDir * 100.0f; 
                        Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightPos, Vector3.Zero, Vector3.UnitY);
                        if (MathF.Abs(dirLightDir.Y) >= 0.999f) 
                        {
                            lightView = Matrix4x4.CreateLookAt(lightPos, Vector3.Zero, Vector3.UnitZ);
                        }
                        
                        Matrix4x4 lightProj = Matrix4x4.CreateOrthographic(100.0f, 100.0f, 1.0f, 200.0f);
                        lightSpaceMatrix = lightView * lightProj;
                    }
                }
            }
            else if (light.Type == LightType.Point)
            {
                if (pointLightCount < 4)
                {
                    pointLights[pointLightCount] = new Renderer3D.PointLightData
                    {
                        Position = transform.WorldPosition,
                        Color = light.Color,
                        Intensity = light.Intensity,
                        Range = light.Range
                    };
                    pointLightCount++;
                }
            }
        }
        
        if (castShadows)
        {
            Renderer3D.BeginShadowPass(lightSpaceMatrix);
            foreach (Entity entity in scene.View<Transform, MeshRenderer>())
            {
                if (!entity.IsActiveInHierarchy()) continue;
                MeshRenderer meshRenderer = entity.GetComponent<MeshRenderer>();
                var transform = entity.GetComponent<Transform>();
                if (!meshRenderer.Enabled || !transform.Enabled || meshRenderer.Model is null) continue;
                
                Matrix4x4 world = transform.Matrix;
                foreach (Mesh mesh in meshRenderer.Model.Meshes)
                {
                    Renderer3D.DrawShadowMesh(world, mesh);
                }
            }
            Renderer3D.EndShadowPass();
        }

        Renderer3D.BeginScene(viewProjection, hasDirLight, dirLightDir, dirLightColor, ambientIntensity, lightSpaceMatrix, castShadows, pointLights.Slice(0, pointLightCount));
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

        if (postProcess != null && s_hdrFramebuffer != null)
        {
            Renderer.Api.BindFramebuffer(Silk.NET.OpenGL.FramebufferTarget.Framebuffer, (uint)currentFbo[0]);
            Renderer.Api.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);
            
            PostProcessingRenderer.Draw(s_hdrFramebuffer.ColorAttachment, postProcess);
        }
    }
}

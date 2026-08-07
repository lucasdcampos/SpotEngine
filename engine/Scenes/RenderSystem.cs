using System;
using System.Numerics;
using Spot.Assets;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// Draws every entity's 3D mesh (<see cref="MeshComponent"/>) and 2D sprite (<see cref="Sprite2DComponent"/>),
/// each together with its <see cref="TransformComponent"/>.
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

    // Number of horizontal+vertical blur pairs used for bloom. Higher widens the glow at a small fill
    // cost; five reads as a soft, wide bloom at half resolution without visible box stepping.
    private const int BloomIterations = 5;

    // Synthesized when HDR is on but the scene has no PostProcessingComponent, so tone mapping and FXAA
    // still apply. Reused across frames rather than reallocated each render.
    private static PostProcessingComponent? s_defaultPostProcess;

    /// <summary>
    /// Draws all mesh and sprite entities in the scene through the given camera.
    /// </summary>
    /// <param name="scene">The scene whose meshes and sprites are drawn.</param>
    /// <param name="viewProjection">The view-projection matrix to render with.</param>
    /// <param name="cameraPosition">
    /// The camera's world position, used by the lighting shaders for view-dependent terms (specular,
    /// fresnel). When <see langword="null"/> it is approximated from the inverse view-projection, which
    /// is good enough for editor overlays but wrong for perspective specular — pass the real position.
    /// </param>
    public static void Render(Scene scene, Matrix4x4 viewProjection, Vector3? cameraPosition = null)
    {
        Vector3 cameraPos;
        if (cameraPosition.HasValue)
        {
            cameraPos = cameraPosition.Value;
        }
        else
        {
            Matrix4x4.Invert(viewProjection, out Matrix4x4 inv);
            Vector4 p = Vector4.Transform(new Vector4(0f, 0f, -1f, 1f), inv);
            cameraPos = new Vector3(p.X, p.Y, p.Z) / p.W;
        }

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

        // With HDR always-on, a scene without a PostProcessingComponent still renders through the HDR
        // buffer with the full default look — the same defaults a freshly added component would have:
        // ACES tone mapping, FXAA, threshold-gated bloom that only responds to genuine HDR highlights
        // (the sun disc, emissive surfaces), and only a very faint vignette. The baseline exalts the
        // engine's lighting rather than dressing it up with heavy stylistic filters. Adding the
        // component is for *customizing* that look, not for switching quality on.
        if (postProcess is null && Spot.Rendering.RenderSettings.Hdr)
        {
            postProcess = s_defaultPostProcess ??= new PostProcessingComponent();
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

        foreach (Entity entity in scene.View<TransformComponent, LightComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var transform = entity.GetComponent<TransformComponent>();
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
        
        if (Spot.Rendering.RendererDebug.Fullbright)
        {
            hasDirLight = false;
            castShadows = false;
            pointLightCount = 0;
        }

        if (castShadows)
        {
            Renderer3D.BeginShadowPass(lightSpaceMatrix);
            foreach (Entity entity in scene.View<TransformComponent, MeshComponent>())
            {
                if (!entity.IsActiveInHierarchy()) continue;
                MeshComponent meshRenderer = entity.GetComponent<MeshComponent>();
                var transform = entity.GetComponent<TransformComponent>();
                if (!meshRenderer.Enabled || !transform.Enabled) continue;

                ResolveAssets(meshRenderer);
                if (meshRenderer.Model is null) continue;

                Matrix4x4 world = transform.Matrix;
                foreach (Mesh mesh in meshRenderer.Model.Meshes)
                {
                    Renderer3D.DrawShadowMesh(world, mesh);
                }
            }
            Renderer3D.EndShadowPass();
        }

        if (Spot.Rendering.RendererDebug.Wireframe)
        {
            Renderer.Api.PolygonMode(Silk.NET.OpenGL.GLEnum.FrontAndBack, Silk.NET.OpenGL.GLEnum.Line);
        }

        Renderer3D.BeginScene(viewProjection, hasDirLight, dirLightDir, dirLightColor, ambientIntensity, lightSpaceMatrix, castShadows, pointLights.Slice(0, pointLightCount), cameraPos);
        
        foreach (Entity entity in scene.View<SkyboxComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var skybox = entity.GetComponent<SkyboxComponent>();
            if (!skybox.Enabled) continue;

            Renderer3D.DrawSkybox(skybox.SkyColor, skybox.GroundColor);
            break;
        }

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

        foreach (Entity entity in scene.View<TransformComponent, MeshComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            MeshComponent meshRenderer = entity.GetComponent<MeshComponent>();
            var transform = entity.GetComponent<TransformComponent>();
            if (!meshRenderer.Enabled || !transform.Enabled) continue;

            ResolveAssets(meshRenderer);

            if (meshRenderer.Model is null)
            {
                continue;
            }

            Matrix4x4 world = entity.GetComponent<TransformComponent>().Matrix;
            Vector4 color = meshRenderer.Material?.Color ?? meshRenderer.Color;
            Texture2D? texture = meshRenderer.Material?.Texture;
            int shaderType = (int)(meshRenderer.Material?.ShaderType ?? Spot.Assets.MaterialShaderType.Standard);
            foreach (Mesh mesh in meshRenderer.Model.Meshes)
            {
                Renderer3D.DrawMesh(world, mesh, color, texture, shaderType, meshRenderer.Material);
            }
        }

        Renderer3D.EndScene();

        if (Spot.Rendering.RendererDebug.Wireframe)
        {
            Renderer.Api.PolygonMode(Silk.NET.OpenGL.GLEnum.FrontAndBack, Silk.NET.OpenGL.GLEnum.Fill);
        }

        Renderer2D.BeginScene(viewProjection);

        foreach (Entity entity in scene.View<TransformComponent, Sprite2DComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;
            TransformComponent transform = entity.GetComponent<TransformComponent>();
            Sprite2DComponent sprite = entity.GetComponent<Sprite2DComponent>();
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

        if (Spot.Physics.PhysicsDebug.ShowColliders)
        {
            foreach (var entity in scene.View<Spot.Physics.BoxCollider2DComponent, TransformComponent>())
            {
                if (!entity.IsActiveInHierarchy()) continue;
                var transform = entity.GetComponent<TransformComponent>();
                var collider = entity.GetComponent<Spot.Physics.BoxCollider2DComponent>();
                if (!transform.Enabled || !collider.Enabled) continue;
                var bounds = collider.GetWorldBounds(new Vector2(transform.WorldPosition.X, transform.WorldPosition.Y), new Vector2(transform.WorldScale.X, transform.WorldScale.Y));
                Renderer2D.DrawRect(bounds.Center, bounds.HalfExtents * 2.0f, new Vector4(0.0f, 1.0f, 0.0f, 1.0f), 0.02f);
            }

            foreach (var entity in scene.View<Spot.Physics.BoxCollider3DComponent, TransformComponent>())
            {
                if (!entity.IsActiveInHierarchy()) continue;
                var transform = entity.GetComponent<TransformComponent>();
                var collider = entity.GetComponent<Spot.Physics.BoxCollider3DComponent>();
                if (!transform.Enabled || !collider.Enabled) continue;
                var bounds = collider.GetWorldBounds(transform.WorldPosition, transform.WorldScale);
                Vector3 min = bounds.Min;
                Vector3 max = bounds.Max;
                Vector4 color = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
                float t = 0.02f;

                Renderer2D.DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z), color, t);
                Renderer2D.DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, min.Y, max.Z), color, t);
                Renderer2D.DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(min.X, min.Y, max.Z), color, t);
                Renderer2D.DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, min.Y, min.Z), color, t);

                Renderer2D.DrawLine(new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color, t);
                Renderer2D.DrawLine(new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, max.Y, max.Z), color, t);
                Renderer2D.DrawLine(new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color, t);
                Renderer2D.DrawLine(new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, max.Y, min.Z), color, t);

                Renderer2D.DrawLine(new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, max.Y, min.Z), color, t);
                Renderer2D.DrawLine(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color, t);
                Renderer2D.DrawLine(new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z), color, t);
                Renderer2D.DrawLine(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color, t);
            }
        }

        Renderer2D.EndScene();

        if (postProcess != null && s_hdrFramebuffer != null)
        {
            // Extract and blur the scene's bright regions while the HDR buffer is still bound as the
            // source. Bloom manages its own (half-res) targets and leaves nothing bound, so do it before
            // rebinding the final target below.
            uint bloomTexture = 0;
            if (postProcess.EnableBloom)
            {
                bloomTexture = BloomRenderer.Generate(
                    s_hdrFramebuffer.ColorAttachment, viewport[2], viewport[3], postProcess.BloomThreshold, BloomIterations);
            }

            Renderer.Api.BindFramebuffer(Silk.NET.OpenGL.FramebufferTarget.Framebuffer, (uint)currentFbo[0]);
            Renderer.Api.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);

            // Carry the scene's depth from the HDR pass into the target buffer so anything drawn on top
            // afterwards (e.g. the editor grid and world axes) is occluded by the geometry instead of
            // showing through it. Skipped for the default framebuffer (game runtime), where nothing is
            // drawn over the composite and its depth format may not match for a blit.
            if (currentFbo[0] != 0)
                s_hdrFramebuffer.BlitDepthTo((uint)currentFbo[0], viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);

            PostProcessingRenderer.Draw(s_hdrFramebuffer.ColorAttachment, postProcess, bloomTexture);
        }
    }

    /// <summary>
    /// Lazily fills in a mesh renderer's <see cref="MeshComponent.Model"/> and <see cref="MeshComponent.Material"/>
    /// from their stored paths. Scene loading stores only the paths (so it never blocks startup on a heavy
    /// asset); this resolves them at draw time. Primitives and materials load synchronously — they're cheap —
    /// while model files load asynchronously in the background and stay <see langword="null"/> (skipping the
    /// draw) until ready. Because it runs only after the render loops have skipped disabled entities, a
    /// disabled object never pays to load its assets.
    /// </summary>
    private static void ResolveAssets(MeshComponent meshRenderer)
    {
        if (meshRenderer.Model is null && !string.IsNullOrEmpty(meshRenderer.ModelPath))
        {
            if (meshRenderer.ModelPath.StartsWith("primitive:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    meshRenderer.Model = Model.Load(meshRenderer.ModelPath);
                }
                catch (Exception ex)
                {
                    // An unknown primitive name can't be recovered; log once and drop the reference so we
                    // don't retry (and re-log) it every frame.
                    Spot.Core.Log.CoreError("Failed to load model '{0}': {1}", meshRenderer.ModelPath, ex.Message);
                    meshRenderer.ModelPath = null;
                }
            }
            else
            {
                // Non-blocking: returns null while the file parses in the background, the ready model once
                // its GPU upload has been finalized. RequestAsync tracks failures itself, so no spam here.
                meshRenderer.Model = ModelImporter.RequestAsync(meshRenderer.ModelPath);
            }
        }

        if (meshRenderer.Material is null && !string.IsNullOrEmpty(meshRenderer.MaterialPath))
        {
            // Material.Load is cached and never throws (it logs and returns a default on failure).
            meshRenderer.Material = Material.Load(meshRenderer.MaterialPath);
        }
    }
}

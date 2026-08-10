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
                    
                    if (light.CastShadows && Spot.Rendering.RenderSettings.Shadows)
                    {
                        castShadows = true;
                        lightSpaceMatrix = ComputeDirectionalShadowMatrix(dirLightDir, viewProjection, cameraPos);
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
            Renderer3D.EnsureShadowMapResolution(Spot.Rendering.RenderSettings.ShadowMapResolution);
            Renderer3D.BeginShadowPass(lightSpaceMatrix);
            foreach (Entity entity in scene.View<TransformComponent, MeshComponent>())
            {
                if (!entity.IsActiveInHierarchy()) continue;
                MeshComponent meshRenderer = entity.GetComponent<MeshComponent>();
                var transform = entity.GetComponent<TransformComponent>();
                if (!meshRenderer.Enabled || !transform.Enabled) continue;

                ResolveAssets(meshRenderer);
                if (meshRenderer.Model is null) continue;

                if (entity.TryGetComponent(out SkinnedMeshComponent? skinned) && skinned.Enabled &&
                    skinned.TryBuildPalette(entity, out Matrix4x4[] palette))
                {
                    DrawSkinnedShadowMeshes(meshRenderer, palette);
                    continue;
                }

                Matrix4x4 world = transform.Matrix;
                DrawShadowMeshes(meshRenderer, world);
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

            Vector4 color = meshRenderer.Material?.Color ?? meshRenderer.Color;
            Texture2D? texture = meshRenderer.Material?.Texture;

            if (entity.TryGetComponent(out SkinnedMeshComponent? skinned) && skinned.Enabled &&
                skinned.TryBuildPalette(entity, out Matrix4x4[] palette))
            {
                DrawSkinnedMeshes(meshRenderer, palette, color, texture);
                continue;
            }

            Matrix4x4 world = transform.Matrix;
            int shaderType = (int)(meshRenderer.Material?.ShaderType ?? Spot.Assets.MaterialShaderType.Standard);
            DrawMeshes(meshRenderer, world, color, texture, shaderType);
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
    /// Builds the light-space matrix for the directional shadow map. Unlike the old fixed box glued to
    /// the world origin (which meant shadows only existed within ~50 m of it), the shadow frustum here
    /// <b>follows the camera</b>, so shadows work anywhere in the world. Its size is
    /// <see cref="RenderSettings.ShadowDistance"/>, it is pushed a little toward where the camera looks so
    /// the map is spent on-screen, and the whole thing is <b>texel-snapped</b> so shadow edges stop
    /// crawling/shimmering as the camera moves.
    /// </summary>
    private static Matrix4x4 ComputeDirectionalShadowMatrix(Vector3 lightDir, Matrix4x4 viewProjection, Vector3 cameraPos)
    {
        float size = MathF.Max(Spot.Rendering.RenderSettings.ShadowDistance, 1.0f);
        float res = MathF.Max(Spot.Rendering.RenderSettings.ShadowMapResolution, 1);

        // Camera forward, flattened to the horizontal plane, so the shadow box leads the camera into the
        // scene it's looking at instead of centering half the texels behind the viewer.
        Vector3 forward = new Vector3(0, 0, 1);
        if (Matrix4x4.Invert(viewProjection, out Matrix4x4 invVP))
        {
            Vector4 n = Vector4.Transform(new Vector4(0, 0, -1, 1), invVP);
            Vector4 f = Vector4.Transform(new Vector4(0, 0, 1, 1), invVP);
            Vector3 dir = new Vector3(f.X, f.Y, f.Z) / f.W - new Vector3(n.X, n.Y, n.Z) / n.W;
            if (dir.LengthSquared() > 1e-6f) forward = dir;
        }
        forward.Y = 0;
        forward = forward.LengthSquared() > 1e-6f ? Vector3.Normalize(forward) : new Vector3(0, 0, 1);

        Vector3 center = cameraPos + forward * (size * 0.25f);

        // A generous slab along the light axis so tall geometry (and objects above/below the focus point)
        // never clips out of the shadow depth range.
        float depth = size * 3.0f;
        Vector3 up = MathF.Abs(lightDir.Y) >= 0.999f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 lightPos = center + lightDir * (depth * 0.5f);
        Matrix4x4 lightView = Matrix4x4.CreateLookAt(lightPos, center, up);
        Matrix4x4 lightProj = Matrix4x4.CreateOrthographic(size, size, 0.05f, depth);
        Matrix4x4 lightSpace = lightView * lightProj;

        // Texel snapping. The light view's orientation is fixed (only lightDir/up), so quantizing a fixed
        // world reference (the origin) to whole shadow-map texels pins the texel grid in world space —
        // the shadowed region then jumps in texel-sized steps instead of sliding continuously, which is
        // what kills the shimmering "swimming" along shadow edges as the camera moves.
        Vector4 originClip = Vector4.Transform(new Vector4(0, 0, 0, 1), lightSpace);
        float half = res * 0.5f;
        Vector2 texel = new Vector2(originClip.X, originClip.Y) * half;
        Vector2 offset = (new Vector2(MathF.Round(texel.X), MathF.Round(texel.Y)) - texel) / half;
        lightProj.M41 += offset.X;
        lightProj.M42 += offset.Y;
        return lightView * lightProj;
    }

    /// <summary>
    /// Draws a renderer's geometry, honoring <see cref="MeshComponent.SubmeshIndex"/>: the whole model
    /// when it is negative, otherwise the single named submesh (skipped silently when out of range).
    /// </summary>
    private static void DrawMeshes(MeshComponent meshRenderer, Matrix4x4 world, Vector4 color, Texture2D? texture, int shaderType)
    {
        IReadOnlyList<Mesh> meshes = meshRenderer.Model!.Meshes;
        int index = meshRenderer.SubmeshIndex;
        if (index < 0)
        {
            foreach (Mesh mesh in meshes)
            {
                Renderer3D.DrawMesh(world, mesh, color, texture, shaderType, meshRenderer.Material);
            }
        }
        else if (index < meshes.Count)
        {
            Renderer3D.DrawMesh(world, meshes[index], color, texture, shaderType, meshRenderer.Material);
        }
    }

    /// <summary>The <see cref="DrawMeshes"/> counterpart for the shadow pass (depth only).</summary>
    private static void DrawShadowMeshes(MeshComponent meshRenderer, Matrix4x4 world)
    {
        IReadOnlyList<Mesh> meshes = meshRenderer.Model!.Meshes;
        int index = meshRenderer.SubmeshIndex;
        if (index < 0)
        {
            foreach (Mesh mesh in meshes)
            {
                Renderer3D.DrawShadowMesh(world, mesh);
            }
        }
        else if (index < meshes.Count)
        {
            Renderer3D.DrawShadowMesh(world, meshes[index]);
        }
    }

    /// <summary>
    /// Draws a skinned renderer's submesh with the bone palette produced from the live skeleton. A skinned
    /// part always names a single submesh (its <see cref="MeshComponent.SubmeshIndex"/>), so only that one is
    /// drawn — and only when it actually uses the skinned vertex layout.
    /// </summary>
    private static void DrawSkinnedMeshes(MeshComponent meshRenderer, ReadOnlySpan<Matrix4x4> palette, Vector4 color, Texture2D? texture)
    {
        IReadOnlyList<Mesh> meshes = meshRenderer.Model!.Meshes;
        int index = meshRenderer.SubmeshIndex;
        if (index >= 0 && index < meshes.Count && meshes[index].IsSkinned)
        {
            Renderer3D.DrawSkinnedMesh(meshes[index], palette, color, texture, meshRenderer.Material);
        }
    }

    /// <summary>The <see cref="DrawSkinnedMeshes"/> counterpart for the shadow pass (depth only).</summary>
    private static void DrawSkinnedShadowMeshes(MeshComponent meshRenderer, ReadOnlySpan<Matrix4x4> palette)
    {
        IReadOnlyList<Mesh> meshes = meshRenderer.Model!.Meshes;
        int index = meshRenderer.SubmeshIndex;
        if (index >= 0 && index < meshes.Count && meshes[index].IsSkinned)
        {
            Renderer3D.DrawSkinnedShadowMesh(meshes[index], palette);
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

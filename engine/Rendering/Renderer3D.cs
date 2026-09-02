using System.Numerics;
using System.Runtime.InteropServices;

namespace Spot.Rendering;

/// <summary>
/// A simple 3D mesh renderer. Draws individual <see cref="Mesh"/> instances through a camera, with a
/// default shader that applies a fixed directional light so a model's shape reads clearly.
/// </summary>
/// <remarks>
/// This is the mid-level counterpart to <see cref="Renderer2D"/>: it consumes high-level draw
/// requests and delegates the actual draw calls to <see cref="Renderer"/>. It does not batch — each
/// mesh is a single draw call — which is plenty while materials and lighting are still to come. For
/// full control, build a <see cref="Mesh"/> and drive it here, or drop down to <see cref="Renderer"/>
/// / <see cref="Renderer.Api"/> yourself.
/// </remarks>
public static partial class Renderer3D
{
    /// <summary>
    /// The maximum number of bones a single skinned draw can upload. Must match the bone-array size in
    /// the skinned shader sources (Renderer3D.Shaders.cs); a skeleton with more bones is clamped (and
    /// logged) rather than crashing.
    /// </summary>
    public const int MaxBones = 128;

    /// <summary>
    /// Per-instance data for an instanced mesh draw: the world matrix and a color, laid out as 20 tightly
    /// packed floats (a mat4 followed by a vec4) that map directly to the instanced shader's attributes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct InstanceData
    {
        /// <summary>The instance's world (model) matrix.</summary>
        public Matrix4x4 Model;

        /// <summary>The instance's color, multiplied into the shaded result (and the texture, when set).</summary>
        public Vector4 Color;
    }

    // Floats per InstanceData (mat4 = 16 + vec4 = 4). Also the count of instances uploaded per draw before
    // the shared buffer is refilled — a fixed capacity keeps its GPU handle stable so the per-mesh instanced
    // VAOs that reference it never dangle.
    private const int InstanceFloats = 20;
    private const int MaxInstancesPerBatch = 1024;

    private static Shader? s_shader;
    private static Shader? s_instancedShader;
    private static VertexBuffer? s_instanceBuffer;
    private static Shader? s_waterShader;
    private static Shader? s_skyboxShader;
    private static Shader? s_cloudsShader;
    private static Shader? s_gridShader;
    private static Shader? s_shadowShader;
    private static Shader? s_skinnedShader;
    private static Shader? s_skinnedShadowShader;
    private static VertexArray? s_emptyVao;
    private static Texture2D? s_whiteTexture;
    private static DepthFramebuffer? s_shadowMap;
    private static Matrix4x4 s_viewProjection = Matrix4x4.Identity;
    private static Matrix4x4 s_inverseViewProjection = Matrix4x4.Identity;
    private static Vector3 s_cameraPosition = Vector3.Zero;
    private static Matrix4x4 s_lightSpaceMatrix = Matrix4x4.Identity;

    public struct PointLightData
    {
        public Vector3 Position;
        public Vector3 Color;
        public float Intensity;
        public float Range;
    }

    private static int s_hasDirLight = 0;
    private static int s_castShadows = 0;
    private static Vector3 s_lightDir = Vector3.UnitY;
    private static Vector3 s_lightColor = Vector3.One;
    private static float s_ambientIntensity = 0.3f;
    
    private static PointLightData[] s_pointLights = new PointLightData[4];
    private static int s_pointLightCount = 0;

    // Sky colours of the active skybox, captured by DrawSkybox and fed to the water shader so water
    // reflects the same sky the scene renders. Reset each BeginScene; s_hasSkybox stays 0 with no skybox.
    private static Vector3 s_skyColor = new Vector3(0.55f, 0.75f, 1.0f);
    private static Vector3 s_groundColor = new Vector3(0.35f, 0.37f, 0.4f);
    private static int s_hasSkybox = 0;

    // Pre-built uniform-name strings for the point-light array (max 4 slots), so ApplyLighting never
    // interpolates a string — and so never allocates — on the render path.
    private static readonly string[] s_pointLightPosNames = { "uPointLights[0].position", "uPointLights[1].position", "uPointLights[2].position", "uPointLights[3].position" };
    private static readonly string[] s_pointLightColorNames = { "uPointLights[0].color", "uPointLights[1].color", "uPointLights[2].color", "uPointLights[3].color" };
    private static readonly string[] s_pointLightIntensityNames = { "uPointLights[0].intensity", "uPointLights[1].intensity", "uPointLights[2].intensity", "uPointLights[3].intensity" };
    private static readonly string[] s_pointLightRangeNames = { "uPointLights[0].range", "uPointLights[1].range", "uPointLights[2].range", "uPointLights[3].range" };

    // Per-scene stamp: BeginScene bumps it, and each lit shader records the stamp at which it last had the
    // scene-constant uniforms (camera + all lights) uploaded. That turns ~15 redundant uniform uploads per
    // draw into a single upload per shader per scene. -1 means "not yet applied".
    private static int s_sceneStamp;
    private static int s_stdConstantsStamp = -1;
    private static int s_instancedConstantsStamp = -1;
    private static int s_waterConstantsStamp = -1;
    private static int s_skinnedConstantsStamp = -1;

    // The shader currently bound during the mesh pass, so consecutive draws that share a shader skip a
    // redundant program bind. Reset each BeginScene; maintained only by the mesh-pass draw calls.
    private static Shader? s_lastMeshShader;

    // Textures currently bound to the albedo (unit 0) and normal-map (unit 2) slots during the mesh pass,
    // so consecutive draws that share a texture skip a redundant bind (texture binds are pricier than
    // uniform sets). Reset each BeginScene; maintained only by the mesh-pass draw calls.
    private static Texture2D? s_lastAlbedoTexture;
    private static Texture2D? s_lastNormalTexture;

    /// <summary>
    /// Creates the shared shader and fallback texture. Called once by the application after the renderer is ready.
    /// </summary>
    public static void Init()
    {
        s_shader = new Shader(VertexShaderSource, FragmentShaderSource);
        s_instancedShader = new Shader(InstancedVertexShaderSource, FragmentShaderSource);
        s_waterShader = new Shader(WaterVertexShaderSource, WaterFragmentShaderSource);
        s_skyboxShader = new Shader(SkyboxVertexShaderSource, SkyboxFragmentShaderSource);
        s_cloudsShader = new Shader(CloudsVertexShaderSource, CloudsFragmentShaderSource);
        s_gridShader = new Shader(GridVertexShaderSource, GridFragmentShaderSource);
        s_shadowShader = new Shader(ShadowVertexShaderSource, ShadowFragmentShaderSource);
        s_skinnedShader = new Shader(SkinnedVertexShaderSource, FragmentShaderSource);
        s_skinnedShadowShader = new Shader(SkinnedShadowVertexShaderSource, ShadowFragmentShaderSource);
        s_emptyVao = new VertexArray();

        // One fixed-capacity, dynamically-updated buffer feeds every instanced batch. Its handle stays put
        // for the process lifetime, so the per-mesh instanced VAOs that bind it never point at freed storage.
        s_instanceBuffer = new VertexBuffer(
            (uint)(MaxInstancesPerBatch * InstanceFloats),
            ShaderDataType.Float4,  // model matrix row 0 (instanced attribute location 3)
            ShaderDataType.Float4,  // model matrix row 1 (location 4)
            ShaderDataType.Float4,  // model matrix row 2 (location 5)
            ShaderDataType.Float4,  // model matrix row 3 (location 6)
            ShaderDataType.Float4); // color (location 7)

        s_shadowMap = new DepthFramebuffer(2048, 2048);

        // A 1x1 white texture lets untextured (solid-color) meshes reuse the textured path: texture * color == color.
        ReadOnlySpan<byte> white = stackalloc byte[] { 255, 255, 255, 255 };
        s_whiteTexture = new Texture2D(1, 1, white);
    }

    /// <summary>
    /// Begins a 3D scene. Meshes drawn until <see cref="EndScene"/> use this view-projection.
    /// </summary>
    public static void BeginScene(Matrix4x4 viewProjection, bool hasLight = false, Vector3 lightDir = default, Vector3 lightColor = default, float ambientIntensity = 0.3f, Matrix4x4 lightSpaceMatrix = default, bool castShadows = false, System.ReadOnlySpan<PointLightData> pointLights = default, Vector3 cameraPosition = default)
    {
        // New scene pass: bump the stamp so each lit shader re-uploads camera + lights once (on its first
        // draw this scene), and clear the mesh-pass bind tracker.
        s_sceneStamp++;
        s_lastMeshShader = null;
        s_lastAlbedoTexture = null;
        s_lastNormalTexture = null;

        s_viewProjection = viewProjection;
        // Invert once per scene: the skybox, clouds and grid all need the inverse view-projection, and
        // recomputing it per draw was pure waste. The camera position is supplied by the caller (the
        // camera's world position) rather than derived from the matrix, which is only approximate.
        Matrix4x4.Invert(viewProjection, out s_inverseViewProjection);
        s_cameraPosition = cameraPosition;
        s_hasDirLight = hasLight ? 1 : 0;
        s_lightDir = hasLight ? lightDir : Vector3.UnitY;
        s_lightColor = hasLight ? lightColor : Vector3.One;
        s_ambientIntensity = ambientIntensity;
        s_lightSpaceMatrix = lightSpaceMatrix;
        s_castShadows = castShadows ? 1 : 0;
        // No skybox until DrawSkybox says otherwise this frame; water then falls back to a default sky.
        s_hasSkybox = 0;

        s_pointLightCount = System.Math.Min(pointLights.Length, 4);
        for (int i = 0; i < s_pointLightCount; i++)
        {
            s_pointLights[i] = pointLights[i];
        }
    }

    private static FramebufferHandle s_prevRenderTarget;
    private static int s_prevViewportX;
    private static int s_prevViewportY;
    private static uint s_prevViewportW;
    private static uint s_prevViewportH;

    /// <summary>
    /// Ensures the directional shadow map exists at the requested resolution, rebuilding it if the
    /// resolution changed. Cheap when unchanged; call it before <see cref="BeginShadowPass"/> so
    /// <see cref="RenderSettings.ShadowMapResolution"/> can be tuned at runtime.
    /// </summary>
    public static void EnsureShadowMapResolution(int resolution)
    {
        resolution = System.Math.Clamp(resolution, 256, 8192);
        if (s_shadowMap is null || s_shadowMap.Width != (uint)resolution)
        {
            s_shadowMap?.Dispose();
            s_shadowMap = new DepthFramebuffer((uint)resolution, (uint)resolution);
        }
    }

    /// <summary>
    /// Begins a shadow map pass. Meshes drawn with <see cref="DrawShadowMesh"/> will be rendered to the shadow map.
    /// </summary>
    public static void BeginShadowPass(Matrix4x4 lightSpaceMatrix)
    {
        s_lightSpaceMatrix = lightSpaceMatrix;

        // Save the current render target and viewport (tracked by Renderer, not queried from the GPU) so
        // EndShadowPass can restore them across backends.
        s_prevRenderTarget = Renderer.CurrentRenderTarget;
        s_prevViewportX = Renderer.ViewportX;
        s_prevViewportY = Renderer.ViewportY;
        s_prevViewportW = Renderer.ViewportWidth;
        s_prevViewportH = Renderer.ViewportHeight;

        s_shadowMap!.Bind();
        Renderer.ClearDepth();
        s_shadowShader!.Use();
        s_shadowShader.SetUniform("uLightSpaceMatrix", s_lightSpaceMatrix);
        // Render front faces (default back-face culling), not front-culling: front-culling pushes the occluder
        // depth to the far side of solid meshes, which — combined with normal-offset receiver bias in the lit
        // shaders — reads as a gap between an object and its shadow. Normal offset alone handles the acne.
    }

    /// <summary>
    /// Draws a mesh into the shadow map. Must be called between <see cref="BeginShadowPass"/> and <see cref="EndShadowPass"/>.
    /// </summary>
    public static void DrawShadowMesh(Matrix4x4 model, Mesh mesh)
    {
        s_shadowShader!.SetUniform("uModel", model);
        Renderer.DrawIndexed(mesh.VertexArray, mesh.IndexCount);
    }

    /// <summary>
    /// Ends the current shadow map pass.
    /// </summary>
    public static void EndShadowPass() =>
        Renderer.BindRenderTarget(s_prevRenderTarget, s_prevViewportX, s_prevViewportY, s_prevViewportW, s_prevViewportH);

    /// <summary>
    /// Draws a mesh with the given world transform, color, and optional texture.
    /// </summary>
    /// <param name="model">The mesh's world (model) matrix.</param>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="color">A color multiplied into the shaded result (and into the texture, when set).</param>
    /// <param name="texture">The surface texture, or <see langword="null"/> for a solid color.</param>
    public static void DrawMesh(Matrix4x4 model, Mesh mesh, Vector4 color, Texture2D? texture = null, int shaderType = 0, Spot.Assets.Material? material = null)
    {
        Shader? activeShader = shaderType == 1 ? s_waterShader : s_shader;
        
        if (activeShader is null || s_whiteTexture is null)
        {
            return;
        }

        BindAlbedo(texture ?? s_whiteTexture);

        if (!ReferenceEquals(activeShader, s_lastMeshShader))
        {
            activeShader.Use();
            s_lastMeshShader = activeShader;
        }

        // Camera + lights are identical for every mesh this scene, so upload them once per shader here
        // rather than on every draw call.
        if (shaderType == 1)
        {
            EnsureFrameConstants(activeShader, ref s_waterConstantsStamp);
        }
        else
        {
            EnsureFrameConstants(activeShader, ref s_stdConstantsStamp);
        }

        activeShader.SetUniform("uModel", model);
        activeShader.SetUniform("uColor", color);
        activeShader.SetUniform("uTexture", 0);

        Vector2 tiling = material?.Tiling ?? Vector2.One;
        int autoTile = (material?.AutoTile ?? false) ? 1 : 0;
        activeShader.SetUniform("uTiling", tiling);
        activeShader.SetUniform("uAutoTile", autoTile);
        
        if (autoTile == 1)
        {
            float scaleX = new Vector3(model.M11, model.M12, model.M13).Length();
            float scaleY = new Vector3(model.M21, model.M22, model.M23).Length();
            float scaleZ = new Vector3(model.M31, model.M32, model.M33).Length();
            activeShader.SetUniform("uModelScale", new Vector3(scaleX, scaleY, scaleZ));
        }

        if (shaderType == 0) // Standard
        {
            activeShader.SetUniform("uMetallic", material?.Metallic ?? 0.0f);
            activeShader.SetUniform("uEmissiveColor", material?.EmissiveColor ?? Vector3.Zero);
            activeShader.SetUniform("uEmissiveIntensity", material?.EmissiveIntensity ?? 1.0f);

            if (material?.NormalMap != null)
            {
                if (!ReferenceEquals(material.NormalMap, s_lastNormalTexture))
                {
                    material.NormalMap.Bind(2);
                    s_lastNormalTexture = material.NormalMap;
                }
                activeShader.SetUniform("uNormalMap", 2);
                activeShader.SetUniform("uHasNormalMap", 1);
            }
            else
            {
                activeShader.SetUniform("uHasNormalMap", 0);
            }
        }
        else if (shaderType == 1) // Water
        {
            activeShader.SetUniform("uTime", Spot.Core.Time.UnscaledTime);
            
            float speed = material?.WaveSpeed ?? 1.0f;
            float scale = material?.WaveScale ?? 1.0f;
            float strength = material?.WaveStrength ?? 0.3f;
            float specPower = material?.SpecularPower ?? 64.0f;
            
            activeShader.SetUniform("uWaveSpeed", speed);
            activeShader.SetUniform("uWaveScale", scale);
            activeShader.SetUniform("uWaveStrength", strength);
            activeShader.SetUniform("uSpecularPower", specPower);

            activeShader.SetUniform("uSkyColor", s_skyColor);
            activeShader.SetUniform("uGroundColor", s_groundColor);
            activeShader.SetUniform("uHasSkybox", s_hasSkybox);
        }

        Renderer.DrawIndexed(mesh.VertexArray, mesh.IndexCount);
    }

    /// <summary>
    /// Draws many copies of one rigid mesh — each with its own world matrix and color — in as few draw calls
    /// as possible (one per <see cref="MaxInstancesPerBatch"/> instances). All copies share the material, so
    /// only their per-instance transform and color vary; the standard (lit) shader is used. This is the
    /// scalable counterpart to <see cref="DrawMesh"/> for scenes that place the same model many times.
    /// </summary>
    /// <param name="mesh">The rigid mesh to draw (must not use the skinned layout).</param>
    /// <param name="instances">The per-instance world matrices and colors.</param>
    /// <param name="texture">The shared surface texture, or <see langword="null"/> for a solid color.</param>
    /// <param name="material">The shared surface material, or <see langword="null"/> for defaults.</param>
    public static void DrawMeshInstanced(Mesh mesh, ReadOnlySpan<InstanceData> instances, Texture2D? texture = null, Spot.Assets.Material? material = null)
    {
        if (instances.IsEmpty || s_instancedShader is null || s_whiteTexture is null || s_instanceBuffer is null)
        {
            return;
        }

        BindAlbedo(texture ?? s_whiteTexture);

        if (!ReferenceEquals(s_instancedShader, s_lastMeshShader))
        {
            s_instancedShader.Use();
            s_lastMeshShader = s_instancedShader;
        }

        EnsureFrameConstants(s_instancedShader, ref s_instancedConstantsStamp);

        s_instancedShader.SetUniform("uTexture", 0);
        s_instancedShader.SetUniform("uTiling", material?.Tiling ?? Vector2.One);
        s_instancedShader.SetUniform("uAutoTile", (material?.AutoTile ?? false) ? 1 : 0);
        s_instancedShader.SetUniform("uMetallic", material?.Metallic ?? 0.0f);
        s_instancedShader.SetUniform("uEmissiveColor", material?.EmissiveColor ?? Vector3.Zero);
        s_instancedShader.SetUniform("uEmissiveIntensity", material?.EmissiveIntensity ?? 1.0f);

        if (material?.NormalMap != null)
        {
            if (!ReferenceEquals(material.NormalMap, s_lastNormalTexture))
            {
                material.NormalMap.Bind(2);
                s_lastNormalTexture = material.NormalMap;
            }
            s_instancedShader.SetUniform("uNormalMap", 2);
            s_instancedShader.SetUniform("uHasNormalMap", 1);
        }
        else
        {
            s_instancedShader.SetUniform("uHasNormalMap", 0);
        }

        VertexArray vao = mesh.GetInstancedVertexArray(s_instanceBuffer);
        ReadOnlySpan<float> floats = MemoryMarshal.Cast<InstanceData, float>(instances);

        // Upload and draw in chunks no larger than the shared buffer's capacity.
        for (int offset = 0; offset < instances.Length; offset += MaxInstancesPerBatch)
        {
            int count = System.Math.Min(MaxInstancesPerBatch, instances.Length - offset);
            s_instanceBuffer.SetData(floats.Slice(offset * InstanceFloats, count * InstanceFloats));
            Renderer.DrawIndexedInstanced(vao, mesh.IndexCount, (uint)count);
        }
    }

    /// <summary>
    /// Draws a skinned mesh with the given bone palette. The mesh must use the skinned vertex layout; the
    /// palette holds one matrix per bone (<c>InverseBind * boneWorld</c>, which already yields world space),
    /// so no model matrix is needed. Only the standard (lit) shader is supported for skinned meshes.
    /// </summary>
    /// <param name="mesh">The skinned mesh to draw.</param>
    /// <param name="bones">The bone matrices, one per bone (capped at <see cref="MaxBones"/>).</param>
    /// <param name="color">A color multiplied into the shaded result (and into the texture, when set).</param>
    /// <param name="texture">The surface texture, or <see langword="null"/> for a solid color.</param>
    /// <param name="material">The surface material, or <see langword="null"/> for defaults.</param>
    public static void DrawSkinnedMesh(Mesh mesh, ReadOnlySpan<Matrix4x4> bones, Vector4 color, Texture2D? texture = null, Spot.Assets.Material? material = null)
    {
        Shader? activeShader = s_skinnedShader;
        if (activeShader is null || s_whiteTexture is null)
        {
            return;
        }

        BindAlbedo(texture ?? s_whiteTexture);

        if (!ReferenceEquals(activeShader, s_lastMeshShader))
        {
            activeShader.Use();
            s_lastMeshShader = activeShader;
        }

        EnsureFrameConstants(activeShader, ref s_skinnedConstantsStamp);

        activeShader.SetUniform("uColor", color);
        activeShader.SetUniform("uTexture", 0);
        activeShader.SetUniform("uBones", ClampBones(bones));

        // Skinning replaces the model matrix, so auto-tiling (which scales UVs by the model matrix) is off.
        activeShader.SetUniform("uTiling", material?.Tiling ?? Vector2.One);
        activeShader.SetUniform("uAutoTile", 0);

        activeShader.SetUniform("uMetallic", material?.Metallic ?? 0.0f);
        activeShader.SetUniform("uEmissiveColor", material?.EmissiveColor ?? Vector3.Zero);
        activeShader.SetUniform("uEmissiveIntensity", material?.EmissiveIntensity ?? 1.0f);

        if (material?.NormalMap != null)
        {
            if (!ReferenceEquals(material.NormalMap, s_lastNormalTexture))
            {
                material.NormalMap.Bind(2);
                s_lastNormalTexture = material.NormalMap;
            }
            activeShader.SetUniform("uNormalMap", 2);
            activeShader.SetUniform("uHasNormalMap", 1);
        }
        else
        {
            activeShader.SetUniform("uHasNormalMap", 0);
        }

        Renderer.DrawIndexed(mesh.VertexArray, mesh.IndexCount);
    }

    /// <summary>
    /// Draws a skinned mesh into the shadow map with the given bone palette. Must be called between
    /// <see cref="BeginShadowPass"/> and <see cref="EndShadowPass"/>.
    /// </summary>
    /// <param name="mesh">The skinned mesh to draw.</param>
    /// <param name="bones">The bone matrices, one per bone (capped at <see cref="MaxBones"/>).</param>
    public static void DrawSkinnedShadowMesh(Mesh mesh, ReadOnlySpan<Matrix4x4> bones)
    {
        if (s_skinnedShadowShader is null)
        {
            return;
        }

        s_skinnedShadowShader.Use();
        s_skinnedShadowShader.SetUniform("uLightSpaceMatrix", s_lightSpaceMatrix);
        s_skinnedShadowShader.SetUniform("uBones", ClampBones(bones));
        Renderer.DrawIndexed(mesh.VertexArray, mesh.IndexCount);
    }

    // Caps a bone palette at MaxBones so a rig larger than the shader's uniform array can't overrun it.
    private static ReadOnlySpan<Matrix4x4> ClampBones(ReadOnlySpan<Matrix4x4> bones)
    {
        if (bones.Length <= MaxBones)
        {
            return bones;
        }

        Spot.Core.Log.CoreWarn("Skeleton has {0} bones but the shader supports at most {1}; extra bones are ignored.", bones.Length, MaxBones);
        return bones[..MaxBones];
    }

    // Binds a texture to the albedo slot (unit 0) only when it differs from the one already bound this mesh
    // pass, so consecutive draws that share a texture skip a redundant bind. Reset each BeginScene.
    private static void BindAlbedo(Texture2D texture)
    {
        if (!ReferenceEquals(texture, s_lastAlbedoTexture))
        {
            texture.Bind(0);
            s_lastAlbedoTexture = texture;
        }
    }

    // Uploads the scene-constant uniforms (camera + all lights) to a lit shader once per scene. The
    // per-shader stamp is compared against the current scene stamp, so the first draw that uses each shader
    // pays the upload and the rest skip it — GL keeps a program's uniform values across bind switches, so
    // re-selecting a shader later in the pass doesn't need them re-sent.
    private static void EnsureFrameConstants(Shader shader, ref int shaderStamp)
    {
        if (shaderStamp == s_sceneStamp)
        {
            return;
        }

        shaderStamp = s_sceneStamp;
        shader.SetUniform("uViewProjection", s_viewProjection);
        shader.SetUniform("uCameraPos", s_cameraPosition);
        ApplyLighting(shader);
    }

    // Uploads the directional light, shadow map and point lights shared by the standard and skinned shaders.
    private static void ApplyLighting(Shader shader)
    {
        shader.SetUniform("uHasDirectionalLight", s_hasDirLight);
        if (s_hasDirLight == 1)
        {
            shader.SetUniform("uLightDir", s_lightDir);
            shader.SetUniform("uLightColor", s_lightColor);
            shader.SetUniform("uAmbientIntensity", s_ambientIntensity);
        }

        if (s_castShadows == 1)
        {
            s_shadowMap!.BindDepthTexture(1);
            shader.SetUniform("uShadowMap", 1);
            shader.SetUniform("uLightSpaceMatrix", s_lightSpaceMatrix);
            // World size of one shadow-map texel, so the shader's normal-offset bias is expressed in the
            // same world units regardless of resolution/distance. Matches the box size used to build the
            // light matrix (RenderSystem.ComputeDirectionalShadowMatrix).
            float shadowSize = MathF.Max(RenderSettings.ShadowDistance, 1.0f);
            float shadowRes = MathF.Max(s_shadowMap.Width, 1u);
            shader.SetUniform("uShadowTexelSize", shadowSize / shadowRes);
            shader.SetUniform("uCastShadows", 1);
        }
        else
        {
            shader.SetUniform("uCastShadows", 0);
        }

        shader.SetUniform("uPointLightCount", s_pointLightCount);
        for (int i = 0; i < s_pointLightCount; i++)
        {
            shader.SetUniform(s_pointLightPosNames[i], s_pointLights[i].Position);
            shader.SetUniform(s_pointLightColorNames[i], s_pointLights[i].Color);
            shader.SetUniform(s_pointLightIntensityNames[i], s_pointLights[i].Intensity);
            shader.SetUniform(s_pointLightRangeNames[i], s_pointLights[i].Range);
        }
    }

    /// <summary>
    /// Ends the current 3D scene. Present for symmetry with <see cref="Renderer2D"/>; currently a no-op.
    /// </summary>
    public static void EndScene()
    {
    }

    /// <summary>
    /// Draws the procedural skybox.
    /// </summary>
    public static void DrawSkybox(Vector3 skyColor, Vector3 groundColor)
    {
        if (s_skyboxShader == null || s_emptyVao == null) return;

        // Remember the sky palette so water drawn later this frame can reflect it.
        s_skyColor = skyColor;
        s_groundColor = groundColor;
        s_hasSkybox = 1;

        s_skyboxShader.Use();
        s_skyboxShader.SetUniform("uInverseViewProjection", s_inverseViewProjection);
        
        s_skyboxShader.SetUniform("uSkyColor", skyColor);
        s_skyboxShader.SetUniform("uGroundColor", groundColor);
        
        s_skyboxShader.SetUniform("uLightDir", s_lightDir);
        s_skyboxShader.SetUniform("uLightColor", s_lightColor);
        s_skyboxShader.SetUniform("uHasDirLight", s_hasDirLight);

        Renderer.SetDepthTest(false);
        Renderer.DrawArrays(s_emptyVao, 3);
        Renderer.SetDepthTest(true);
    }

    /// <summary>
    /// Draws procedural dynamic clouds over the sky.
    /// </summary>
    public static void DrawDynamicClouds(float colorTopX, float colorTopY, float colorTopZ, 
        float colorBotX, float colorBotY, float colorBotZ, 
        float speed, float density, float height, float opacity, float volume, float time)
    {
        if (s_cloudsShader == null || s_emptyVao == null) return;

        s_cloudsShader.Use();
        s_cloudsShader.SetUniform("uInverseViewProjection", s_inverseViewProjection);
        s_cloudsShader.SetUniform("uColorTop", new Vector3(colorTopX, colorTopY, colorTopZ));
        s_cloudsShader.SetUniform("uColorBottom", new Vector3(colorBotX, colorBotY, colorBotZ));
        s_cloudsShader.SetUniform("uSpeed", speed);
        s_cloudsShader.SetUniform("uDensity", density);
        s_cloudsShader.SetUniform("uHeight", height);
        s_cloudsShader.SetUniform("uOpacity", opacity);
        s_cloudsShader.SetUniform("uVolume", volume);
        s_cloudsShader.SetUniform("uTime", time);

        Renderer.Device.SetCapability(GraphicsCapability.Blend, true);
        Renderer.Device.SetBlendFunc(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);

        Renderer.SetDepthTest(false);
        Renderer.DrawArrays(s_emptyVao, 3);
        Renderer.SetDepthTest(true);
    }

    /// <summary>
    /// Draws an infinite anti-aliased 3D grid plane for the editor.
    /// </summary>
    public static void DrawEditorGrid(Vector3 cameraPos)
    {
        if (s_gridShader == null || s_emptyVao == null) return;

        s_gridShader.Use();
        s_gridShader.SetUniform("uViewProjection", s_viewProjection);
        s_gridShader.SetUniform("uInverseViewProjection", s_inverseViewProjection);
        s_gridShader.SetUniform("uCameraPos", cameraPos);

        Renderer.Device.SetCapability(GraphicsCapability.Blend, true);
        Renderer.Device.SetBlendFunc(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha);

        Renderer.DrawArrays(s_emptyVao, 3);
        
        // Editor will reset or disable blending later if needed, but standard UI and transparent sprites need it too.
    }
}

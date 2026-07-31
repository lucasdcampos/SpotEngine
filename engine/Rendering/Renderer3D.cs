using System.Numerics;

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
public static class Renderer3D
{
    private const string VertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;

        uniform mat4 uViewProjection;
        uniform mat4 uModel;

        out vec3 vNormal;
        out vec2 vTexCoord;

        void main()
        {
            vNormal = mat3(uModel) * aNormal;
            vTexCoord = aTexCoord;
            gl_Position = uViewProjection * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource =
        """
        #version 330 core
        in vec3 vNormal;
        in vec2 vTexCoord;

        uniform vec4 uColor;
        uniform sampler2D uTexture;

        uniform int uHasDirectionalLight;
        uniform vec3 uLightDir;
        uniform vec3 uLightColor;
        uniform float uAmbientIntensity;

        out vec4 fragColor;

        void main()
        {
            vec4 albedo = texture(uTexture, vTexCoord) * uColor;
            
            if (uHasDirectionalLight == 1)
            {
                vec3 normal = normalize(vNormal);
                vec3 lightDir = normalize(uLightDir);
                float diffuse = max(dot(normal, lightDir), 0.0);
                vec3 lighting = (uAmbientIntensity + diffuse) * uLightColor;
                fragColor = vec4(albedo.rgb * lighting, albedo.a);
            }
            else
            {
                fragColor = albedo; // Unlit
            }
        }
        """;

    private const string WaterVertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;

        uniform mat4 uViewProjection;
        uniform mat4 uModel;
        uniform float uTime;
        uniform float uWaveSpeed;
        uniform float uWaveScale;
        uniform float uWaveStrength;

        out vec3 vFragPos;
        out vec3 vNormal;
        out vec2 vTexCoord;

        void main()
        {
            // Simple low-frequency vertex displacement (Gerstner-lite)
            vec3 pos = aPosition;
            float time = uTime * uWaveSpeed;
            // Only displace Y if normal points up
            if (aNormal.y > 0.5) {
                float wave = sin(pos.x * uWaveScale * 2.0 + time) * 0.1 
                           + cos(pos.z * uWaveScale * 1.5 + time * 1.2) * 0.1;
                pos.y += wave * uWaveStrength;
            }
            
            vec4 worldPos = uModel * vec4(pos, 1.0);
            vFragPos = worldPos.xyz;
            vNormal = mat3(transpose(inverse(uModel))) * aNormal;
            vTexCoord = aTexCoord;
            gl_Position = uViewProjection * worldPos;
        }
        """;

    private const string WaterFragmentShaderSource =
        """
        #version 330 core
        in vec3 vFragPos;
        in vec3 vNormal;
        in vec2 vTexCoord;

        uniform vec4 uColor;
        uniform sampler2D uTexture;
        uniform float uTime;
        uniform mat4 uInverseViewProjection;
        
        uniform float uWaveSpeed;
        uniform float uWaveScale;
        uniform float uWaveStrength;
        uniform float uSpecularPower;

        uniform int uHasDirectionalLight;
        uniform vec3 uLightDir;
        uniform vec3 uLightColor;
        uniform float uAmbientIntensity;

        out vec4 fragColor;
        
        float hash(vec2 p) {
            vec3 p3  = fract(vec3(p.xyx) * .1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }

        float noise(vec2 x) {
            vec2 i = floor(x);
            vec2 f = fract(x);
            float a = hash(i);
            float b = hash(i + vec2(1.0, 0.0));
            float c = hash(i + vec2(0.0, 1.0));
            float d = hash(i + vec2(1.0, 1.0));
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(a, b, u.x) + (c - a)* u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
        }
        
        float fbm(vec2 p) {
            float v = 0.0;
            float a = 0.5;
            for(int i=0; i<3; i++) {
                v += a * noise(p);
                p *= 2.0;
                a *= 0.5;
            }
            return v;
        }

        void main()
        {
            float time = uTime * uWaveSpeed;
            float scale = uWaveScale * 2.0;
            
            // Create layered ripples using FBM
            vec2 uv1 = vFragPos.xz * scale + vec2(time * 0.5, time * 0.3);
            vec2 uv2 = vFragPos.xz * (scale * 2.0) - vec2(time * 0.3, time * 0.6);
            
            float n1 = fbm(uv1);
            float n2 = fbm(uv2);
            
            // Perturb normal
            vec3 normal = normalize(vNormal);
            vec3 perturbedNormal = normalize(normal + vec3(n1 - 0.5, 0.0, n2 - 0.5) * (uWaveStrength * 1.5));
            
            vec4 texColor = texture(uTexture, vTexCoord);
            vec4 albedo = texColor * uColor;
            
            // Calculate approximate camera position from uInverseViewProjection
            vec4 camPos4 = uInverseViewProjection * vec4(0.0, 0.0, -1.0, 1.0);
            vec3 cameraPos = camPos4.xyz / camPos4.w;
            vec3 viewDir = normalize(cameraPos - vFragPos);
            
            if (uHasDirectionalLight == 1)
            {
                vec3 lightDir = normalize(uLightDir);
                
                // Diffuse
                float diff = max(dot(perturbedNormal, lightDir), 0.0);
                vec3 diffuse = diff * uLightColor;
                
                // Specular
                vec3 reflectDir = reflect(-lightDir, perturbedNormal);
                float spec = pow(max(dot(viewDir, reflectDir), 0.0), uSpecularPower);
                vec3 specular = spec * uLightColor * (uWaveStrength * 2.0 + 0.5);
                
                // Fake reflection / fresnel
                float fresnel = pow(1.0 - max(dot(viewDir, perturbedNormal), 0.0), 3.0);
                vec3 skyColor = vec3(0.5, 0.7, 0.9);
                
                // Fake sub-surface scattering color (cyan tint on waves)
                vec3 scatterColor = mix(albedo.rgb, vec3(0.2, 0.8, 0.9), fresnel * 0.5 * uWaveStrength);
                vec3 waterBase = mix(scatterColor, skyColor, fresnel * 0.6);
                
                vec3 ambient = uAmbientIntensity * uLightColor;
                vec3 finalColor = (ambient + diffuse) * waterBase + specular;
                
                fragColor = vec4(finalColor, albedo.a);
            }
            else
            {
                fragColor = albedo;
            }
        }
        """;

    private const string SkyboxVertexShaderSource =
        """
        #version 330 core
        
        out vec2 vUV;
        void main() 
        {
            float x = -1.0 + float((gl_VertexID & 1) << 2);
            float y = -1.0 + float((gl_VertexID & 2) << 1);
            vUV.x = (x+1.0)*0.5;
            vUV.y = (y+1.0)*0.5;
            gl_Position = vec4(x, y, 1.0, 1.0);
        }
        """;

    private const string SkyboxFragmentShaderSource =
        """
        #version 330 core
        
        in vec2 vUV;
        out vec4 fragColor;
        
        uniform mat4 uInverseViewProjection;
        uniform vec3 uLightDir;
        uniform vec3 uLightColor;
        
        void main()
        {
            vec4 ndcNear = vec4(vUV * 2.0 - 1.0, -1.0, 1.0);
            vec4 ndcFar  = vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
            
            vec4 nearPos = uInverseViewProjection * ndcNear;
            vec4 farPos  = uInverseViewProjection * ndcFar;
            nearPos.xyz /= nearPos.w;
            farPos.xyz  /= farPos.w;
            
            vec3 rayDir = normalize(farPos.xyz - nearPos.xyz);
            
            vec3 skyColorTop = vec3(0.1, 0.4, 0.8) * uLightColor;
            vec3 skyColorBottom = vec3(0.6, 0.8, 1.0) * uLightColor;
            vec3 groundColorDay = vec3(0.15, 0.15, 0.15) * uLightColor;
            
            vec3 nightSkyTop = vec3(0.01, 0.02, 0.05);
            vec3 nightSkyBottom = vec3(0.05, 0.05, 0.1);
            vec3 groundColorNight = vec3(0.01, 0.01, 0.01);
            
            float sunHeight = smoothstep(-0.2, 0.2, uLightDir.y);
            
            float skyGradient = smoothstep(0.0, 1.0, rayDir.y);
            vec3 daySky = mix(skyColorBottom, skyColorTop, skyGradient);
            vec3 nightSky = mix(nightSkyBottom, nightSkyTop, skyGradient);
            
            float groundMix = 1.0 - smoothstep(-0.05, 0.0, rayDir.y);
            vec3 dayColor = mix(daySky, groundColorDay, groundMix);
            vec3 nightColor = mix(nightSky, groundColorNight, groundMix);
            
            vec3 finalSky = mix(nightColor, dayColor, sunHeight);
            
            float sunDot = dot(rayDir, uLightDir);
            float sunGlow = smoothstep(0.95, 1.0, sunDot);
            float sunDisc = smoothstep(0.998, 1.0, sunDot);
            
            finalSky += uLightColor * sunGlow * 0.5 * sunHeight;
            finalSky += uLightColor * sunDisc * 2.0 * sunHeight;
            
            fragColor = vec4(finalSky, 1.0);
        }
        """;

    private const string CloudsVertexShaderSource =
        """
        #version 330 core
        
        out vec2 vUV;
        void main() 
        {
            float x = -1.0 + float((gl_VertexID & 1) << 2);
            float y = -1.0 + float((gl_VertexID & 2) << 1);
            vUV.x = (x+1.0)*0.5;
            vUV.y = (y+1.0)*0.5;
            gl_Position = vec4(x, y, 1.0, 1.0);
        }
        """;

    private const string CloudsFragmentShaderSource =
        """
        #version 330 core
        
        in vec2 vUV;
        out vec4 fragColor;
        
        uniform mat4 uInverseViewProjection;
        uniform vec3 uColorTop;
        uniform vec3 uColorBottom;
        uniform float uSpeed;
        uniform float uDensity;
        uniform float uHeight;
        uniform float uTime;
        uniform float uOpacity;
        uniform float uVolume;
        
        // Better noise without high frequency floating point breakdown
        float hash(vec2 p) {
            vec3 p3  = fract(vec3(p.xyx) * .1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }

        float noise(vec2 x) {
            vec2 i = floor(x);
            vec2 f = fract(x);
            
            float a = hash(i);
            float b = hash(i + vec2(1.0, 0.0));
            float c = hash(i + vec2(0.0, 1.0));
            float d = hash(i + vec2(1.0, 1.0));
            
            vec2 u = f * f * (3.0 - 2.0 * f);
            return mix(a, b, u.x) + (c - a)* u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
        }

        float fbm(vec2 x) {
            float v = 0.0;
            float a = 0.5;
            vec2 shift = vec2(100.0);
            mat2 rot = mat2(cos(0.5), sin(0.5), -sin(0.5), cos(0.50));
            for (int i = 0; i < 5; ++i) {
                v += a * noise(x);
                x = rot * x * 2.0 + shift;
                a *= 0.5;
            }
            return v;
        }

        void main()
        {
            vec4 ndcNear = vec4(vUV * 2.0 - 1.0, -1.0, 1.0);
            vec4 ndcFar  = vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
            
            vec4 nearPos = uInverseViewProjection * ndcNear;
            vec4 farPos  = uInverseViewProjection * ndcFar;
            nearPos.xyz /= nearPos.w;
            farPos.xyz  /= farPos.w;
            
            vec3 rayDir = normalize(farPos.xyz - nearPos.xyz);
            
            // Only draw clouds in the sky, above the horizon
            if (rayDir.y < 0.02) {
                discard;
            }

            // Map to a sky dome to avoid infinity / floating point precision breakdown at the horizon
            vec2 skyUV = rayDir.xz / (rayDir.y + 0.2);
            skyUV *= (max(uHeight, 0.1) * 3.0);
            
            // Apply time for movement (scaled down heavily so speed 1.0 is reasonable)
            skyUV += vec2(uTime * uSpeed * 0.02, uTime * uSpeed * 0.01);
            
            // Base cloud structure
            float n = fbm(skyUV * 2.0);
            
            // Subtract detail noise to carve out fluffy edges
            float detail = fbm(skyUV * 6.0 + uTime * uSpeed * 0.05);
            n = n - (1.0 - detail) * 0.3;
            
            // Calculate cloud coverage based on density parameter
            float coverage = uDensity * 1.5 - 0.2;
            float edgeSoftness = 0.2;
            float cloudMask = smoothstep(1.0 - coverage, 1.0 - coverage + edgeSoftness, n);
            
            // Add volumetric-like shading based on thickness and uVolume
            float localThickness = max(0.0, n - (1.0 - coverage));
            float shading = clamp(localThickness * (2.0 * max(uVolume, 0.1)), 0.0, 1.0);
            shading = pow(shading, 0.8); // Nice volumetric curve
            
            vec3 color = mix(uColorBottom, uColorTop, shading);
            
            // Smooth fade at the horizon
            float fade = smoothstep(0.02, 0.2, rayDir.y);
            
            fragColor = vec4(color, cloudMask * fade * uOpacity);
        }
        """;

    private const string GridVertexShaderSource =
        """
        #version 330 core
        
        out vec3 vNearPoint;
        out vec3 vFarPoint;

        uniform mat4 uInverseViewProjection;

        vec3 UnprojectPoint(float x, float y, float z) {
            vec4 unprojectedPoint = uInverseViewProjection * vec4(x, y, z, 1.0);
            return unprojectedPoint.xyz / unprojectedPoint.w;
        }

        void main() 
        {
            float x = -1.0 + float((gl_VertexID & 1) << 2);
            float y = -1.0 + float((gl_VertexID & 2) << 1);
            gl_Position = vec4(x, y, 0.0, 1.0);
            
            vNearPoint = UnprojectPoint(x, y, 0.0);
            vFarPoint = UnprojectPoint(x, y, 1.0);
        }
        """;

    private const string GridFragmentShaderSource =
        """
        #version 330 core
        
        in vec3 vNearPoint;
        in vec3 vFarPoint;
        out vec4 fragColor;

        uniform mat4 uViewProjection;
        uniform vec3 uCameraPos;

        vec4 grid(vec3 fragPos3D, float scale, bool drawAxis) {
            vec2 coord = fragPos3D.xz * scale;
            vec2 derivative = max(fwidth(coord), vec2(1e-5));
            vec2 grid = abs(fract(coord - 0.5) - 0.5) / derivative;
            float line = min(grid.x, grid.y);
            vec4 color = vec4(0.3, 0.3, 0.3, 1.0 - min(line, 1.0));
            
            if (drawAxis) {
                // z axis (blue)
                float zAxis = abs(coord.x) / derivative.x;
                if (zAxis < 1.0) {
                    color.xyz = mix(vec3(0.0, 0.0, 1.0), color.xyz, zAxis);
                }
                // x axis (red)
                float xAxis = abs(coord.y) / derivative.y;
                if (xAxis < 1.0) {
                    color.xyz = mix(vec3(1.0, 0.0, 0.0), color.xyz, xAxis);
                }
            }
            return color;
        }

        void main() {
            float t = -vNearPoint.y / (vFarPoint.y - vNearPoint.y);
            if (t < 0.0) discard;

            vec3 fragPos3D = vNearPoint + t * (vFarPoint - vNearPoint);
            
            vec4 clip_space_pos = uViewProjection * vec4(fragPos3D, 1.0);
            float clip_depth = clip_space_pos.z / clip_space_pos.w;
            gl_FragDepth = clip_depth * 0.5 + 0.5;

            // distance from camera for fading
            float distance = length(fragPos3D - uCameraPos);
            // Height based LOD
            float height = max(abs(uCameraPos.y), 1.0);
            
            // Fading at the horizon
            float fadeEnd = height * 20.0;
            float fadeStart = height * 5.0;
            float fading = 1.0 - smoothstep(fadeStart, fadeEnd, distance);
            
            // Grid LOD (power of 10)
            float logHeight = log(height * 0.2) / log(10.0);
            float lod = floor(logHeight);
            float lodFade = fract(logHeight); // 0.0 (near) to 1.0 (far)
            
            float scale0 = 1.0 / pow(10.0, lod);
            float scale1 = 1.0 / pow(10.0, lod + 1.0);
            float scale2 = 1.0 / pow(10.0, lod + 2.0);
            
            vec4 grid0 = grid(fragPos3D, scale0, true);
            vec4 grid1 = grid(fragPos3D, scale1, true);
            vec4 grid2 = grid(fragPos3D, scale2, true);
            
            // grid2 is the most coarse, grid0 is the finest.
            // As we go higher, grid0 fades out (multiplied by 1.0 - lodFade)
            grid0.a *= (1.0 - lodFade);
            
            vec4 c = grid0;
            c = mix(c, grid1, grid1.a);
            c = mix(c, grid2, grid2.a);

            fragColor = c;
            fragColor.a *= fading;
            if (fragColor.a <= 0.0) discard;
        }
        """;

    private static Shader? s_shader;
    private static Shader? s_waterShader;
    private static Shader? s_skyboxShader;
    private static Shader? s_cloudsShader;
    private static Shader? s_gridShader;
    private static VertexArray? s_emptyVao;
    private static Texture2D? s_whiteTexture;
    private static Matrix4x4 s_viewProjection = Matrix4x4.Identity;

    private static int s_hasDirLight = 0;
    private static Vector3 s_lightDir = Vector3.UnitY;
    private static Vector3 s_lightColor = Vector3.One;
    private static float s_ambientIntensity = 0.3f;

    /// <summary>
    /// Creates the shared shader and fallback texture. Called once by the application after the renderer is ready.
    /// </summary>
    public static void Init()
    {
        s_shader = new Shader(VertexShaderSource, FragmentShaderSource);
        s_waterShader = new Shader(WaterVertexShaderSource, WaterFragmentShaderSource);
        s_skyboxShader = new Shader(SkyboxVertexShaderSource, SkyboxFragmentShaderSource);
        s_cloudsShader = new Shader(CloudsVertexShaderSource, CloudsFragmentShaderSource);
        s_gridShader = new Shader(GridVertexShaderSource, GridFragmentShaderSource);
        s_emptyVao = new VertexArray();

        // A 1x1 white texture lets untextured (solid-color) meshes reuse the textured path: texture * color == color.
        ReadOnlySpan<byte> white = stackalloc byte[] { 255, 255, 255, 255 };
        s_whiteTexture = new Texture2D(1, 1, white);
    }

    /// <summary>
    /// Begins a 3D scene. Meshes drawn until <see cref="EndScene"/> use this view-projection.
    /// </summary>
    public static void BeginScene(Matrix4x4 viewProjection, bool hasLight = false, Vector3 lightDir = default, Vector3 lightColor = default, float ambientIntensity = 0.3f)
    {
        s_viewProjection = viewProjection;
        s_hasDirLight = hasLight ? 1 : 0;
        s_lightDir = hasLight ? lightDir : Vector3.UnitY;
        s_lightColor = hasLight ? lightColor : Vector3.One;
        s_ambientIntensity = ambientIntensity;
    }

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

        (texture ?? s_whiteTexture).Bind(0);

        activeShader.Use();
        activeShader.SetUniform("uViewProjection", s_viewProjection);
        activeShader.SetUniform("uModel", model);
        activeShader.SetUniform("uColor", color);
        activeShader.SetUniform("uTexture", 0);
        
        if (shaderType == 1) // Water
        {
            activeShader.SetUniform("uTime", Spot.Core.Application.Instance.Time);
            Matrix4x4.Invert(s_viewProjection, out Matrix4x4 invViewProj);
            activeShader.SetUniform("uInverseViewProjection", invViewProj);
            
            float speed = material?.WaveSpeed ?? 1.0f;
            float scale = material?.WaveScale ?? 1.0f;
            float strength = material?.WaveStrength ?? 0.3f;
            float specPower = material?.SpecularPower ?? 64.0f;
            
            activeShader.SetUniform("uWaveSpeed", speed);
            activeShader.SetUniform("uWaveScale", scale);
            activeShader.SetUniform("uWaveStrength", strength);
            activeShader.SetUniform("uSpecularPower", specPower);
        }

        activeShader.SetUniform("uHasDirectionalLight", s_hasDirLight);
        if (s_hasDirLight == 1)
        {
            activeShader.SetUniform("uLightDir", s_lightDir);
            activeShader.SetUniform("uLightColor", s_lightColor);
            activeShader.SetUniform("uAmbientIntensity", s_ambientIntensity);
        }

        Renderer.DrawIndexed(mesh.VertexArray, mesh.IndexCount);
    }

    /// <summary>
    /// Ends the current 3D scene. Present for symmetry with <see cref="Renderer2D"/>; currently a no-op.
    /// </summary>
    public static void EndScene()
    {
    }

    /// <summary>
    /// Draws the procedural skybox based on the directional light.
    /// </summary>
    public static void DrawSkybox()
    {
        if (s_skyboxShader == null || s_emptyVao == null || s_hasDirLight == 0) return;

        Matrix4x4.Invert(s_viewProjection, out Matrix4x4 invViewProj);
        s_skyboxShader.Use();
        s_skyboxShader.SetUniform("uInverseViewProjection", invViewProj);
        s_skyboxShader.SetUniform("uLightDir", s_lightDir);
        s_skyboxShader.SetUniform("uLightColor", s_lightColor);

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

        Matrix4x4.Invert(s_viewProjection, out Matrix4x4 invViewProj);
        s_cloudsShader.Use();
        s_cloudsShader.SetUniform("uInverseViewProjection", invViewProj);
        s_cloudsShader.SetUniform("uColorTop", new Vector3(colorTopX, colorTopY, colorTopZ));
        s_cloudsShader.SetUniform("uColorBottom", new Vector3(colorBotX, colorBotY, colorBotZ));
        s_cloudsShader.SetUniform("uSpeed", speed);
        s_cloudsShader.SetUniform("uDensity", density);
        s_cloudsShader.SetUniform("uHeight", height);
        s_cloudsShader.SetUniform("uOpacity", opacity);
        s_cloudsShader.SetUniform("uVolume", volume);
        s_cloudsShader.SetUniform("uTime", time);

        Renderer.Api.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        Renderer.Api.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);

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

        Matrix4x4.Invert(s_viewProjection, out Matrix4x4 invViewProj);
        s_gridShader.Use();
        s_gridShader.SetUniform("uViewProjection", s_viewProjection);
        s_gridShader.SetUniform("uInverseViewProjection", invViewProj);
        s_gridShader.SetUniform("uCameraPos", cameraPos);

        Renderer.Api.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        Renderer.Api.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);

        Renderer.DrawArrays(s_emptyVao, 3);
        
        // Editor will reset or disable blending later if needed, but standard UI and transparent sprites need it too.
    }
}

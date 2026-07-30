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
            vec4 ndc = vec4(vUV * 2.0 - 1.0, 1.0, 1.0);
            vec4 worldPos = uInverseViewProjection * ndc;
            vec3 rayDir = normalize(worldPos.xyz / worldPos.w);
            
            vec3 skyColorTop = vec3(0.1, 0.4, 0.8) * uLightColor;
            vec3 skyColorBottom = vec3(0.6, 0.8, 1.0) * uLightColor;
            
            vec3 nightSkyTop = vec3(0.01, 0.02, 0.05);
            vec3 nightSkyBottom = vec3(0.05, 0.05, 0.1);
            
            float sunHeight = smoothstep(-0.2, 0.2, uLightDir.y);
            
            float gradient = smoothstep(-0.2, 1.0, rayDir.y);
            vec3 dayColor = mix(skyColorBottom, skyColorTop, gradient);
            vec3 nightColor = mix(nightSkyBottom, nightSkyTop, gradient);
            
            vec3 finalSky = mix(nightColor, dayColor, sunHeight);
            
            float sunDot = dot(rayDir, uLightDir);
            float sunGlow = smoothstep(0.95, 1.0, sunDot);
            float sunDisc = smoothstep(0.998, 1.0, sunDot);
            
            finalSky += uLightColor * sunGlow * 0.5 * sunHeight;
            finalSky += uLightColor * sunDisc * 2.0 * sunHeight;
            
            fragColor = vec4(finalSky, 1.0);
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
            vec2 derivative = fwidth(coord);
            vec2 grid = abs(fract(coord - 0.5) - 0.5) / derivative;
            float line = min(grid.x, grid.y);
            float minimumz = min(derivative.y, 1.0) / scale;
            float minimumx = min(derivative.x, 1.0) / scale;
            vec4 color = vec4(0.3, 0.3, 0.3, 1.0 - min(line, 1.0));
            
            if (drawAxis) {
                // z axis
                if(fragPos3D.x > -0.1 * minimumx && fragPos3D.x < 0.1 * minimumx)
                    color.xyz = vec3(0.0, 0.0, 1.0);
                // x axis
                if(fragPos3D.z > -0.1 * minimumz && fragPos3D.z < 0.1 * minimumz)
                    color.xyz = vec3(1.0, 0.0, 0.0);
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
    private static Shader? s_skyboxShader;
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
    internal static void Init()
    {
        s_shader = new Shader(VertexShaderSource, FragmentShaderSource);
        s_skyboxShader = new Shader(SkyboxVertexShaderSource, SkyboxFragmentShaderSource);
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
    public static void DrawMesh(Matrix4x4 model, Mesh mesh, Vector4 color, Texture2D? texture = null)
    {
        if (s_shader is null || s_whiteTexture is null)
        {
            return;
        }

        (texture ?? s_whiteTexture).Bind(0);

        s_shader.Use();
        s_shader.SetUniform("uViewProjection", s_viewProjection);
        s_shader.SetUniform("uModel", model);
        s_shader.SetUniform("uColor", color);
        s_shader.SetUniform("uTexture", 0);

        s_shader.SetUniform("uHasDirectionalLight", s_hasDirLight);
        if (s_hasDirLight == 1)
        {
            s_shader.SetUniform("uLightDir", s_lightDir);
            s_shader.SetUniform("uLightColor", s_lightColor);
            s_shader.SetUniform("uAmbientIntensity", s_ambientIntensity);
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

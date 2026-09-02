namespace Spot.Rendering;

// GLSL shader program sources for Renderer3D, split out of Renderer3D.cs to keep the renderer
// logic readable. This partial holds only the embedded shader text; the C# rendering logic and
// public API (MaxBones) live in Renderer3D.cs.
public static partial class Renderer3D
{
    private const string VertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;

        uniform mat4 uViewProjection;
        uniform mat4 uModel;
        uniform mat4 uLightSpaceMatrix;
        uniform vec4 uColor;
        uniform vec3 uModelScale;

        out vec3 vFragPos;
        out vec4 vFragPosLightSpace;
        out vec3 vNormal;
        out vec2 vTexCoord;
        out vec4 vColor;
        out vec3 vModelScale;

        void main()
        {
            vec4 worldPos = uModel * vec4(aPosition, 1.0);
            vFragPos = worldPos.xyz;
            vFragPosLightSpace = uLightSpaceMatrix * worldPos;

            vNormal = mat3(uModel) * aNormal;
            vTexCoord = aTexCoord;
            vColor = uColor;
            vModelScale = uModelScale;
            gl_Position = uViewProjection * worldPos;
        }
        """;

    // Instanced counterpart of the standard vertex shader: the model matrix and color come from
    // per-instance vertex attributes (locations 3-6 for the mat4, 7 for the color) instead of uniforms,
    // so a whole batch of identical meshes draws in one call. Model scale for auto-tiling is derived
    // from the instance matrix's basis vectors. Feeds the same FragmentShaderSource as the others.
    private const string InstancedVertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;
        layout (location = 3) in mat4 iModel;   // occupies locations 3,4,5,6
        layout (location = 7) in vec4 iColor;

        uniform mat4 uViewProjection;
        uniform mat4 uLightSpaceMatrix;

        out vec3 vFragPos;
        out vec4 vFragPosLightSpace;
        out vec3 vNormal;
        out vec2 vTexCoord;
        out vec4 vColor;
        out vec3 vModelScale;

        void main()
        {
            vec4 worldPos = iModel * vec4(aPosition, 1.0);
            vFragPos = worldPos.xyz;
            vFragPosLightSpace = uLightSpaceMatrix * worldPos;

            vNormal = mat3(iModel) * aNormal;
            vTexCoord = aTexCoord;
            vColor = iColor;
            vModelScale = vec3(length(iModel[0].xyz), length(iModel[1].xyz), length(iModel[2].xyz));
            gl_Position = uViewProjection * worldPos;
        }
        """;

    private const string FragmentShaderSource =
        """
        #version 330 core
        in vec3 vFragPos;
        in vec4 vFragPosLightSpace;
        in vec3 vNormal;
        in vec2 vTexCoord;
        // Color and model scale arrive as varyings (set by the vertex stage from either a uniform or
        // per-instance attributes), so the same fragment shader serves the standard, skinned, and
        // instanced draw paths.
        in vec4 vColor;
        in vec3 vModelScale;

        uniform sampler2D uTexture;
        uniform sampler2D uNormalMap;
        uniform sampler2DShadow uShadowMap;
        uniform mat4 uLightSpaceMatrix;
        uniform float uShadowTexelSize;

        uniform int uHasNormalMap;
        uniform float uMetallic;
        uniform vec3 uEmissiveColor;
        uniform float uEmissiveIntensity;
        uniform vec3 uCameraPos;

        uniform vec2 uTiling;
        uniform int uAutoTile;

        uniform int uHasDirectionalLight;
        uniform int uCastShadows;
        uniform vec3 uLightDir;
        uniform vec3 uLightColor;
        uniform float uAmbientIntensity;

        // Point lights live in a std140 uniform block so the count scales far past the old fixed four.
        // Each light is two vec4s (position+range, color+intensity) to sidestep std140's vec3 padding.
        const int MAX_LIGHTS = 256;
        struct PointLight {
            vec4 positionRange;
            vec4 colorIntensity;
        };
        layout(std140) uniform Lights {
            PointLight uLights[MAX_LIGHTS];
        };
        uniform int uPointLightCount;

        // Clustered forward lighting: when uClustered is 1, a fragment loops only the lights assigned to its
        // froxel via two integer lookup textures (grid: packed offset<<8|count per froxel; indices: the flat
        // per-froxel light lists). Grid dimensions must match LightClusters.cs.
        uniform int uClustered;
        uniform float uNear;
        uniform float uFar;
        uniform vec2 uScreenSize;
        uniform highp usampler2D uClusterGrid;
        uniform highp usampler2D uLightIndices;
        const int CLUSTERS_X = 16;
        const int CLUSTERS_Y = 9;
        const int CLUSTERS_Z = 24;
        const int GRID_TEX_W = 64;
        const int INDEX_TEX_W = 256;

        out vec4 fragColor;

        // Normal-offset shadows: instead of a depth bias measured in the light's NDC z (which balloons
        // into meters of "peter-panning" gap once the shadow frustum is deep), push the sampled point off
        // the surface along its normal by a couple of shadow texels' worth of world space, widened at
        // grazing light angles where acne is worst. This is scale-stable and keeps the shadow glued to
        // the object's contact point. Sampling is hardware PCF (sampler2DShadow) over a 5x5 kernel.
        float ShadowCalculation(vec3 worldPos, vec3 N, vec3 L)
        {
            float slope = clamp(1.0 - dot(N, L), 0.0, 1.0);
            vec3 offsetPos = worldPos + N * uShadowTexelSize * (1.5 + 3.0 * slope);
            vec4 lp = uLightSpaceMatrix * vec4(offsetPos, 1.0);

            vec3 projCoords = lp.xyz / lp.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0) return 0.0;
            // Outside the shadow map's xy extent reads as fully lit. On desktop the depth texture's
            // clamp-to-border handled this; WebGL2 has no border, so test the bounds explicitly (harmless
            // on desktop too).
            if (projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0) return 0.0;

            float depthRef = projCoords.z - 0.0015; // tiny residual constant bias

            float shadow = 0.0;
            vec2 texelSize = 1.0 / vec2(textureSize(uShadowMap, 0));
            for (int x = -2; x <= 2; ++x)
            {
                for (int y = -2; y <= 2; ++y)
                {
                    // sampler2DShadow returns filtered visibility in [0,1] (1 = lit); accumulate occlusion.
                    shadow += 1.0 - texture(uShadowMap, vec3(projCoords.xy + vec2(x, y) * texelSize, depthRef));
                }
            }
            return shadow / 25.0;
        }

        vec3 getNormalFromMap(vec2 uv) {
            vec3 tangentNormal = texture(uNormalMap, uv).xyz * 2.0 - 1.0;

            vec3 Q1  = dFdx(vFragPos);
            vec3 Q2  = dFdy(vFragPos);
            vec2 st1 = dFdx(uv);
            vec2 st2 = dFdy(uv);

            vec3 N   = normalize(vNormal);
            vec3 T  = normalize(Q1*st2.t - Q2*st1.t);
            vec3 B  = -normalize(cross(N, T));
            mat3 TBN = mat3(T, B, N);

            return normalize(TBN * tangentNormal);
        }

        // One point light's Blinn-Phong contribution, shared by the brute-force and clustered loops.
        vec3 pointLightContribution(int i, vec3 normal, vec3 viewDir, vec3 F0)
        {
            vec3 lightPos = uLights[i].positionRange.xyz;
            float lightRange = uLights[i].positionRange.w;
            vec3 lightCol = uLights[i].colorIntensity.rgb;
            float lightInt = uLights[i].colorIntensity.a;

            vec3 lightDir = lightPos - vFragPos;
            float distance = length(lightDir);
            if (distance >= lightRange) return vec3(0.0);

            lightDir = normalize(lightDir);
            vec3 halfVector = normalize(lightDir + viewDir);
            float diff = max(dot(normal, lightDir), 0.0);
            float spec = pow(max(dot(normal, halfVector), 0.0), mix(16.0, 128.0, uMetallic));
            vec3 specular = lightCol * spec * F0;
            float attenuation = 1.0 - (distance / lightRange);
            attenuation = attenuation * attenuation;
            return (lightCol * diff + specular) * lightInt * attenuation;
        }

        // The froxel this fragment falls in: screen tile in x/y, exponential radial-distance slice in z.
        int froxelIndex()
        {
            float dist = length(vFragPos - uCameraPos);
            float zf = log(max(dist, uNear) / uNear) / log(uFar / uNear);
            int zSlice = clamp(int(zf * float(CLUSTERS_Z)), 0, CLUSTERS_Z - 1);
            vec2 tileSize = uScreenSize / vec2(float(CLUSTERS_X), float(CLUSTERS_Y));
            ivec2 tile = clamp(ivec2(gl_FragCoord.xy / tileSize), ivec2(0), ivec2(CLUSTERS_X - 1, CLUSTERS_Y - 1));
            return tile.x + tile.y * CLUSTERS_X + zSlice * CLUSTERS_X * CLUSTERS_Y;
        }

        // Sums the point lights affecting this fragment — its froxel's list when clustered, else all of them.
        vec3 accumulatePointLights(vec3 normal, vec3 viewDir, vec3 F0)
        {
            vec3 sum = vec3(0.0);
            if (uClustered == 1)
            {
                int fro = froxelIndex();
                uint packed = texelFetch(uClusterGrid, ivec2(fro % GRID_TEX_W, fro / GRID_TEX_W), 0).r;
                uint offset = packed >> 8u;
                uint count = packed & 255u;
                for (uint k = 0u; k < count; k++)
                {
                    uint li = offset + k;
                    int i = int(texelFetch(uLightIndices, ivec2(int(li) % INDEX_TEX_W, int(li) / INDEX_TEX_W), 0).r);
                    sum += pointLightContribution(i, normal, viewDir, F0);
                }
            }
            else
            {
                for (int i = 0; i < uPointLightCount; i++)
                {
                    sum += pointLightContribution(i, normal, viewDir, F0);
                }
            }
            return sum;
        }

        void main()
        {
            vec2 scale2D = vec2(1.0);
            if (uAutoTile == 1) {
                vec3 n = abs(normalize(vNormal));
                if (n.x > n.y && n.x > n.z) scale2D = vModelScale.zy;
                else if (n.y > n.x && n.y > n.z) scale2D = vModelScale.xz;
                else scale2D = vModelScale.xy;
            }
            vec2 finalUV = vTexCoord * uTiling * scale2D;

            vec4 albedo = texture(uTexture, finalUV) * vColor;
            vec3 normal = uHasNormalMap == 1 ? getNormalFromMap(finalUV) : normalize(vNormal);
            
            vec3 viewDir = normalize(uCameraPos - vFragPos);

            vec3 F0 = vec3(0.04);
            F0 = mix(F0, albedo.rgb, uMetallic);
            
            vec3 lighting = vec3(0.0);
            
            if (uHasDirectionalLight == 1)
            {
                vec3 lightDir = normalize(uLightDir);
                vec3 halfVector = normalize(lightDir + viewDir);
                
                float diffuse = max(dot(normal, lightDir), 0.0);
                float spec = pow(max(dot(normal, halfVector), 0.0), mix(16.0, 128.0, uMetallic));
                vec3 specular = uLightColor * spec * F0;
                
                float shadow = uCastShadows == 1 ? ShadowCalculation(vFragPos, normalize(vNormal), lightDir) : 0.0;
                lighting += (uAmbientIntensity + (1.0 - shadow) * diffuse) * uLightColor + (1.0 - shadow) * specular;
            }
            else
            {
                lighting += uAmbientIntensity * uLightColor;
            }
            
            lighting += accumulatePointLights(normal, viewDir, F0);
            
            vec3 emissive = uEmissiveColor * uEmissiveIntensity;

            if (uHasDirectionalLight == 0 && uPointLightCount == 0)
            {
                fragColor = vec4(albedo.rgb + emissive, albedo.a); // Unlit
            }
            else
            {
                fragColor = vec4(albedo.rgb * lighting + emissive, albedo.a);
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
        uniform mat4 uLightSpaceMatrix;
        uniform float uTime;
        uniform float uWaveSpeed;
        uniform float uWaveScale;
        uniform float uWaveStrength;

        out vec3 vFragPos;
        out vec4 vFragPosLightSpace;
        out vec3 vNormal;
        out vec2 vTexCoord;

        void main()
        {
            vec4 worldPos = uModel * vec4(aPosition, 1.0);
            vec3 worldNormal = normalize(mat3(transpose(inverse(uModel))) * aNormal);

            // Large, low-frequency swell computed in WORLD space, so the wavelength is the same
            // absolute size whether this mesh is a 1-unit puddle or a 500-unit ocean (the old code
            // displaced by *local* position, so identical geometry rippled differently once scaled).
            // Kept gentle: the plane primitive is only lightly tessellated, and the fine detail is
            // carried by the fragment normal, so the geometry just needs a soft undulating silhouette.
            if (worldNormal.y > 0.5) {
                float t = uTime * uWaveSpeed;
                float f = 0.06 * max(uWaveScale, 0.001);
                float swell = sin(worldPos.x * f + t) * 0.6
                            + sin((worldPos.x + worldPos.z) * f * 0.7 - t * 0.8) * 0.4
                            + cos(worldPos.z * f * 1.3 + t * 1.1) * 0.5;
                worldPos.y += swell * uWaveStrength;
            }

            vFragPos = worldPos.xyz;
            vFragPosLightSpace = uLightSpaceMatrix * worldPos;
            vNormal = worldNormal;
            vTexCoord = aTexCoord;
            gl_Position = uViewProjection * worldPos;
        }
        """;

    private const string WaterFragmentShaderSource =
        """
        #version 330 core
        in vec3 vFragPos;
        in vec4 vFragPosLightSpace;
        in vec3 vNormal;
        in vec2 vTexCoord;

        uniform vec4 uColor;
        uniform sampler2D uTexture;
        uniform sampler2DShadow uShadowMap;
        uniform mat4 uLightSpaceMatrix;
        uniform float uShadowTexelSize;
        uniform float uTime;
        uniform vec3 uCameraPos;

        uniform float uWaveSpeed;
        uniform float uWaveScale;
        uniform float uWaveStrength;
        uniform float uSpecularPower;
        
        uniform vec2 uTiling;
        uniform int uAutoTile;
        uniform vec3 uModelScale;

        uniform int uHasDirectionalLight;
        uniform int uCastShadows;
        uniform vec3 uLightDir;
        uniform vec3 uLightColor;
        uniform float uAmbientIntensity;

        // Shared std140 point-light block (see the standard shader). Two vec4s per light.
        const int MAX_LIGHTS = 256;
        struct PointLight {
            vec4 positionRange;
            vec4 colorIntensity;
        };
        layout(std140) uniform Lights {
            PointLight uLights[MAX_LIGHTS];
        };
        uniform int uPointLightCount;

        // Sky colours of the active procedural skybox, so the water reflects the same sky the scene
        // shows. uHasSkybox is 0 when the scene has no skybox, in which case a neutral fallback is used.
        uniform vec3 uSkyColor;
        uniform vec3 uGroundColor;
        uniform int uHasSkybox;

        out vec4 fragColor;

        float hash(vec2 p) {
            vec3 p3  = fract(vec3(p.xyx) * .1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }

        // Value noise carrying its analytic derivative: returns (value in [-1,1], d/dx, d/dy). The
        // derivative lets us build an exact surface normal from the summed height field instead of
        // sampling noise twice and hoping — smoother, and cheaper per octave.
        vec3 noised(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u  = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
            vec2 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);
            float a = hash(i + vec2(0.0, 0.0)) * 2.0 - 1.0;
            float b = hash(i + vec2(1.0, 0.0)) * 2.0 - 1.0;
            float c = hash(i + vec2(0.0, 1.0)) * 2.0 - 1.0;
            float d = hash(i + vec2(1.0, 1.0)) * 2.0 - 1.0;
            float k1 = b - a;
            float k2 = c - a;
            float k3 = a - b - c + d;
            float value = a + k1 * u.x + k2 * u.y + k3 * u.x * u.y;
            vec2 deriv = du * (vec2(k1, k2) + k3 * vec2(u.y, u.x));
            return vec3(value, deriv);
        }

        // Analytic sky colour for a reflected ray, rebuilding the same haze -> sky -> zenith gradient
        // the procedural skybox uses (plus a reflected-sun disc), so a mirror-like grazing reflection
        // reads as the real sky rather than a flat blue.
        vec3 sampleSky(vec3 dir) {
            vec3 skyC = uHasSkybox == 1 ? uSkyColor : vec3(0.55, 0.75, 1.0);
            vec3 grdC = uHasSkybox == 1 ? uGroundColor : vec3(0.35, 0.37, 0.4);
            float lum = dot(skyC, vec3(0.2126, 0.7152, 0.0722));
            vec3 zenith = skyC * skyC;
            vec3 haze = mix(skyC, vec3(lum), 0.35);
            haze = mix(haze, vec3(0.82, 0.88, 0.95), 0.4);
            float up = clamp(dir.y, 0.0, 1.0);
            vec3 col = mix(haze, skyC, smoothstep(0.0, 0.25, up));
            col = mix(col, zenith, smoothstep(0.18, 0.9, up));
            if (dir.y < 0.0) col = mix(haze, grdC, clamp(-dir.y * 3.0, 0.0, 1.0));
            if (uHasDirectionalLight == 1) {
                float sd = max(dot(dir, normalize(uLightDir)), 0.0);
                col += uLightColor * pow(sd, 900.0) * 7.0;  // reflected sun disc (blooms into sparkle)
                col += uLightColor * pow(sd, 40.0) * 0.25;  // soft forward-scatter glow
            }
            return col;
        }

        // See the standard shader for the rationale: normal-offset receiver + hardware PCF (sampler2DShadow).
        float ShadowCalculation(vec3 worldPos, vec3 N, vec3 L)
        {
            float slope = clamp(1.0 - dot(N, L), 0.0, 1.0);
            vec3 offsetPos = worldPos + N * uShadowTexelSize * (1.5 + 3.0 * slope);
            vec4 lp = uLightSpaceMatrix * vec4(offsetPos, 1.0);

            vec3 projCoords = lp.xyz / lp.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0) return 0.0;
            // Outside the shadow map's xy extent reads as fully lit. On desktop the depth texture's
            // clamp-to-border handled this; WebGL2 has no border, so test the bounds explicitly (harmless
            // on desktop too).
            if (projCoords.x < 0.0 || projCoords.x > 1.0 || projCoords.y < 0.0 || projCoords.y > 1.0) return 0.0;

            float depthRef = projCoords.z - 0.0015;

            float shadow = 0.0;
            vec2 texelSize = 1.0 / vec2(textureSize(uShadowMap, 0));
            for (int x = -2; x <= 2; ++x)
            {
                for (int y = -2; y <= 2; ++y)
                {
                    shadow += 1.0 - texture(uShadowMap, vec3(projCoords.xy + vec2(x, y) * texelSize, depthRef));
                }
            }
            return shadow / 25.0;
        }

        void main()
        {
            // ---- Scale-independent wave normal ------------------------------------------------
            // The height field is summed in WORLD space from octaves spanning a wide band of absolute
            // wavelengths (big swell -> fine ripple). Because it's world-space, the SAME material reads
            // as ripples on a small puddle and as ocean swell on a huge plane. Octaves whose wavelength
            // falls below a couple of screen pixels are faded out (analytic LOD via fwidth), so a
            // distant ocean stays crisp instead of shimmering with noise aliasing.
            vec2 world = vFragPos.xz;
            float texel = max(length(fwidth(world)), 1e-5);
            float t = uTime * uWaveSpeed;

            const int OCTAVES = 6;
            float baseFreq = 0.05 * max(uWaveScale, 0.001);
            mat2 rot = mat2(0.80, -0.60, 0.60, 0.80); // rotate each octave's domain to hide grid alignment

            vec2 grad = vec2(0.0);
            float amp = 1.0;
            float freq = baseFreq;
            float ampSum = 0.0;
            vec2 p = world;
            vec2 dir = vec2(1.0, 0.35); // per-octave scroll direction, rotated alongside the domain
            for (int i = 0; i < OCTAVES; i++) {
                float wavelength = 1.0 / freq;
                float lod = smoothstep(1.5, 3.5, wavelength / texel);
                vec3 n = noised(p * freq + dir * t);
                grad += amp * freq * n.yz * lod;
                ampSum += amp;
                p = rot * p;
                dir = rot * dir;
                amp *= 0.5;
                freq *= 2.0;
            }
            grad /= max(ampSum, 1e-4);

            // Surface normal from the height gradient (water's up is world +Y). Bias toward the actual
            // geometric normal so a non-horizontal water surface still shades sensibly.
            vec3 geoN = normalize(vNormal);
            float bump = uWaveStrength * 6.0;
            vec3 N = normalize(vec3(-grad.x * bump, 1.0, -grad.y * bump));
            N = normalize(mix(geoN, N, clamp(geoN.y, 0.0, 1.0)));

            vec3 viewDir = normalize(uCameraPos - vFragPos);
            if (dot(N, viewDir) < 0.0) N = -N;

            // ---- Base water body colour -------------------------------------------------------
            vec2 scale2D = vec2(1.0);
            if (uAutoTile == 1) {
                vec3 an = abs(geoN);
                if (an.x > an.y && an.x > an.z) scale2D = uModelScale.zy;
                else if (an.y > an.x && an.y > an.z) scale2D = uModelScale.xz;
                else scale2D = uModelScale.xy;
            }
            vec4 texColor = texture(uTexture, vTexCoord * uTiling * scale2D);
            vec3 deep = (texColor * uColor).rgb;
            // Shallow water reads lighter and greener; look straight down and you see the deep tint,
            // look across the surface and it lifts toward the shallow tint.
            vec3 shallow = mix(deep, deep * vec3(1.6, 2.0, 1.9) + vec3(0.0, 0.05, 0.06), 0.6);
            float depthT = pow(clamp(dot(viewDir, geoN), 0.0, 1.0), 0.6);
            vec3 bodyColor = mix(shallow, deep, depthT);

            // ---- Reflection + Fresnel ---------------------------------------------------------
            vec3 reflDir = reflect(-viewDir, N);
            reflDir.y = abs(reflDir.y); // never sample below the world through a steep wave facet
            vec3 reflection = sampleSky(reflDir);
            // Schlick Fresnel with water's real F0 (~0.02): refractive body colour head-on, mirror at grazing.
            float fres = 0.02 + 0.98 * pow(1.0 - max(dot(N, viewDir), 0.0), 5.0);

            // ---- Lighting on the body ---------------------------------------------------------
            vec3 ambient = uAmbientIntensity * (uHasSkybox == 1 ? mix(uLightColor, uSkyColor, 0.5) : uLightColor);
            vec3 lit = bodyColor * ambient;
            vec3 specularSum = vec3(0.0);

            if (uHasDirectionalLight == 1)
            {
                vec3 L = normalize(uLightDir);
                float shadow = uCastShadows == 1 ? ShadowCalculation(vFragPos, N, L) : 0.0;
                float diff = max(dot(N, L), 0.0);
                lit += bodyColor * diff * uLightColor * (1.0 - shadow);

                // Sub-surface scattering: wave crests backlit by the sun glow a warm green.
                float sss = pow(max(dot(viewDir, -L), 0.0), 3.0) * clamp(grad.x * 0.5 + 0.5, 0.0, 1.0);
                lit += uLightColor * vec3(0.15, 0.4, 0.35) * sss * (1.0 - shadow);

                // Sharp Blinn-Phong sun glint (HDR, so it blooms into a sparkle highlight).
                vec3 H = normalize(L + viewDir);
                float spec = pow(max(dot(N, H), 0.0), uSpecularPower);
                specularSum += spec * uLightColor * (1.0 - shadow) * 2.5;
            }

            for (int i = 0; i < uPointLightCount; i++)
            {
                vec3 lightPos = uLights[i].positionRange.xyz;
                float lightRange = uLights[i].positionRange.w;
                vec3 lightCol = uLights[i].colorIntensity.rgb;
                float lightInt = uLights[i].colorIntensity.a;

                vec3 Lv = lightPos - vFragPos;
                float dist = length(Lv);
                if (dist < lightRange)
                {
                    vec3 L = Lv / max(dist, 1e-4);
                    float atten = 1.0 - (dist / lightRange);
                    atten *= atten;
                    float diff = max(dot(N, L), 0.0);
                    lit += bodyColor * lightCol * lightInt * diff * atten;
                    vec3 H = normalize(L + viewDir);
                    float spec = pow(max(dot(N, H), 0.0), uSpecularPower);
                    specularSum += spec * lightCol * lightInt * atten * 2.0;
                }
            }

            // ---- Composite --------------------------------------------------------------------
            vec3 color;
            if (uHasDirectionalLight == 0 && uPointLightCount == 0)
            {
                // Unlit scene: still layer the sky reflection over the body so water never reads as flat paint.
                color = mix(bodyColor, reflection, fres);
            }
            else
            {
                color = mix(lit, reflection, fres) + specularSum;
            }

            fragColor = vec4(color, uColor.a);
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
        
        uniform vec3 uSkyColor;
        uniform vec3 uGroundColor;
        
        uniform vec3 uLightDir;
        uniform vec3 uLightColor;
        uniform int uHasDirLight;

        // Cheap, sine-free per-pixel hash in [0,1). Used for dithering.
        float hash12(vec2 p)
        {
            vec3 p3 = fract(vec3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
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
            float y = rayDir.y;

            // Derive a small atmospheric palette from the single picked sky color, so the sky reads as a
            // gradient instead of one flat, cartoon-looking tint.
            float lum = dot(uSkyColor, vec3(0.2126, 0.7152, 0.0722));
            // Zenith: deeper and more saturated than the picked color (squaring deepens + saturates).
            vec3 zenith = uSkyColor * uSkyColor;
            // Horizon haze: pale, desaturated and lifted toward a bright sky-white (aerial scattering).
            vec3 haze = mix(uSkyColor, vec3(lum), 0.35);
            haze = mix(haze, vec3(0.82, 0.88, 0.95), 0.4);

            // Above-horizon gradient: haze at the horizon, rising through the picked color to the deep zenith.
            float up = clamp(y, 0.0, 1.0);
            vec3 sky = mix(haze, uSkyColor, smoothstep(0.0, 0.25, up));
            sky = mix(sky, zenith, smoothstep(0.18, 0.9, up));

            // Sun scattering: a warm forward-scatter halo (strongest near the horizon), plus corona and disc.
            vec3 bandTint = haze;
            if (uHasDirLight == 1)
            {
                vec3 L = normalize(uLightDir); // points toward the sun
                float sunHeight = smoothstep(-0.15, 0.25, L.y); // fade the sun's contribution at night
                float sunDot = max(dot(rayDir, L), 0.0);

                // Wide Mie forward-scatter halo (strongest near the horizon).
                float halo = pow(sunDot, 4.0);
                float horizonBias = pow(1.0 - up, 3.0);
                sky += uLightColor * halo * (0.12 + 0.5 * horizonBias) * sunHeight;

                // Sun disc with limb darkening: core is brighter than the edge.
                float discOuter = smoothstep(0.9992, 0.9997, sunDot);
                float discCore  = smoothstep(0.9995, 0.9999, sunDot);
                float disc = discOuter * mix(0.5, 1.0, discCore * discCore);
                sky += uLightColor * disc * 12.0 * sunHeight;

                // Inner corona: tight exponential falloff just outside the disc edge, no hard ring.
                float corona = pow(sunDot, 512.0) * (1.0 - discOuter * 0.8);
                sky += uLightColor * corona * 3.5 * sunHeight;

                // Outer atmospheric glare: warm-tinted, smooth Gaussian-like falloff, no ring artifacts.
                vec3 glareColor = mix(uLightColor, vec3(1.0, 0.62, 0.18), 0.35);
                float outerGlare = pow(sunDot, 16.0) * (1.0 - pow(sunDot, 300.0) * 0.9);
                sky += glareColor * outerGlare * 0.35 * sunHeight;

                // Warm the horizon band toward the sun's side of the sky (sunset-style glow).
                vec2 flatRay = normalize(vec2(rayDir.x, rayDir.z) + 1e-4);
                vec2 flatSun = normalize(vec2(L.x, L.z) + 1e-4);
                float az = pow(max(dot(flatRay, flatSun), 0.0), 2.0);
                bandTint = mix(haze, haze * 0.6 + uLightColor * 0.7, az * sunHeight);
            }

            // Ground picks up the horizon haze as it nears the horizon (aerial perspective).
            float groundHaze = pow(1.0 - clamp(-y, 0.0, 1.0), 3.0);
            vec3 ground = mix(uGroundColor, haze, groundHaze * 0.55);

            // Soft sky/ground transition with a thin, bright atmospheric band hugging the horizon line.
            float horizonBlend = 1.0 - smoothstep(-0.04, 0.04, y);
            vec3 color = mix(sky, ground, horizonBlend);
            float band = exp(-abs(y) * 16.0);
            color = mix(color, bandTint, band * 0.35);

            // Dithering. This smooth gradient bands into concentric "onion rings" once quantized to an
            // 8-bit target (the direct, no-post-processing path — the HDR/post path dithers in its own
            // composite). Add ~1 LSB of triangular-PDF noise so each band edge dissolves into noise.
            float d1 = hash12(gl_FragCoord.xy);
            float d2 = hash12(gl_FragCoord.xy + 17.0);
            color += (d1 + d2 - 1.0) / 255.0;

            fragColor = vec4(color, 1.0);
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

    private const string ShadowVertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        uniform mat4 uLightSpaceMatrix;
        uniform mat4 uModel;
        void main()
        {
            gl_Position = uLightSpaceMatrix * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string ShadowFragmentShaderSource =
        """
        #version 330 core
        void main()
        {
            // gl_FragDepth is written automatically
        }
        """;

    // Skinned counterpart of the standard vertex shader: it blends up to four bone matrices per vertex into a
    // skinning matrix that already yields world space (each bone matrix is InverseBind * boneWorld), so — unlike
    // the rigid shader — it does not use uModel. The fragment stage is shared with the standard shader.
    private const string SkinnedVertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;
        layout (location = 3) in vec4 aBoneIndices;
        layout (location = 4) in vec4 aBoneWeights;

        uniform mat4 uViewProjection;
        uniform mat4 uLightSpaceMatrix;
        uniform vec4 uColor;

        const int MAX_BONES = 128;
        uniform mat4 uBones[MAX_BONES];

        out vec3 vFragPos;
        out vec4 vFragPosLightSpace;
        out vec3 vNormal;
        out vec2 vTexCoord;
        out vec4 vColor;
        out vec3 vModelScale;

        mat4 skinMatrix()
        {
            mat4 skin =
                uBones[int(aBoneIndices.x)] * aBoneWeights.x +
                uBones[int(aBoneIndices.y)] * aBoneWeights.y +
                uBones[int(aBoneIndices.z)] * aBoneWeights.z +
                uBones[int(aBoneIndices.w)] * aBoneWeights.w;

            float total = aBoneWeights.x + aBoneWeights.y + aBoneWeights.z + aBoneWeights.w;
            return total > 0.0001 ? skin * (1.0 / total) : mat4(1.0);
        }

        void main()
        {
            mat4 skin = skinMatrix();
            vec4 worldPos = skin * vec4(aPosition, 1.0);
            vFragPos = worldPos.xyz;
            vFragPosLightSpace = uLightSpaceMatrix * worldPos;
            vNormal = mat3(skin) * aNormal;
            vTexCoord = aTexCoord;
            vColor = uColor;
            vModelScale = vec3(1.0); // skinning replaces the model matrix, so auto-tile is off
            gl_Position = uViewProjection * worldPos;
        }
        """;

    // Skinned counterpart of the shadow vertex shader, so animated meshes cast animated shadows.
    private const string SkinnedShadowVertexShaderSource =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 3) in vec4 aBoneIndices;
        layout (location = 4) in vec4 aBoneWeights;

        uniform mat4 uLightSpaceMatrix;

        const int MAX_BONES = 128;
        uniform mat4 uBones[MAX_BONES];

        void main()
        {
            mat4 skin =
                uBones[int(aBoneIndices.x)] * aBoneWeights.x +
                uBones[int(aBoneIndices.y)] * aBoneWeights.y +
                uBones[int(aBoneIndices.z)] * aBoneWeights.z +
                uBones[int(aBoneIndices.w)] * aBoneWeights.w;

            float total = aBoneWeights.x + aBoneWeights.y + aBoneWeights.z + aBoneWeights.w;
            if (total <= 0.0001) skin = mat4(1.0); else skin = skin * (1.0 / total);

            gl_Position = uLightSpaceMatrix * skin * vec4(aPosition, 1.0);
        }
        """;
}

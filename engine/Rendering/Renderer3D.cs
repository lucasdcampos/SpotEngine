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
        uniform mat4 uLightSpaceMatrix;

        out vec3 vFragPos;
        out vec4 vFragPosLightSpace;
        out vec3 vNormal;
        out vec2 vTexCoord;

        void main()
        {
            vec4 worldPos = uModel * vec4(aPosition, 1.0);
            vFragPos = worldPos.xyz;
            vFragPosLightSpace = uLightSpaceMatrix * worldPos;
            
            vNormal = mat3(uModel) * aNormal;
            vTexCoord = aTexCoord;
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

        uniform vec4 uColor;
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
        uniform vec3 uModelScale;

        uniform int uHasDirectionalLight;
        uniform int uCastShadows;
        uniform vec3 uLightDir;
        uniform vec3 uLightColor;
        uniform float uAmbientIntensity;

        struct PointLight {
            vec3 position;
            vec3 color;
            float intensity;
            float range;
        };
        uniform int uPointLightCount;
        uniform PointLight uPointLights[4];

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

        void main()
        {
            vec2 scale2D = vec2(1.0);
            if (uAutoTile == 1) {
                vec3 n = abs(normalize(vNormal));
                if (n.x > n.y && n.x > n.z) scale2D = uModelScale.zy;
                else if (n.y > n.x && n.y > n.z) scale2D = uModelScale.xz;
                else scale2D = uModelScale.xy;
            }
            vec2 finalUV = vTexCoord * uTiling * scale2D;

            vec4 albedo = texture(uTexture, finalUV) * uColor;
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
            
            for(int i = 0; i < uPointLightCount && i < 4; i++)
            {
                vec3 lightDir = uPointLights[i].position - vFragPos;
                float distance = length(lightDir);
                if(distance < uPointLights[i].range)
                {
                    lightDir = normalize(lightDir);
                    vec3 halfVector = normalize(lightDir + viewDir);
                    
                    float diff = max(dot(normal, lightDir), 0.0);
                    float spec = pow(max(dot(normal, halfVector), 0.0), mix(16.0, 128.0, uMetallic));
                    vec3 specular = uPointLights[i].color * spec * F0;
                    
                    float attenuation = 1.0 - (distance / uPointLights[i].range);
                    attenuation = attenuation * attenuation;
                    
                    lighting += (uPointLights[i].color * diff + specular) * uPointLights[i].intensity * attenuation;
                }
            }
            
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

        struct PointLight {
            vec3 position;
            vec3 color;
            float intensity;
            float range;
        };
        uniform int uPointLightCount;
        uniform PointLight uPointLights[4];

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

            for (int i = 0; i < uPointLightCount && i < 4; i++)
            {
                vec3 Lv = uPointLights[i].position - vFragPos;
                float dist = length(Lv);
                if (dist < uPointLights[i].range)
                {
                    vec3 L = Lv / max(dist, 1e-4);
                    float atten = 1.0 - (dist / uPointLights[i].range);
                    atten *= atten;
                    float diff = max(dot(N, L), 0.0);
                    lit += bodyColor * uPointLights[i].color * uPointLights[i].intensity * diff * atten;
                    vec3 H = normalize(L + viewDir);
                    float spec = pow(max(dot(N, H), 0.0), uSpecularPower);
                    specularSum += spec * uPointLights[i].color * uPointLights[i].intensity * atten * 2.0;
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

    /// <summary>
    /// The maximum number of bones a single skinned draw can upload. Must match <c>MAX_BONES</c> in the
    /// skinned shader sources below; a skeleton with more bones is clamped (and logged) rather than crashing.
    /// </summary>
    public const int MaxBones = 128;

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

        const int MAX_BONES = 128;
        uniform mat4 uBones[MAX_BONES];

        out vec3 vFragPos;
        out vec4 vFragPosLightSpace;
        out vec3 vNormal;
        out vec2 vTexCoord;

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


    private static Shader? s_shader;
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
        s_shadowShader = new Shader(ShadowVertexShaderSource, ShadowFragmentShaderSource);
        s_skinnedShader = new Shader(SkinnedVertexShaderSource, FragmentShaderSource);
        s_skinnedShadowShader = new Shader(SkinnedShadowVertexShaderSource, ShadowFragmentShaderSource);
        s_emptyVao = new VertexArray();
        
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

    private static int s_prevFramebuffer;
    private static int[] s_prevViewport = new int[4];

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
    public static unsafe void BeginShadowPass(Matrix4x4 lightSpaceMatrix)
    {
        s_lightSpaceMatrix = lightSpaceMatrix;
        
        Renderer.Api.GetInteger(Silk.NET.OpenGL.GLEnum.FramebufferBinding, out s_prevFramebuffer);
        fixed (int* vp = s_prevViewport)
        {
            Renderer.Api.GetInteger(Silk.NET.OpenGL.GLEnum.Viewport, vp);
        }
        
        s_shadowMap!.Bind();
        Renderer.ClearDepth();
        s_shadowShader!.Use();
        s_shadowShader.SetUniform("uLightSpaceMatrix", s_lightSpaceMatrix);
        // Render front faces (default culling) rather than front-culling. Front-culling pushes the
        // occluder depth to the far side of solid meshes, which — combined with normal-offset receiver
        // bias in the lit shaders — reads as a gap between an object and its shadow. Normal offset alone
        // handles the acne that front-culling used to hide.
        Renderer.Api.CullFace(Silk.NET.OpenGL.TriangleFace.Back);
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
    public static unsafe void EndShadowPass()
    {
        Renderer.Api.BindFramebuffer(Silk.NET.OpenGL.FramebufferTarget.Framebuffer, (uint)s_prevFramebuffer);
        Renderer.Api.Viewport(s_prevViewport[0], s_prevViewport[1], (uint)s_prevViewport[2], (uint)s_prevViewport[3]);
        Renderer.Api.CullFace(Silk.NET.OpenGL.TriangleFace.Back);
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

        activeShader.SetUniform("uCameraPos", s_cameraPosition);

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
                material.NormalMap.Bind(2);
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
            activeShader.SetUniform("uTime", Spot.Core.Application.Instance.Time);
            
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

        ApplyLighting(activeShader);

        Renderer.DrawIndexed(mesh.VertexArray, mesh.IndexCount);
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

        (texture ?? s_whiteTexture).Bind(0);

        activeShader.Use();
        activeShader.SetUniform("uViewProjection", s_viewProjection);
        activeShader.SetUniform("uColor", color);
        activeShader.SetUniform("uTexture", 0);
        activeShader.SetUniform("uCameraPos", s_cameraPosition);
        activeShader.SetUniform("uBones", ClampBones(bones));

        // Skinning replaces the model matrix, so auto-tiling (which scales UVs by the model matrix) is off.
        activeShader.SetUniform("uTiling", material?.Tiling ?? Vector2.One);
        activeShader.SetUniform("uAutoTile", 0);

        activeShader.SetUniform("uMetallic", material?.Metallic ?? 0.0f);
        activeShader.SetUniform("uEmissiveColor", material?.EmissiveColor ?? Vector3.Zero);
        activeShader.SetUniform("uEmissiveIntensity", material?.EmissiveIntensity ?? 1.0f);

        if (material?.NormalMap != null)
        {
            material.NormalMap.Bind(2);
            activeShader.SetUniform("uNormalMap", 2);
            activeShader.SetUniform("uHasNormalMap", 1);
        }
        else
        {
            activeShader.SetUniform("uHasNormalMap", 0);
        }

        ApplyLighting(activeShader);

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
            shader.SetUniform($"uPointLights[{i}].position", s_pointLights[i].Position);
            shader.SetUniform($"uPointLights[{i}].color", s_pointLights[i].Color);
            shader.SetUniform($"uPointLights[{i}].intensity", s_pointLights[i].Intensity);
            shader.SetUniform($"uPointLights[{i}].range", s_pointLights[i].Range);
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

        s_gridShader.Use();
        s_gridShader.SetUniform("uViewProjection", s_viewProjection);
        s_gridShader.SetUniform("uInverseViewProjection", s_inverseViewProjection);
        s_gridShader.SetUniform("uCameraPos", cameraPos);

        Renderer.Api.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        Renderer.Api.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);

        Renderer.DrawArrays(s_emptyVao, 3);
        
        // Editor will reset or disable blending later if needed, but standard UI and transparent sprites need it too.
    }
}

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
        uniform sampler2D uShadowMap;
        
        uniform int uHasNormalMap;
        uniform float uMetallic;
        uniform mat4 uInverseViewProjection;
        
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

        float ShadowCalculation(vec4 fragPosLightSpace)
        {
            vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0) return 0.0;
            
            float currentDepth = projCoords.z;
            float bias = max(0.005 * (1.0 - dot(normalize(vNormal), normalize(uLightDir))), 0.001);
            
            float shadow = 0.0;
            vec2 texelSize = 1.0 / textureSize(uShadowMap, 0);
            for(int x = -1; x <= 1; ++x)
            {
                for(int y = -1; y <= 1; ++y)
                {
                    float pcfDepth = texture(uShadowMap, projCoords.xy + vec2(x, y) * texelSize).r; 
                    shadow += currentDepth - bias > pcfDepth ? 1.0 : 0.0;        
                }    
            }
            shadow /= 9.0;
            
            return shadow;
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
            
            vec4 camPos4 = uInverseViewProjection * vec4(0.0, 0.0, -1.0, 1.0);
            vec3 cameraPos = camPos4.xyz / camPos4.w;
            vec3 viewDir = normalize(cameraPos - vFragPos);
            
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
                
                float shadow = uCastShadows == 1 ? ShadowCalculation(vFragPosLightSpace) : 0.0;
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
            
            if (uHasDirectionalLight == 0 && uPointLightCount == 0)
            {
                fragColor = albedo; // Unlit
            }
            else
            {
                fragColor = vec4(albedo.rgb * lighting, albedo.a);
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
            vFragPosLightSpace = uLightSpaceMatrix * worldPos;
            vNormal = mat3(transpose(inverse(uModel))) * aNormal;
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
        uniform sampler2D uShadowMap;
        uniform float uTime;
        uniform mat4 uInverseViewProjection;
        
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
        
        float ShadowCalculation(vec4 fragPosLightSpace, vec3 perturbedNormal)
        {
            vec3 projCoords = fragPosLightSpace.xyz / fragPosLightSpace.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0) return 0.0;
            
            float currentDepth = projCoords.z;
            float bias = max(0.005 * (1.0 - dot(perturbedNormal, normalize(uLightDir))), 0.001);
            
            float shadow = 0.0;
            vec2 texelSize = 1.0 / textureSize(uShadowMap, 0);
            for(int x = -1; x <= 1; ++x)
            {
                for(int y = -1; y <= 1; ++y)
                {
                    float pcfDepth = texture(uShadowMap, projCoords.xy + vec2(x, y) * texelSize).r; 
                    shadow += currentDepth - bias > pcfDepth ? 1.0 : 0.0;        
                }    
            }
            shadow /= 9.0;
            return shadow;
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
            
            vec2 scale2D = vec2(1.0);
            if (uAutoTile == 1) {
                vec3 n = abs(normal);
                if (n.x > n.y && n.x > n.z) scale2D = uModelScale.zy;
                else if (n.y > n.x && n.y > n.z) scale2D = uModelScale.xz;
                else scale2D = uModelScale.xy;
            }
            vec2 finalUV = vTexCoord * uTiling * scale2D;
            
            vec4 texColor = texture(uTexture, finalUV);
            vec4 albedo = texColor * uColor;
            
            // Calculate approximate camera position from uInverseViewProjection
            vec4 camPos4 = uInverseViewProjection * vec4(0.0, 0.0, -1.0, 1.0);
            vec3 cameraPos = camPos4.xyz / camPos4.w;
            vec3 viewDir = normalize(cameraPos - vFragPos);
            
            vec3 waterBase = vec3(0.0);
            vec3 finalColor = vec3(0.0);
            
            // Fake reflection / fresnel
            float fresnel = pow(1.0 - max(dot(viewDir, perturbedNormal), 0.0), 3.0);
            vec3 skyColor = vec3(0.5, 0.7, 0.9);
            // Fake sub-surface scattering color (cyan tint on waves)
            vec3 scatterColor = mix(albedo.rgb, vec3(0.2, 0.8, 0.9), fresnel * 0.5 * uWaveStrength);
            waterBase = mix(scatterColor, skyColor, fresnel * 0.6);

            if (uHasDirectionalLight == 1)
            {
                vec3 lightDir = normalize(uLightDir);
                float shadow = uCastShadows == 1 ? ShadowCalculation(vFragPosLightSpace, perturbedNormal) : 0.0;
                
                float diff = max(dot(perturbedNormal, lightDir), 0.0);
                vec3 diffuse = diff * uLightColor;
                
                vec3 reflectDir = reflect(-lightDir, perturbedNormal);
                float spec = pow(max(dot(viewDir, reflectDir), 0.0), uSpecularPower);
                vec3 specular = spec * uLightColor * (uWaveStrength * 2.0 + 0.5);
                
                vec3 ambient = uAmbientIntensity * uLightColor;
                finalColor += (ambient + (1.0 - shadow) * diffuse) * waterBase + (1.0 - shadow) * specular;
            }
            else
            {
                finalColor += uAmbientIntensity * uLightColor * waterBase;
            }
            
            for(int i = 0; i < uPointLightCount && i < 4; i++)
            {
                vec3 lightDir = uPointLights[i].position - vFragPos;
                float distance = length(lightDir);
                if(distance < uPointLights[i].range)
                {
                    lightDir = normalize(lightDir);
                    float diff = max(dot(perturbedNormal, lightDir), 0.0);
                    float attenuation = 1.0 - (distance / uPointLights[i].range);
                    attenuation = attenuation * attenuation;
                    
                    vec3 reflectDir = reflect(-lightDir, perturbedNormal);
                    float spec = pow(max(dot(viewDir, reflectDir), 0.0), uSpecularPower);
                    vec3 specular = spec * uPointLights[i].color * (uWaveStrength * 2.0 + 0.5);
                    
                    finalColor += uPointLights[i].color * uPointLights[i].intensity * diff * attenuation * waterBase + specular * attenuation * uPointLights[i].intensity;
                }
            }

            if (uHasDirectionalLight == 0 && uPointLightCount == 0)
            {
                fragColor = albedo;
            }
            else
            {
                fragColor = vec4(finalColor, albedo.a);
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


    private static Shader? s_shader;
    private static Shader? s_waterShader;
    private static Shader? s_skyboxShader;
    private static Shader? s_cloudsShader;
    private static Shader? s_gridShader;
    private static Shader? s_shadowShader;
    private static VertexArray? s_emptyVao;
    private static Texture2D? s_whiteTexture;
    private static DepthFramebuffer? s_shadowMap;
    private static Matrix4x4 s_viewProjection = Matrix4x4.Identity;
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
        s_emptyVao = new VertexArray();
        
        s_shadowMap = new DepthFramebuffer(2048, 2048);

        // A 1x1 white texture lets untextured (solid-color) meshes reuse the textured path: texture * color == color.
        ReadOnlySpan<byte> white = stackalloc byte[] { 255, 255, 255, 255 };
        s_whiteTexture = new Texture2D(1, 1, white);
    }

    /// <summary>
    /// Begins a 3D scene. Meshes drawn until <see cref="EndScene"/> use this view-projection.
    /// </summary>
    public static void BeginScene(Matrix4x4 viewProjection, bool hasLight = false, Vector3 lightDir = default, Vector3 lightColor = default, float ambientIntensity = 0.3f, Matrix4x4 lightSpaceMatrix = default, bool castShadows = false, System.ReadOnlySpan<PointLightData> pointLights = default)
    {
        s_viewProjection = viewProjection;
        s_hasDirLight = hasLight ? 1 : 0;
        s_lightDir = hasLight ? lightDir : Vector3.UnitY;
        s_lightColor = hasLight ? lightColor : Vector3.One;
        s_ambientIntensity = ambientIntensity;
        s_lightSpaceMatrix = lightSpaceMatrix;
        s_castShadows = castShadows ? 1 : 0;
        
        s_pointLightCount = System.Math.Min(pointLights.Length, 4);
        for (int i = 0; i < s_pointLightCount; i++)
        {
            s_pointLights[i] = pointLights[i];
        }
    }

    private static int s_prevFramebuffer;
    private static int[] s_prevViewport = new int[4];

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
        Renderer.Api.CullFace(Silk.NET.OpenGL.TriangleFace.Front);
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
        
        Matrix4x4.Invert(s_viewProjection, out Matrix4x4 invViewProj);
        activeShader.SetUniform("uInverseViewProjection", invViewProj);
        
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
        }

        activeShader.SetUniform("uHasDirectionalLight", s_hasDirLight);
        if (s_hasDirLight == 1)
        {
            activeShader.SetUniform("uLightDir", s_lightDir);
            activeShader.SetUniform("uLightColor", s_lightColor);
            activeShader.SetUniform("uAmbientIntensity", s_ambientIntensity);
        }
        
        if (s_castShadows == 1)
        {
            s_shadowMap!.BindDepthTexture(1);
            activeShader.SetUniform("uShadowMap", 1);
            activeShader.SetUniform("uLightSpaceMatrix", s_lightSpaceMatrix);
            activeShader.SetUniform("uCastShadows", 1);
        }
        else
        {
            activeShader.SetUniform("uCastShadows", 0);
        }

        activeShader.SetUniform("uPointLightCount", s_pointLightCount);
        for (int i = 0; i < s_pointLightCount; i++)
        {
            activeShader.SetUniform($"uPointLights[{i}].position", s_pointLights[i].Position);
            activeShader.SetUniform($"uPointLights[{i}].color", s_pointLights[i].Color);
            activeShader.SetUniform($"uPointLights[{i}].intensity", s_pointLights[i].Intensity);
            activeShader.SetUniform($"uPointLights[{i}].range", s_pointLights[i].Range);
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
    /// Draws the procedural skybox.
    /// </summary>
    public static void DrawSkybox(Vector3 skyColor, Vector3 groundColor)
    {
        if (s_skyboxShader == null || s_emptyVao == null) return;

        Matrix4x4.Invert(s_viewProjection, out Matrix4x4 invViewProj);
        s_skyboxShader.Use();
        s_skyboxShader.SetUniform("uInverseViewProjection", invViewProj);
        
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

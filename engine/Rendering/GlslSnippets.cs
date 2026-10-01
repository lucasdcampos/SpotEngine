namespace Spot.Rendering;

// Shared GLSL utility functions injected via C# string concatenation into the shaders that need them.
// Each snippet is a self-contained GLSL block (no surrounding version/layout declarations).
internal static class GlslSnippets
{
    // Cheap, sine-free per-pixel hash in [0,1). Used for dithering and noise in skybox/clouds/water/postprocess.
    internal const string Hash = """

        float hash(vec2 p) {
            vec3 p3  = fract(vec3(p.xyx) * .1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }

        """;

    // Normal-offset PCF directional shadow. Requires uniforms: uShadowTexelSize, uLightSpaceMatrix, uShadowMap.
    // Pushes the receiver along its normal to avoid acne at grazing angles; samples a 5x5 hardware-PCF kernel.
    internal const string ShadowCalculation = """

        float ShadowCalculation(vec3 worldPos, vec3 N, vec3 L)
        {
            float slope = clamp(1.0 - dot(N, L), 0.0, 1.0);
            vec3 offsetPos = worldPos + N * uShadowTexelSize * (1.5 + 3.0 * slope);
            vec4 lp = uLightSpaceMatrix * vec4(offsetPos, 1.0);

            vec3 projCoords = lp.xyz / lp.w;
            projCoords = projCoords * 0.5 + 0.5;
            if (projCoords.z > 1.0) return 0.0;
            // Outside the shadow map extent reads as fully lit; test bounds explicitly for WebGL2 compat.
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

        """;
}

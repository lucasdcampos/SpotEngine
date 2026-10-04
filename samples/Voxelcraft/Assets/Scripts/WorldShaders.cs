namespace Voxelcraft;

/// <summary>
/// The GLSL behind <see cref="WorldRenderer"/>, written once in desktop GLSL 3.30 (the engine rewrites it for
/// WebGL2). The sky function is shared by the sky, the fog and the water's reflections, so distant terrain melts
/// into exactly the color of the sky behind it.
/// </summary>
internal static class WorldShaders
{
    private const string Sky =
        """
        uniform vec3 uSunDir;
        uniform vec3 uZenith;
        uniform vec3 uHorizon;
        uniform float uTwilight;
        uniform float uDaylight;

        vec3 skyColor(vec3 dir)
        {
            float up = max(dir.y, 0.0);
            vec3 col = mix(uHorizon, uZenith, pow(up, 0.55));
            // Below the horizon the haze darkens a little, like the ground's reflected light.
            col = mix(col, uHorizon * 0.6, clamp(-dir.y * 2.5, 0.0, 1.0));

            // Sunrise and sunset: the band of sky along the horizon burns orange toward the sun and rose away from it.
            float toward = max(dot(dir, uSunDir), 0.0);
            float band = exp(-max(dir.y, 0.0) * 6.0);
            vec3 glow = mix(vec3(0.55, 0.26, 0.32), vec3(1.0, 0.36, 0.07), pow(toward, 3.0));
            col = mix(col, glow, clamp(uTwilight * band * (0.45 + 0.55 * toward), 0.0, 1.0));
            col += vec3(1.0, 0.45, 0.12) * pow(toward, 14.0) * 0.9 * uTwilight;
            col += vec3(1.0, 0.86, 0.66) * pow(toward, 24.0) * 0.4 * uDaylight;
            return col;
        }
        """;

    // Decodes the packed vertex (see ChunkMesher) into its parts.
    private const string Decode =
        """
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec2 aUV;
        layout (location = 2) in float aTile;
        layout (location = 3) in float aLight;
        layout (location = 4) in float aTint;

        uniform mat4 uViewProjection;
        uniform vec3 uChunkOrigin;
        uniform float uTime;

        int tileOf() { return int(aTile + 0.5) % 256; }
        int faceOf() { return int(aTile + 0.5) / 256; }
        int lightOf() { return int(aLight + 0.5); }

        vec3 faceNormal(int face)
        {
            if (face == 0) return vec3(1.0, 0.0, 0.0);
            if (face == 1) return vec3(-1.0, 0.0, 0.0);
            if (face == 2) return vec3(0.0, 1.0, 0.0);
            if (face == 3) return vec3(0.0, -1.0, 0.0);
            if (face == 4) return vec3(0.0, 0.0, 1.0);
            if (face == 5) return vec3(0.0, 0.0, -1.0);
            return vec3(0.0, 1.0, 0.0);
        }

        vec3 worldPosition(int flags)
        {
            vec3 world = aPosition + uChunkOrigin;
            if ((flags & 1) != 0)
            {
                // Plants sway at the top, each a little out of step with its neighbours.
                float phase = world.x * 0.7 + world.z * 0.43;
                world.x += sin(uTime * 1.9 + phase) * 0.07 + sin(uTime * 3.3 + phase * 1.7) * 0.025;
                world.z += cos(uTime * 1.6 + phase * 1.3) * 0.06;
            }
            return world;
        }
        """;

    // Samples a tile of the atlas with mip-mapping that never bleeds into the neighbouring tiles: the level is
    // chosen by hand (at most 4, where a tile is one texel) and the coordinate is kept half a texel inside the tile.
    private const string Atlas =
        """
        uniform sampler2D uAtlas;

        vec2 atlasCoord(vec2 uv, int tile, out float lod)
        {
            // Merged faces span several blocks: the UV counts blocks, and the tile repeats once per block.
            vec2 dx = dFdx(uv * 16.0);
            vec2 dy = dFdy(uv * 16.0);
            vec2 texel = vec2(fract(uv.x), 1.0 - fract(uv.y)) * 16.0;
            lod = clamp(0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8)), 0.0, 4.0);
            float inset = 0.5 * exp2(floor(lod + 0.5));
            texel = clamp(texel, vec2(min(inset, 8.0)), vec2(16.0 - min(inset, 8.0)));
            vec2 origin = vec2(float(tile % 16), float(tile / 16)) * 16.0;
            return (origin + texel) / 256.0;
        }
        """;

    public const string TerrainVertex =
        "#version 330 core\n" + Decode +
        """

        out vec3 vWorld;
        out vec2 vUV;
        flat out int vTile;
        flat out int vFace;
        flat out int vFlags;
        out float vAO;
        out float vSky;
        out float vGlow;
        out vec3 vTint;

        void main()
        {
            int light = lightOf();
            vFlags = light / 1024;
            vec3 world = worldPosition(vFlags);
            vWorld = world;
            vUV = aUV;
            vTile = tileOf();
            vFace = faceOf();
            vAO = float(light % 4);
            vSky = float((light / 4) % 16);
            vGlow = float((light / 64) % 16);
            int tint = int(aTint + 0.5);
            vTint = pow(vec3(float((tint >> 16) & 255), float((tint >> 8) & 255), float(tint & 255)) / 255.0, vec3(2.2));
            gl_Position = uViewProjection * vec4(world, 1.0);
        }
        """;

    private const string Lighting =
        """
        uniform sampler2DShadow uShadowMap;
        uniform mat4 uLightSpace;
        uniform vec2 uShadowTexel;
        uniform float uShadowEnabled;
        uniform float uShadowRange;
        uniform vec3 uShadowCenter;
        uniform vec3 uCameraPos;
        uniform vec3 uLightDir;
        uniform vec3 uLightColor;
        uniform vec3 uAmbientSky;
        uniform vec3 uAmbientHorizon;
        uniform float uFogStart;
        uniform float uFogEnd;
        uniform float uUnderwater;

        float shadowAt(vec3 world, vec3 normal)
        {
            if (uShadowEnabled < 0.5) return 1.0;
            vec4 p = uLightSpace * vec4(world + normal * 0.06 + uLightDir * 0.02, 1.0);
            vec3 c = p.xyz / p.w;
            c = c * 0.5 + 0.5;
            if (c.x <= 0.0 || c.x >= 1.0 || c.y <= 0.0 || c.y >= 1.0 || c.z >= 1.0) return 1.0;

            float sum = 0.0;
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    sum += texture(uShadowMap, vec3(c.xy + vec2(float(x), float(y)) * uShadowTexel, c.z - 0.0006));
                }
            }
            float lit = sum / 9.0;

            // Fade out at the edge of the shadowed area instead of ending in a line.
            float edge = length((world - uShadowCenter).xz) / uShadowRange;
            lit = mix(lit, 1.0, smoothstep(0.75, 0.98, edge));
            return lit;
        }

        vec3 applyFog(vec3 color, vec3 world)
        {
            vec3 toPoint = world - uCameraPos;
            float dist = length(toPoint);
            vec3 dir = toPoint / max(dist, 1e-4);
            if (uUnderwater > 0.5)
            {
                float f = 1.0 - exp(-dist * 0.09);
                vec3 deep = vec3(0.012, 0.06, 0.1) * (0.25 + uAmbientSky.b * 1.4);
                return mix(color, deep, f);
            }
            float haze = 1.0 - exp(-dist * 0.0011);
            float edge = smoothstep(uFogStart, uFogEnd, dist);
            float f = clamp(max(haze * 0.45, edge * edge), 0.0, 1.0);
            return mix(color, skyColor(dir), f);
        }

        // Light reaching a surface: the sun (or moon) through the shadow map, the open sky above and around it, and
        // the warm light of glowing blocks; all of it dimmed in corners by the ambient occlusion.
        vec3 lightSurface(vec3 albedo, vec3 world, vec3 normal, int face, float ao, float sky, float glow)
        {
            float skyLevel = sky / 15.0;
            float occlusion = mix(0.38, 1.0, ao / 3.0);
            float sunGate = smoothstep(0.55, 0.95, skyLevel);

            float diffuse = face == 6 ? 0.65 : max(dot(normal, uLightDir), 0.0);
            float shadow = diffuse > 0.0 ? shadowAt(world, face == 6 ? uLightDir : normal) : 0.0;
            vec3 direct = uLightColor * diffuse * shadow * sunGate * mix(1.0, occlusion, 0.4);

            // Minecraft's directional shading keeps the faces of a cube readable even in full shade.
            float shade = face == 2 ? 1.0 : face == 3 ? 0.5 : (face == 0 || face == 1) ? 0.78 : face == 6 ? 0.9 : 0.66;
            vec3 ambient = mix(uAmbientHorizon, uAmbientSky, normal.y * 0.5 + 0.5) * pow(skyLevel, 1.8) * shade;
            ambient = max(ambient, vec3(0.016, 0.018, 0.024) * shade);

            float g = glow / 15.0;
            vec3 blockLight = vec3(1.0, 0.68, 0.38) * (g * g * g * 2.6) * shade;
            return albedo * (direct + (ambient + blockLight) * occlusion);
        }
        """;

    public const string TerrainFragment =
        "#version 330 core\n" + Sky + Atlas + Lighting +
        """

        uniform sampler2D uTintMask;

        in vec3 vWorld;
        in vec2 vUV;
        flat in int vTile;
        flat in int vFace;
        flat in int vFlags;
        in float vAO;
        in float vSky;
        in float vGlow;
        in vec3 vTint;

        out vec4 fragColor;

        vec3 normalOf(int face)
        {
            if (face == 0) return vec3(1.0, 0.0, 0.0);
            if (face == 1) return vec3(-1.0, 0.0, 0.0);
            if (face == 2) return vec3(0.0, 1.0, 0.0);
            if (face == 3) return vec3(0.0, -1.0, 0.0);
            if (face == 4) return vec3(0.0, 0.0, 1.0);
            if (face == 5) return vec3(0.0, 0.0, -1.0);
            return vec3(0.0, 1.0, 0.0);
        }

        void main()
        {
            float lod;
            vec2 coord = atlasCoord(vUV, vTile, lod);
            vec4 texel = textureLod(uAtlas, coord, lod);
            if (texel.a < 0.5) discard;

            float mask = textureLod(uTintMask, coord, lod).r;
            vec3 albedo = pow(texel.rgb, vec3(2.2)) * mix(vec3(1.0), vTint, mask);

            vec3 color;
            if ((vFlags & 2) != 0)
            {
                // Glowing blocks shine on their own, bright enough to bloom.
                color = albedo * 2.4;
            }
            else
            {
                color = lightSurface(albedo, vWorld, normalOf(vFace), vFace, vAO, vSky, vGlow);
            }

            fragColor = vec4(applyFog(color, vWorld), 1.0);
        }
        """;

    public const string WaterVertex =
        "#version 330 core\n" + Decode +
        """

        out vec3 vWorld;
        flat out int vFace;
        out float vSky;
        out float vGlow;

        void main()
        {
            int light = lightOf();
            vec3 world = aPosition + uChunkOrigin;
            vFace = faceOf();
            if (vFace == 2)
            {
                // A gentle swell on open water.
                world.y += (sin(uTime * 1.3 + world.x * 0.55 + world.z * 0.3) + sin(uTime * 0.9 - world.z * 0.7)) * 0.025 - 0.02;
            }
            vWorld = world;
            vSky = float((light / 4) % 16);
            vGlow = float((light / 64) % 16);
            gl_Position = uViewProjection * vec4(world, 1.0);
        }
        """;

    public const string WaterFragment =
        "#version 330 core\n" + Sky + Lighting +
        """

        uniform float uTime;

        in vec3 vWorld;
        flat in int vFace;
        in float vSky;
        in float vGlow;

        out vec4 fragColor;

        // A sum of travelling waves in several directions at unrelated wavelengths, so no grid shows; they calm
        // with distance, where they would only shimmer.
        vec3 waveNormal(vec2 p, float t, float dist)
        {
            vec2 slope = vec2(0.0);
            vec2 dirs[5] = vec2[5](vec2(0.94, 0.34), vec2(-0.53, 0.85), vec2(0.17, -0.98), vec2(-0.87, -0.49), vec2(0.62, 0.78));
            float lengths[5] = float[5](3.7, 2.3, 1.45, 0.93, 0.61);
            float speeds[5] = float[5](1.1, 1.4, 1.9, 2.3, 2.9);
            for (int i = 0; i < 5; i++)
            {
                float k = 6.2831 / lengths[i];
                float phase = dot(dirs[i], p) * k + t * speeds[i] + float(i) * 1.7;
                slope += dirs[i] * cos(phase) * (0.028 * lengths[i] * k * 0.25);
            }
            slope *= 1.0 / (1.0 + dist * 0.025);
            return normalize(vec3(-slope.x, 1.0, -slope.y));
        }

        void main()
        {
            vec3 normal = vFace == 2 ? waveNormal(vWorld.xz, uTime, length(vWorld - uCameraPos)) : (vFace == 3 ? vec3(0.0, -1.0, 0.0) : vec3(0.0, 0.0, 0.0));
            if (vFace != 2 && vFace != 3)
            {
                normal = vFace == 0 ? vec3(1.0, 0.0, 0.0) : vFace == 1 ? vec3(-1.0, 0.0, 0.0) : vFace == 4 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 0.0, -1.0);
            }

            vec3 view = normalize(uCameraPos - vWorld);
            if (dot(normal, view) < 0.0) normal = -normal;

            float skyLevel = vSky / 15.0;
            float shadow = shadowAt(vWorld, vec3(0.0, 1.0, 0.0));
            float sunGate = smoothstep(0.55, 0.95, skyLevel);

            float fresnel = 0.02 + 0.98 * pow(1.0 - max(dot(normal, view), 0.0), 5.0);
            vec3 reflected = skyColor(reflect(-view, normal)) * mix(0.25, 1.0, pow(skyLevel, 2.0));

            vec3 ambient = mix(uAmbientHorizon, uAmbientSky, 0.6) * pow(skyLevel, 1.8);
            float g = vGlow / 15.0;
            vec3 body = vec3(0.03, 0.16, 0.24) * (ambient + uLightColor * max(uLightDir.y, 0.0) * shadow * sunGate * 0.6
                        + vec3(1.0, 0.68, 0.38) * g * g * g * 2.0);

            vec3 halfway = normalize(uLightDir + view);
            float spec = pow(max(dot(normal, halfway), 0.0), 220.0) * 3.5;
            vec3 color = mix(body, reflected, fresnel) + uLightColor * spec * shadow * sunGate;
            float alpha = mix(0.62, 0.95, fresnel);
            if (uUnderwater > 0.5) alpha = 0.55;

            fragColor = vec4(applyFog(color, vWorld), alpha);
        }
        """;

    public const string ShadowVertex =
        "#version 330 core\n" + Decode +
        """

        out vec2 vUV;
        flat out int vTile;

        void main()
        {
            vec3 world = worldPosition(lightOf() / 1024);
            vUV = aUV;
            vTile = tileOf();
            gl_Position = uViewProjection * vec4(world, 1.0);
        }
        """;

    public const string ShadowFragment =
        """
        #version 330 core
        uniform sampler2D uAtlas;

        in vec2 vUV;
        flat in int vTile;

        out vec4 fragColor;

        void main()
        {
            vec2 texel = clamp(vec2(fract(vUV.x), 1.0 - fract(vUV.y)) * 16.0, vec2(0.5), vec2(15.5));
            vec2 origin = vec2(float(vTile % 16), float(vTile / 16)) * 16.0;
            if (textureLod(uAtlas, (origin + texel) / 256.0, 0.0).a < 0.5) discard;
            fragColor = vec4(1.0);
        }
        """;

    public const string SkyFragment =
        "#version 330 core\n" + Sky +
        """

        in vec2 vTexCoord;

        uniform mat4 uInverseViewProjection;
        uniform vec3 uCameraPos;
        uniform vec3 uMoonDir;
        uniform vec3 uLightColor;
        uniform vec3 uAmbientSky;
        uniform float uStars;
        uniform float uTime;
        uniform float uStarAngle;
        uniform float uUnderwater;

        out vec4 fragColor;

        float hash(vec3 p)
        {
            p = fract(p * vec3(443.897, 441.423, 437.195));
            p += dot(p, p.yzx + 19.19);
            return fract((p.x + p.y) * p.z);
        }

        float hash2(vec2 p)
        {
            vec3 q = fract(vec3(p.xyx) * 0.1031);
            q += dot(q, q.yzx + 33.33);
            return fract((q.x + q.y) * q.z);
        }

        float valueNoise(vec2 p)
        {
            vec2 i = floor(p);
            vec2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash2(i), hash2(i + vec2(1.0, 0.0)), f.x), mix(hash2(i + vec2(0.0, 1.0)), hash2(i + vec2(1.0, 1.0)), f.x), f.y);
        }

        // A square disc facing the viewer along `axis`, like the sun and moon of the game this sample nods to.
        float square(vec3 dir, vec3 axis, float size, out vec2 uv)
        {
            vec3 right = normalize(cross(axis, abs(axis.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0)));
            vec3 up = cross(right, axis);
            float d = dot(dir, axis);
            uv = vec2(dot(dir, right), dot(dir, up)) / max(d, 1e-4) / size;
            if (d <= 0.0) return 0.0;
            vec2 edge = smoothstep(vec2(1.0), vec2(0.96), abs(uv));
            return edge.x * edge.y;
        }

        void main()
        {
            vec4 farPoint = uInverseViewProjection * vec4(vTexCoord * 2.0 - 1.0, 1.0, 1.0);
            vec3 dir = normalize(farPoint.xyz / farPoint.w - uCameraPos);

            if (uUnderwater > 0.5)
            {
                fragColor = vec4(vec3(0.012, 0.06, 0.1) * (0.25 + uAmbientSky.b * 1.4), 1.0);
                return;
            }

            vec3 col = skyColor(dir);

            // Stars turn with the night sky and twinkle.
            if (uStars > 0.01 && dir.y > -0.1)
            {
                float c = cos(uStarAngle);
                float s = sin(uStarAngle);
                vec3 sd = vec3(c * dir.x - s * dir.y, s * dir.x + c * dir.y, dir.z);
                vec3 cell = floor(sd * 220.0);
                float h = hash(cell);
                if (h > 0.9965)
                {
                    vec3 center = (cell + 0.5) / 220.0;
                    float d = length(normalize(center) - sd) * 220.0;
                    float twinkle = 0.65 + 0.35 * sin(uTime * (2.0 + h * 40.0) + h * 100.0);
                    float star = smoothstep(0.55, 0.0, d) * twinkle * (h - 0.9965) / 0.0035;
                    col += vec3(0.9, 0.95, 1.0) * star * 2.2 * uStars * smoothstep(-0.1, 0.15, dir.y);
                }
            }

            // The sun: a hot square with a soft halo, bright enough to bloom.
            vec2 uv;
            float sun = square(dir, uSunDir, 0.055, uv);
            float halo = pow(max(dot(dir, uSunDir), 0.0), 220.0);
            float high = smoothstep(0.0, 0.5, uSunDir.y);
            vec3 sunTint = mix(vec3(1.0, 0.34, 0.07), vec3(1.0, 0.95, 0.85), high);
            col += sunTint * (sun * mix(2.4, 20.0, high) + halo * 2.0) * smoothstep(-0.12, 0.02, dir.y);

            // The moon: a pale square with darker maria.
            float moon = square(dir, uMoonDir, 0.045, uv);
            if (moon > 0.0)
            {
                float maria = valueNoise(uv * 3.0 + 4.0) * 0.6 + valueNoise(uv * 7.0) * 0.4;
                vec3 surface = vec3(0.86, 0.88, 0.95) * (0.75 + 0.35 * smoothstep(0.35, 0.75, maria));
                col = mix(col, surface * 1.6, moon * smoothstep(-0.12, 0.02, dir.y));
            }

            // Clouds: a flat layer of blocky puffs high above, drifting west.
            if (dir.y > 0.0)
            {
                float t = (210.0 - uCameraPos.y) / dir.y;
                if (t > 0.0)
                {
                    vec2 p = uCameraPos.xz + dir.xz * t;
                    p.x += uTime * 1.6;
                    vec2 cell = floor(p / 12.0);
                    float cover = valueNoise(cell * 0.18) * 0.65 + valueNoise(cell * 0.45 + 7.0) * 0.35;
                    vec2 f = fract(p / 12.0);
                    float cloud = step(0.56, cover);
                    // Neighbouring cells join seamlessly; only edges facing open sky soften.
                    float joinX = step(0.56, valueNoise((cell + vec2(f.x < 0.5 ? -1.0 : 1.0, 0.0)) * 0.18) * 0.65
                                       + valueNoise((cell + vec2(f.x < 0.5 ? -1.0 : 1.0, 0.0)) * 0.45 + 7.0) * 0.35);
                    float joinY = step(0.56, valueNoise((cell + vec2(0.0, f.y < 0.5 ? -1.0 : 1.0)) * 0.18) * 0.65
                                       + valueNoise((cell + vec2(0.0, f.y < 0.5 ? -1.0 : 1.0)) * 0.45 + 7.0) * 0.35);
                    float ex = smoothstep(0.0, 0.06, min(f.x, 1.0 - f.x));
                    float ey = smoothstep(0.0, 0.06, min(f.y, 1.0 - f.y));
                    cloud *= max(ex, joinX) * max(ey, joinY);

                    vec3 lit = uAmbientSky * 1.3 + uLightColor * 0.42 * max(uSunDir.y, 0.0) + uLightColor * 0.12;
                    vec3 cloudColor = vec3(0.95) * lit + vec3(1.0, 0.45, 0.2) * uTwilight * 0.35 + col * 0.6;
                    float fade = exp(-t * 0.0012) * smoothstep(0.0, 0.12, dir.y);
                    col = mix(col, cloudColor, cloud * 0.88 * fade);
                }
            }

            fragColor = vec4(col, 1.0);
        }
        """;

    public const string LineVertex =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        uniform mat4 uViewProjection;
        void main()
        {
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    public const string LineFragment =
        """
        #version 330 core
        uniform vec4 uColor;
        out vec4 fragColor;
        void main()
        {
            fragColor = uColor;
        }
        """;

    public const string ParticleVertex =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec2 aUV;
        layout (location = 2) in vec4 aColor;
        uniform mat4 uViewProjection;
        out vec2 vUV;
        out vec4 vColor;
        out vec3 vWorld;
        void main()
        {
            vUV = aUV;
            vColor = aColor;
            vWorld = aPosition;
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    public const string ParticleFragment =
        "#version 330 core\n" + Sky +
        """

        uniform sampler2D uAtlas;
        uniform vec3 uCameraPos;
        uniform float uFogStart;
        uniform float uFogEnd;
        in vec2 vUV;
        in vec4 vColor;
        in vec3 vWorld;
        out vec4 fragColor;
        void main()
        {
            vec4 texel = textureLod(uAtlas, vUV, 0.0);
            if (texel.a < 0.5) discard;
            vec3 color = pow(texel.rgb, vec3(2.2)) * vColor.rgb;
            float dist = length(vWorld - uCameraPos);
            color = mix(color, skyColor(normalize(vWorld - uCameraPos)), smoothstep(uFogStart, uFogEnd, dist));
            fragColor = vec4(color, 1.0);
        }
        """;
}

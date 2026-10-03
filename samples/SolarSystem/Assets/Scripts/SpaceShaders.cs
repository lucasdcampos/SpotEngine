namespace SolarSystem;

/// <summary>
/// The GLSL behind <see cref="SpaceRenderer"/>. Written once in desktop GLSL 3.30: the engine's graphics device
/// rewrites it for WebGL2 in the browser. Every surface is procedural — gradient noise on the unit sphere, in the
/// body's own frame so features turn with it — which is why the sample ships no textures at all.
/// </summary>
internal static class SpaceShaders
{
    // Integer hashing (Chris Wellons' "lowbias32", public domain) feeding 3D gradient noise and fractal sums.
    private const string Noise =
        """
        const float PI = 3.14159265;

        uint hashU(uint x)
        {
            x ^= x >> 16u;
            x *= 0x7feb352du;
            x ^= x >> 15u;
            x *= 0x846ca68bu;
            x ^= x >> 16u;
            return x;
        }

        uint hash3u(ivec3 c)
        {
            uvec3 u = uvec3(c + ivec3(32768));
            return hashU(u.x ^ hashU(u.y ^ hashU(u.z)));
        }

        vec3 gradient(ivec3 c)
        {
            uint h = hash3u(c);
            return vec3(float(h & 1023u), float((h >> 10u) & 1023u), float((h >> 20u) & 1023u)) * (2.0 / 1023.0) - 1.0;
        }

        // 3D gradient noise in roughly -1..1.
        float noise(vec3 p)
        {
            vec3 cell = floor(p);
            ivec3 i = ivec3(cell);
            vec3 f = p - cell;
            vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
            float n000 = dot(gradient(i), f);
            float n100 = dot(gradient(i + ivec3(1, 0, 0)), f - vec3(1.0, 0.0, 0.0));
            float n010 = dot(gradient(i + ivec3(0, 1, 0)), f - vec3(0.0, 1.0, 0.0));
            float n110 = dot(gradient(i + ivec3(1, 1, 0)), f - vec3(1.0, 1.0, 0.0));
            float n001 = dot(gradient(i + ivec3(0, 0, 1)), f - vec3(0.0, 0.0, 1.0));
            float n101 = dot(gradient(i + ivec3(1, 0, 1)), f - vec3(1.0, 0.0, 1.0));
            float n011 = dot(gradient(i + ivec3(0, 1, 1)), f - vec3(0.0, 1.0, 1.0));
            float n111 = dot(gradient(i + ivec3(1, 1, 1)), f - vec3(1.0, 1.0, 1.0));
            return mix(mix(mix(n000, n100, u.x), mix(n010, n110, u.x), u.y),
                       mix(mix(n001, n101, u.x), mix(n011, n111, u.x), u.y), u.z);
        }

        float fbm(vec3 p, int octaves)
        {
            float sum = 0.0;
            float amplitude = 0.5;
            for (int i = 0; i < 8; i++)
            {
                if (i >= octaves) break;
                sum += amplitude * noise(p);
                p = p * 2.03 + vec3(17.3, -9.1, 4.7);
                amplitude *= 0.5;
            }
            return sum;
        }

        vec3 rotateY(vec3 p, float a)
        {
            float c = cos(a);
            float s = sin(a);
            return vec3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z);
        }
        """;

    // Ring opacity at a distance from the planet's center (in planet radii): Saturn's broad C, B and A rings with
    // the Cassini division and Encke gap, or Uranus' narrow dark ringlets. `detail` fades the finest ringlets out
    // where they would shimmer.
    private const string Rings =
        """
        uniform vec2 uRing;
        uniform int uRingStyle;

        float ringDensity(float x, float detail)
        {
            float t = (x - uRing.x) / max(uRing.y - uRing.x, 1e-4);
            if (t < 0.0 || t > 1.0) return 0.0;

            float d;
            if (uRingStyle == 1)
            {
                float positions[7] = float[7](0.04, 0.12, 0.19, 0.31, 0.44, 0.58, 0.93);
                float widths[7] = float[7](0.006, 0.005, 0.006, 0.008, 0.006, 0.007, 0.02);
                d = 0.0;
                for (int i = 0; i < 7; i++)
                {
                    float k = (t - positions[i]) / widths[i];
                    d += exp(-k * k) * (i == 6 ? 0.85 : 0.55);
                }
                d += 0.04;
            }
            else
            {
                float c = smoothstep(0.0, 0.03, t) * (1.0 - smoothstep(0.27, 0.29, t)) * (0.2 + 0.12 * noise(vec3(x * 40.0, 1.3, 0.0)));
                float b = smoothstep(0.27, 0.31, t) * (1.0 - smoothstep(0.675, 0.69, t)) * (0.62 + 0.36 * smoothstep(0.31, 0.5, t));
                float cassini = smoothstep(0.69, 0.7, t) * (1.0 - smoothstep(0.765, 0.775, t)) * 0.05;
                float a = smoothstep(0.765, 0.785, t) * (1.0 - smoothstep(0.985, 1.0, t)) * 0.58;
                a *= smoothstep(0.002, 0.005, abs(t - 0.946));
                d = c + b + cassini + a;
                d *= 0.74 + 0.26 * clamp(noise(vec3(x * 110.0, 0.5, 0.25)) * 2.0, -1.0, 1.0);
                d *= 1.0 + detail * 0.16 * noise(vec3(x * 480.0, 2.0, 1.0));
            }
            return clamp(d, 0.0, 1.0);
        }
        """;

    /// <summary>Transforms the unit sphere (or a flat ring) and hands the fragment stage its frame-local point.</summary>
    public const string BodyVertex =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;

        uniform mat4 uViewProjection;
        uniform mat4 uModel;

        out vec3 vWorldPos;
        out vec3 vLocal;

        void main()
        {
            vLocal = aPosition;
            vec4 world = uModel * vec4(aPosition, 1.0);
            vWorldPos = world.xyz;
            gl_Position = uViewProjection * world;
        }
        """;

    /// <summary>A camera-facing quad around a point, for glows computed per pixel (atmospheres, the corona).</summary>
    public const string BillboardVertex =
        """
        #version 330 core
        layout (location = 0) in vec3 aPosition;

        uniform mat4 uViewProjection;
        uniform vec3 uCenter;
        uniform vec3 uRight;
        uniform vec3 uUp;
        uniform float uHalfSize;

        out vec3 vWorldPos;
        out vec2 vQuad;

        void main()
        {
            vQuad = aPosition.xy * 2.0;
            vec3 world = uCenter + (uRight * vQuad.x + uUp * vQuad.y) * uHalfSize;
            vWorldPos = world;
            gl_Position = uViewProjection * vec4(world, 1.0);
        }
        """;

    /// <summary>The planets and moons: a procedural surface per <see cref="SurfaceStyle"/>, lit by the Sun.</summary>
    public const string PlanetFragment =
        """
        #version 330 core
        in vec3 vWorldPos;
        in vec3 vLocal;

        uniform vec3 uCenter;
        uniform float uRadius;
        uniform vec3 uCameraPos;
        uniform vec3 uSunPos;
        uniform vec3 uSunColor;
        uniform vec3 uAmbient;
        uniform int uSurface;
        uniform vec3 uColorA;
        uniform vec3 uColorB;
        uniform vec3 uColorC;
        uniform float uSeed;
        uniform float uTime;
        uniform float uBandFrequency;
        uniform float uTurbulence;
        uniform vec4 uStorm;
        uniform vec3 uStormColor;
        uniform vec4 uAtmosphere;
        uniform vec3 uPole;
        uniform vec4 uRingColor;

        out vec4 fragColor;
        """ + Noise + Rings + """

        const int SURFACE_ROCKY = 1;
        const int SURFACE_VENUS = 2;
        const int SURFACE_EARTH = 3;
        const int SURFACE_MARS = 4;
        const int SURFACE_GAS = 5;
        const int SURFACE_ICE = 6;

        // A field of craters — bowls with bright rims — as a brightness offset around 0.
        float craters(vec3 p, float scale, float coverage)
        {
            vec3 q = p * scale;
            vec3 cell = floor(q);
            vec3 f = q - cell;
            float shade = 0.0;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                ivec3 c = ivec3(cell) + ivec3(x, y, z);
                uint h = hash3u(c + ivec3(int(uSeed * 37.0)));
                float roll = float(h & 255u) / 255.0;
                if (roll > coverage) continue;
                vec3 center = vec3(float(x), float(y), float(z))
                    + vec3(float((h >> 8u) & 255u), float((h >> 16u) & 255u), float((h >> 24u) & 255u)) / 255.0;
                float radius = 0.15 + 0.3 * (roll / coverage);
                float d = length(f - center) / radius;
                if (d < 1.3)
                {
                    float bowl = 1.0 - smoothstep(0.0, 0.92, d);
                    float rim = smoothstep(0.7, 0.97, d) * (1.0 - smoothstep(0.97, 1.3, d));
                    shade += rim * 0.26 - bowl * 0.24;
                }
            }
            return shade;
        }

        vec3 applyStorm(vec3 albedo, vec3 p)
        {
            if (uStorm.w <= 0.0) return albedo;
            float lat = asin(clamp(p.y, -1.0, 1.0));
            float lon = atan(p.z, p.x);
            float dLon = mod(lon - uStorm.y + PI, 2.0 * PI) - PI;
            vec2 d = vec2(dLon * cos(lat), lat - uStorm.x) / vec2(uStorm.z * 1.7, uStorm.z);
            float r = length(d);
            if (r > 1.3) return albedo;
            float a = (1.0 - r) * 4.0 + uTime * 0.25;
            vec2 swirl = vec2(cos(a) * d.x - sin(a) * d.y, sin(a) * d.x + cos(a) * d.y);
            float detail = noise(vec3(swirl * 3.5, uSeed));
            float spot = 1.0 - smoothstep(0.55, 1.0, r);
            float collar = smoothstep(0.75, 0.98, r) * (1.0 - smoothstep(0.98, 1.25, r));
            albedo = mix(albedo, uStormColor * (0.85 + 0.35 * detail), spot * uStorm.w);
            return mix(albedo, uColorC, collar * 0.45 * uStorm.w);
        }

        vec3 rockySurface(vec3 p)
        {
            float plains = smoothstep(0.0, 0.16, fbm(p * 1.7 + uSeed, 4));
            vec3 albedo = mix(uColorA, uColorB, plains);
            albedo *= 0.84 + 0.3 * fbm(p * 9.0 + uSeed * 1.7, 4);
            float shade = craters(p, 4.0, 0.45) + craters(p, 9.0, 0.5) * 0.8 + craters(p, 21.0, 0.55) * 0.5;
            return albedo * (1.0 + shade * mix(1.0, 0.45, plains));
        }

        vec3 venusSurface(vec3 p)
        {
            vec3 q = rotateY(p, uTime * 0.012) * vec3(1.0, 2.4, 1.0);
            vec3 warp = vec3(fbm(q * 1.4 + uSeed, 4), fbm(q * 1.4 + uSeed + 5.2, 4), fbm(q * 1.4 + uSeed + 9.7, 4));
            float n = fbm(q * 2.1 + warp * 1.7, 5);
            vec3 albedo = mix(uColorB, uColorA, smoothstep(-0.3, 0.3, n));
            return mix(albedo, uColorC, smoothstep(0.12, 0.45, n) * 0.55);
        }

        vec3 marsSurface(vec3 p)
        {
            float n = fbm(p * 2.2 + uSeed, 6);
            float dark = smoothstep(0.02, 0.2, fbm(p * 1.3 + uSeed + 7.3, 4));
            vec3 albedo = mix(uColorC, uColorA, smoothstep(-0.25, 0.12, n));
            albedo = mix(albedo, uColorB, dark * 0.7);
            albedo *= 1.0 + craters(p, 7.0, 0.3) * 0.3 + craters(p, 16.0, 0.35) * 0.2;
            float cap = smoothstep(0.925, 0.945, abs(p.y) + 0.03 * noise(p * 10.0));
            return mix(albedo, vec3(0.94, 0.92, 0.9), cap);
        }

        vec3 earthSurface(vec3 p, out float ocean, out float cloud, out float lights)
        {
            float c = fbm(p * 1.5 + uSeed, 7);
            float coast = 0.045;
            float land = smoothstep(coast, coast + 0.01, c);
            float depth = smoothstep(coast, coast - 0.09, c);
            vec3 water = mix(vec3(0.015, 0.08, 0.15), uColorA, depth);

            float lat = abs(p.y);
            float moisture = fbm(p * 3.1 + uSeed + 13.0, 4);
            float subtropics = smoothstep(0.16, 0.28, lat) * (1.0 - smoothstep(0.42, 0.56, lat));
            float dry = clamp((subtropics - moisture * 3.0 - 0.3) * 1.6, 0.0, 1.0);
            vec3 green = uColorB * (0.78 + 0.44 * fbm(p * 13.0, 3));
            vec3 ground = mix(green, uColorC * (0.9 + 0.2 * fbm(p * 9.0, 3)), smoothstep(0.3, 0.7, dry));
            ground = mix(ground, vec3(0.34, 0.33, 0.29), smoothstep(0.6, 0.76, lat) * 0.75);
            ground = mix(ground, vec3(0.36, 0.29, 0.22), smoothstep(0.17, 0.3, c) * 0.35);
            vec3 surface = mix(water, ground, land);

            float ice = smoothstep(0.83, 0.87, lat + 0.06 * fbm(p * 6.0, 3));
            surface = mix(surface, vec3(0.9, 0.93, 0.97), ice);
            ocean = (1.0 - land) * (1.0 - ice);

            float cities = smoothstep(0.62, 0.9, noise(p * 52.0) * 0.5 + 0.5) * smoothstep(0.35, 0.75, noise(p * 7.0 + 4.0) * 0.5 + 0.5);
            lights = cities * land * (1.0 - ice) * (1.0 - smoothstep(0.55, 0.72, lat));

            vec3 cp = rotateY(p, uTime * 0.008);
            vec3 warp = vec3(noise(cp * 3.0 + 1.0), noise(cp * 3.0 + 7.7), noise(cp * 3.0 + 3.3));
            float cn = fbm(cp * 3.0 + warp * 0.9 + uSeed + 31.0, 6);
            cloud = smoothstep(0.03, 0.32, cn) * 0.92;
            return surface;
        }

        vec3 giantSurface(vec3 p)
        {
            float lat = p.y;
            vec3 q = rotateY(p, sin(lat * 9.0 + uSeed) * 0.02 * uTime);
            vec3 stretch = vec3(2.0, 7.0, 2.0);
            vec3 warp = vec3(fbm(q * stretch + uSeed, 4), fbm(q * stretch + uSeed + 3.1, 4), 0.0);
            float turbulence = fbm(q * vec3(3.0, 12.0, 3.0) + warp * 1.5 + uSeed, 5);
            float band = lat * uBandFrequency + turbulence * uTurbulence * 1.4;
            float b1 = 0.5 + 0.5 * sin(band * PI);
            float b2 = 0.5 + 0.5 * sin(band * 7.1 + 1.3);

            // Eddies curl along the edges between belts and zones, where the winds shear hardest.
            float edge = 1.0 - abs(b1 * 2.0 - 1.0);
            float eddies = fbm(q * vec3(14.0, 40.0, 14.0) + warp * 2.0 + uSeed * 1.3, 4);
            vec3 albedo = mix(uColorB, uColorA, smoothstep(0.2, 0.8, b1 + eddies * edge * uTurbulence * 1.1));
            albedo = mix(albedo, uColorC, smoothstep(0.6, 0.95, b2) * 0.4);
            albedo = mix(albedo, uColorB * 0.72, smoothstep(0.82, 1.0, 1.0 - b1) * 0.4);
            albedo *= 0.9 + 0.16 * noise(q * vec3(6.0, 42.0, 6.0)) + eddies * 0.2;
            albedo = applyStorm(albedo, p);
            return mix(albedo, albedo * vec3(0.72, 0.74, 0.8), smoothstep(0.72, 0.98, abs(lat)));
        }

        vec3 iceSurface(vec3 p)
        {
            float lat = p.y;
            vec3 q = rotateY(p, sin(lat * 5.0) * 0.015 * uTime);
            float turbulence = fbm(q * vec3(2.0, 8.0, 2.0) + uSeed, 4);
            float band = 0.5 + 0.5 * sin((lat * uBandFrequency + turbulence * 0.8) * PI);
            vec3 albedo = mix(uColorA, uColorB, band * 0.55);
            float streak = smoothstep(0.58, 0.82, noise(q * vec3(3.0, 24.0, 3.0) + uSeed) * 0.5 + 0.5);
            albedo = mix(albedo, uColorC, streak * uTurbulence);
            return applyStorm(albedo, p);
        }

        void main()
        {
            vec3 p = normalize(vLocal);
            vec3 N = normalize(vWorldPos - uCenter);
            vec3 L = normalize(uSunPos - vWorldPos);
            vec3 V = normalize(uCameraPos - vWorldPos);
            float ndl = dot(N, L);

            float ocean = 0.0;
            float cloud = 0.0;
            float lights = 0.0;
            vec3 albedo;
            if (uSurface == SURFACE_EARTH) albedo = earthSurface(p, ocean, cloud, lights);
            else if (uSurface == SURFACE_VENUS) albedo = venusSurface(p);
            else if (uSurface == SURFACE_MARS) albedo = marsSurface(p);
            else if (uSurface == SURFACE_GAS) albedo = giantSurface(p);
            else if (uSurface == SURFACE_ICE) albedo = iceSurface(p);
            else albedo = rockySurface(p);

            // An atmosphere softens the terminator; airless bodies keep it sharp.
            float wrap = uAtmosphere.w > 0.0 ? 0.1 : 0.015;
            float diffuse = clamp((ndl + wrap) / (1.0 + wrap), 0.0, 1.0);

            // The rings' shadow on the planet: where the ray toward the Sun crosses the ring plane.
            float shadow = 1.0;
            if (uRing.y > 0.0)
            {
                float facing = dot(L, uPole);
                if (abs(facing) > 1e-4)
                {
                    float t = dot(uCenter - vWorldPos, uPole) / facing;
                    if (t > 0.0)
                    {
                        float x = length(vWorldPos + L * t - uCenter) / uRadius;
                        shadow = 1.0 - ringDensity(x, 0.0) * 0.85 * uRingColor.a;
                    }
                }
            }

            // Sunlight reddens toward the terminator, where it crosses the most air.
            float twilight = smoothstep(0.35, 0.0, ndl) * smoothstep(-0.2, 0.05, ndl) * min(uAtmosphere.w * 2.0, 1.0);
            vec3 sun = uSunColor * mix(vec3(1.0), vec3(1.0, 0.62, 0.4), twilight * 0.7);
            vec3 light = sun * diffuse * shadow;
            vec3 color = albedo * (light + uAmbient);

            if (uSurface == SURFACE_EARTH)
            {
                vec3 H = normalize(L + V);
                float glint = pow(max(dot(N, H), 0.0), 80.0) * ocean * (1.0 - cloud) * step(0.0, ndl);
                color += sun * glint * 0.6 * shadow;
                color = mix(color, vec3(0.96) * (light * 1.05 + uAmbient), cloud);
                float night = 1.0 - smoothstep(-0.12, 0.06, ndl);
                color += vec3(1.0, 0.7, 0.36) * lights * night * (1.0 - cloud * 0.85) * 1.6;
            }

            // Haze over the limb, lit where the Sun reaches it.
            float rim = pow(1.0 - max(dot(N, V), 0.0), 3.0);
            float litRim = smoothstep(-0.3, 0.45, ndl);
            color = mix(color, uAtmosphere.rgb * sun * litRim, rim * uAtmosphere.w * 0.7);

            fragColor = vec4(color, 1.0);
        }
        """;

    /// <summary>The Sun: churning granulation, sunspots and limb darkening, bright enough to bloom.</summary>
    public const string SunFragment =
        """
        #version 330 core
        in vec3 vWorldPos;
        in vec3 vLocal;

        uniform vec3 uCenter;
        uniform vec3 uCameraPos;
        uniform vec3 uColorA;
        uniform vec3 uColorB;
        uniform vec3 uColorC;
        uniform float uSeed;
        uniform float uTime;
        uniform float uIntensity;

        out vec4 fragColor;
        """ + Noise + """

        // Granulation: bright convection cells parted by dark lanes — the gap between the nearest and the
        // second-nearest cell center of a jittered grid, which is zero along a lane.
        float granules(vec3 p)
        {
            vec3 cell = floor(p);
            vec3 f = p - cell;
            float d1 = 8.0;
            float d2 = 8.0;
            for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                uint h = hash3u(ivec3(cell) + ivec3(x, y, z));
                vec3 jitter = vec3(float(h & 255u), float((h >> 8u) & 255u), float((h >> 16u) & 255u)) / 255.0;
                vec3 r = vec3(float(x), float(y), float(z)) + jitter - f;
                float d = dot(r, r);
                if (d < d1) { d2 = d1; d1 = d; }
                else if (d < d2) { d2 = d; }
            }
            return sqrt(d2) - sqrt(d1);
        }

        void main()
        {
            vec3 p = normalize(vLocal);
            vec3 N = normalize(vWorldPos - uCenter);
            float mu = clamp(dot(N, normalize(uCameraPos - vWorldPos)), 0.0, 1.0);

            float churn = uTime * 0.04;
            vec3 warp = vec3(noise(p * 6.0 + churn), noise(p * 6.0 + 4.1 - churn), noise(p * 6.0 + 8.3)) * 0.35;

            // Cells smaller than a pixel would only sparkle, so they fade to their average with distance.
            vec3 g = p * 38.0 + warp * 3.0;
            float detail = 1.0 - clamp(length(fwidth(g)) * 0.6, 0.0, 1.0);
            float lanes = mix(0.7, smoothstep(0.0, 0.3, granules(g)), detail);
            float large = fbm(p * 4.0 + warp + vec3(churn * 0.3), 4);
            float spots = smoothstep(0.2, 0.3, fbm(p * 2.3 + uSeed + vec3(0.0, 0.0, uTime * 0.002), 4))
                * (1.0 - smoothstep(0.3, 0.55, abs(p.y)));

            vec3 color = mix(uColorB, uColorA, 0.35 + 0.65 * pow(mu, 0.6));
            float brightness = (0.6 + 0.4 * lanes) * (0.88 + 0.4 * large);
            brightness *= 0.32 + 0.68 * pow(mu, 0.5);
            color = mix(color * brightness, uColorC * brightness * 0.5, spots * 0.85);
            color += uColorA * smoothstep(0.08, 0.3, large) * (1.0 - mu) * 0.25;

            fragColor = vec4(color * uIntensity, 1.0);
        }
        """;

    /// <summary>The rings: radial structure, lit by the Sun, in the planet's shadow where it falls on them.</summary>
    public const string RingFragment =
        """
        #version 330 core
        in vec3 vWorldPos;
        in vec3 vLocal;

        uniform vec3 uCenter;
        uniform float uRadius;
        uniform vec3 uCameraPos;
        uniform vec3 uSunPos;
        uniform vec3 uSunColor;
        uniform vec3 uAmbient;
        uniform vec3 uPole;
        uniform vec4 uRingColor;
        uniform float uSeed;

        out vec4 fragColor;
        """ + Noise + Rings + """

        void main()
        {
            float x = length(vWorldPos - uCenter) / uRadius;
            float detail = 1.0 - clamp(fwidth(x) * 300.0, 0.0, 1.0);
            float density = ringDensity(x, detail);
            if (density <= 0.003) discard;

            vec3 L = normalize(uSunPos - vWorldPos);
            vec3 V = normalize(uCameraPos - vWorldPos);

            // The planet's shadow: the ray toward the Sun passes within one radius of its center.
            vec3 oc = vWorldPos - uCenter;
            float along = dot(oc, L);
            float miss = sqrt(max(dot(oc, oc) - along * along, 0.0));
            float shadow = along < 0.0 ? smoothstep(uRadius * 0.98, uRadius * 1.03, miss) : 1.0;

            // Seen from the unlit side, light only comes through where the rings are thin.
            float sunSide = dot(uPole, L);
            float through = sunSide * dot(uPole, V) >= 0.0 ? 1.0 : 0.25 + 0.6 * (1.0 - density);
            float tone = 0.84 + 0.32 * noise(vec3(x * 23.0 + uSeed, 3.0, 1.0));
            vec3 albedo = uRingColor.rgb * tone * mix(0.55, 1.0, smoothstep(uRing.x + 0.2, uRing.x + 0.45, x));
            vec3 color = albedo * (uSunColor * (0.3 + 0.7 * abs(sunSide)) * shadow * through + uAmbient);

            fragColor = vec4(color, density * uRingColor.a);
        }
        """;

    /// <summary>
    /// An atmosphere's glow past the limb: brightest along rays that graze the surface, lit on the day side and
    /// blazing when the Sun is behind the planet.
    /// </summary>
    public const string AtmosphereFragment =
        """
        #version 330 core
        in vec3 vWorldPos;
        in vec2 vQuad;

        uniform vec3 uCenter;
        uniform float uRadius;
        uniform float uOuterRadius;
        uniform vec3 uCameraPos;
        uniform vec3 uSunPos;
        uniform vec3 uSunColor;
        uniform vec4 uAtmosphere;

        out vec4 fragColor;

        void main()
        {
            vec3 dir = normalize(vWorldPos - uCameraPos);
            vec3 closest = uCameraPos + dir * dot(uCenter - uCameraPos, dir);
            float h = length(closest - uCenter);
            if (h >= uOuterRadius) discard;

            float path = sqrt(max(uOuterRadius * uOuterRadius - h * h, 0.0));
            float graze = sqrt(max(uOuterRadius * uOuterRadius - uRadius * uRadius, 1e-6));
            float depth = clamp(path / graze, 0.0, 1.0);

            vec3 n = normalize(closest - uCenter);
            vec3 L = normalize(uSunPos - uCenter);
            float facing = dot(n, L);
            float lit = smoothstep(-0.35, 0.5, facing);
            float backlit = pow(max(dot(dir, L), 0.0), 8.0) * smoothstep(-0.7, 0.0, facing);
            float glow = pow(depth, 2.4) * (lit + backlit * 2.0);

            fragColor = vec4(uAtmosphere.rgb * uSunColor * glow * uAtmosphere.w, 1.0);
        }
        """;

    /// <summary>The corona: a hot rim, then streamers fading into a wide halo.</summary>
    public const string CoronaFragment =
        """
        #version 330 core
        in vec3 vWorldPos;
        in vec2 vQuad;

        uniform vec3 uCenter;
        uniform float uRadius;
        uniform vec3 uCameraPos;
        uniform vec3 uColorA;
        uniform vec3 uColorB;
        uniform float uTime;
        uniform float uIntensity;

        out vec4 fragColor;
        """ + Noise + """

        void main()
        {
            vec3 dir = normalize(vWorldPos - uCameraPos);
            vec3 closest = uCameraPos + dir * dot(uCenter - uCameraPos, dir);
            float r = max(length(closest - uCenter) / uRadius - 1.0, 0.0);

            float angle = atan(vQuad.y, vQuad.x);
            vec3 around = vec3(cos(angle) * 2.2, sin(angle) * 2.2, uTime * 0.025 - r * 0.35);
            float streamers = 0.55 + 1.1 * max(fbm(around, 4) + 0.12, 0.0);

            float rim = exp(-r * 10.0);
            float glow = exp(-r * 2.6) * 0.45 * streamers;
            float halo = exp(-r * 0.9) * 0.03;
            float fade = 1.0 - smoothstep(0.7, 1.0, length(vQuad));

            vec3 color = mix(uColorA, uColorB, smoothstep(0.0, 1.6, r));
            fragColor = vec4(color * (rim * 1.4 + glow + halo) * uIntensity * fade, 1.0);
        }
        """;

    /// <summary>
    /// The sky: three layers of hashed stars (point-sized whatever the resolution, tinted by temperature), the
    /// Milky Way with its dust lanes, and faint nebulae.
    /// </summary>
    public const string SkyFragment =
        """
        #version 330 core
        in vec2 vTexCoord;

        uniform mat4 uInverseViewProjection;
        uniform vec3 uCameraPos;
        uniform float uPixelAngle;
        uniform float uTime;

        out vec4 fragColor;
        """ + Noise + """

        vec3 starTint(float t)
        {
            if (t < 0.15) return vec3(1.0, 0.68, 0.48);
            if (t < 0.4) return vec3(1.0, 0.88, 0.74);
            if (t < 0.8) return vec3(1.0, 0.98, 0.96);
            return vec3(0.72, 0.82, 1.0);
        }

        vec3 faceDirection(int face, vec2 uv)
        {
            if (face == 0) return vec3(1.0, uv.x, uv.y);
            if (face == 1) return vec3(-1.0, uv.x, uv.y);
            if (face == 2) return vec3(uv.x, 1.0, uv.y);
            if (face == 3) return vec3(uv.x, -1.0, uv.y);
            if (face == 4) return vec3(uv.x, uv.y, 1.0);
            return vec3(uv.x, uv.y, -1.0);
        }

        // One layer of stars: a jittered point per grid cell of a cube map, kept with probability `density`.
        vec3 stars(vec3 d, float cells, float density, float size, float brightness, int layer)
        {
            vec3 a = abs(d);
            int face;
            vec2 uv;
            if (a.x >= a.y && a.x >= a.z) { face = d.x > 0.0 ? 0 : 1; uv = d.yz / a.x; }
            else if (a.y >= a.z) { face = d.y > 0.0 ? 2 : 3; uv = d.xz / a.y; }
            else { face = d.z > 0.0 ? 4 : 5; uv = d.xy / a.z; }

            vec2 g = (uv * 0.5 + 0.5) * cells;
            vec2 cell = floor(g);
            vec3 sum = vec3(0.0);
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                vec2 c = cell + vec2(float(x), float(y));
                uint h = hash3u(ivec3(int(c.x), int(c.y), face * 131 + layer * 1031));
                float roll = float(h & 1023u) / 1023.0;
                if (roll >= density) continue;
                vec2 at = c + vec2(float((h >> 10u) & 255u), float((h >> 18u) & 255u)) / 255.0;
                vec3 star = normalize(faceDirection(face, at / cells * 2.0 - 1.0));
                float pixels = length(cross(d, star)) / uPixelAngle;
                float magnitude = float((h >> 26u) & 63u) / 63.0;
                float twinkle = 0.88 + 0.12 * sin(uTime * (1.3 + magnitude * 2.7) + float(h & 4095u));
                float core = exp(-pixels * pixels / (2.0 * size * size));
                sum += starTint(roll / density) * brightness * (0.04 + pow(magnitude, 6.0)) * core * twinkle;
            }
            return sum;
        }

        void main()
        {
            vec4 far = uInverseViewProjection * vec4(vTexCoord * 2.0 - 1.0, 1.0, 1.0);
            vec3 d = normalize(far.xyz / far.w - uCameraPos);

            vec3 pole = normalize(vec3(0.32, 0.86, 0.4));
            vec3 core = normalize(vec3(-0.75, 0.12, -0.65));
            float lat = dot(d, pole);
            float band = exp(-lat * lat / 0.05);
            float narrow = exp(-lat * lat / 0.006);

            float n1 = fbm(d * 3.0 + 2.0, 5);
            float n2 = fbm(d * 7.5 + 5.0, 4);
            vec3 milky = mix(vec3(0.3, 0.34, 0.48), vec3(0.62, 0.5, 0.38), smoothstep(-0.2, 0.3, n1)) * band * (0.55 + n1);
            milky += vec3(0.75, 0.6, 0.42) * pow(max(dot(d, core), 0.0), 5.0) * band * 0.9;
            milky *= 1.0 - smoothstep(-0.05, 0.25, n2) * narrow * 0.85;

            vec3 nebula = vec3(0.4, 0.16, 0.5) * smoothstep(0.08, 0.5, fbm(d * 1.7 + 9.0, 4))
                + vec3(0.1, 0.28, 0.45) * smoothstep(0.12, 0.55, fbm(d * 2.2 + 3.3, 4));

            vec3 color = vec3(0.0012, 0.0016, 0.0032) + max(milky, 0.0) * 0.03 + nebula * 0.012;
            color += stars(d, 230.0, 0.1, 0.6, 0.35, 0);
            color += stars(d, 70.0, 0.22, 0.75, 2.4, 1);
            color += stars(d, 420.0, 0.4 * band, 0.55, 0.16, 2);

            fragColor = vec4(color, 1.0);
        }
        """;
}

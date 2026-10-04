namespace Voxelcraft;

/// <summary>
/// Seeded Perlin gradient noise in 2D and 3D (about -1..1) with fractal sums. Immutable after construction, so
/// the world generator's worker threads share one instance.
/// </summary>
public sealed class Noise
{
    private readonly byte[] _perm = new byte[512];

    public Noise(int seed)
    {
        var random = new Random(seed);
        byte[] p = new byte[256];
        for (int i = 0; i < 256; i++) p[i] = (byte)i;
        for (int i = 255; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }

        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    public float Sample(float x, float y)
    {
        int xi = FastFloor(x);
        int yi = FastFloor(y);
        float xf = x - xi;
        float yf = y - yi;
        xi &= 255;
        yi &= 255;
        float u = Fade(xf);
        float v = Fade(yf);

        int aa = _perm[_perm[xi] + yi];
        int ab = _perm[_perm[xi] + yi + 1];
        int ba = _perm[_perm[xi + 1] + yi];
        int bb = _perm[_perm[xi + 1] + yi + 1];

        float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
        float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
        return Lerp(x1, x2, v) * 1.41f;
    }

    public float Sample(float x, float y, float z)
    {
        int xi = FastFloor(x);
        int yi = FastFloor(y);
        int zi = FastFloor(z);
        float xf = x - xi;
        float yf = y - yi;
        float zf = z - zi;
        xi &= 255;
        yi &= 255;
        zi &= 255;
        float u = Fade(xf);
        float v = Fade(yf);
        float w = Fade(zf);

        int a = _perm[xi] + yi;
        int aa = _perm[a] + zi;
        int ab = _perm[a + 1] + zi;
        int b = _perm[xi + 1] + yi;
        int ba = _perm[b] + zi;
        int bb = _perm[b + 1] + zi;

        float x1 = Lerp(Grad(_perm[aa], xf, yf, zf), Grad(_perm[ba], xf - 1, yf, zf), u);
        float x2 = Lerp(Grad(_perm[ab], xf, yf - 1, zf), Grad(_perm[bb], xf - 1, yf - 1, zf), u);
        float y1 = Lerp(x1, x2, v);
        x1 = Lerp(Grad(_perm[aa + 1], xf, yf, zf - 1), Grad(_perm[ba + 1], xf - 1, yf, zf - 1), u);
        x2 = Lerp(Grad(_perm[ab + 1], xf, yf - 1, zf - 1), Grad(_perm[bb + 1], xf - 1, yf - 1, zf - 1), u);
        float y2 = Lerp(x1, x2, v);
        return Lerp(y1, y2, w);
    }

    /// <summary>A fractal sum of 2D octaves, each twice the frequency and half the amplitude of the last.</summary>
    public float Fractal(float x, float y, int octaves, float lacunarity = 2.0f, float gain = 0.5f)
    {
        float sum = 0.0f;
        float amplitude = 1.0f;
        float norm = 0.0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Sample(x, y) * amplitude;
            norm += amplitude;
            x = x * lacunarity + 31.7f;
            y = y * lacunarity - 17.3f;
            amplitude *= gain;
        }

        return sum / norm;
    }

    /// <summary>Ridged fractal noise: sharp crests where the noise crosses zero, in 0..1.</summary>
    public float Ridged(float x, float y, int octaves)
    {
        float sum = 0.0f;
        float amplitude = 1.0f;
        float norm = 0.0f;
        float weight = 1.0f;
        for (int i = 0; i < octaves; i++)
        {
            float n = 1.0f - MathF.Abs(Sample(x, y));
            n *= n * weight;
            weight = Math.Clamp(n * 1.5f, 0.0f, 1.0f);
            sum += n * amplitude;
            norm += amplitude;
            x = x * 2.03f + 11.1f;
            y = y * 2.03f - 7.7f;
            amplitude *= 0.5f;
        }

        return sum / norm;
    }

    private static int FastFloor(float x)
    {
        int i = (int)x;
        return x < i ? i - 1 : i;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static float Lerp(float a, float b, float t) => a + t * (b - a);

    private static float Grad(int hash, float x, float y) => (hash & 7) switch
    {
        0 => x + y,
        1 => -x + y,
        2 => x - y,
        3 => -x - y,
        4 => x,
        5 => -x,
        6 => y,
        _ => -y,
    };

    private static float Grad(int hash, float x, float y, float z)
    {
        int h = hash & 15;
        float u = h < 8 ? x : y;
        float v = h < 4 ? y : h is 12 or 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}

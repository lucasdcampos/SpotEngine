using System.Numerics;
using Spot.Framework.Audio;

namespace ProvingGrounds;

/// <summary>
/// Every sound in the game, synthesized at startup from noise, sine sweeps and simple filters — no audio files. Each
/// is a mono <see cref="AudioClip"/> built straight from PCM samples, so it plays flat or positioned in the world like
/// any imported clip.
/// </summary>
internal static class Sfx
{
    private const int Rate = 44100;

    private static bool s_built;

    public static AudioClip RifleShot { get; private set; } = null!;
    public static AudioClip LauncherShot { get; private set; } = null!;
    public static AudioClip Explosion { get; private set; } = null!;
    public static AudioClip Impact { get; private set; } = null!;
    public static AudioClip MetalHit { get; private set; } = null!;
    public static AudioClip Thud { get; private set; } = null!;
    public static AudioClip HitTick { get; private set; } = null!;
    public static AudioClip KillTick { get; private set; } = null!;
    public static AudioClip Bullseye { get; private set; } = null!;
    public static AudioClip TargetDown { get; private set; } = null!;
    public static AudioClip MagOut { get; private set; } = null!;
    public static AudioClip MagIn { get; private set; } = null!;
    public static AudioClip Chamber { get; private set; } = null!;
    public static AudioClip DryFire { get; private set; } = null!;
    public static AudioClip Switch { get; private set; } = null!;
    public static AudioClip Footstep { get; private set; } = null!;
    public static AudioClip Land { get; private set; } = null!;
    public static AudioClip JumpPad { get; private set; } = null!;
    public static AudioClip UiHover { get; private set; } = null!;
    public static AudioClip UiClick { get; private set; } = null!;
    public static AudioClip Beep { get; private set; } = null!;
    public static AudioClip Go { get; private set; } = null!;
    public static AudioClip Fanfare { get; private set; } = null!;
    public static AudioClip Zone { get; private set; } = null!;
    public static AudioClip DroneHum { get; private set; } = null!;
    public static AudioClip Wind { get; private set; } = null!;

    /// <summary>Synthesizes every clip once per process; later calls do nothing.</summary>
    public static void Build()
    {
        if (s_built) return;
        s_built = true;

        var random = new Random(7);
        RifleShot = Clip(0.42f, 0.9f, (t, s) =>
        {
            float crack = s.HighNoise(1, 2200.0f) * Decay(t, 0.010f) * 1.3f;
            float body = s.LowNoise(2, 2400.0f) * Decay(t, 0.05f);
            float thump = s.Sine(0, Sweep(t, 170.0f, 52.0f, 0.06f)) * Decay(t, 0.06f) * 1.1f;
            float zap = s.Sine(1, Sweep(t, 1500.0f, 260.0f, 0.04f)) * Decay(t, 0.035f) * 0.22f;
            float tail = s.LowNoise(3, 700.0f) * Decay(t, 0.16f) * 0.55f;
            return MathF.Tanh((crack + body + thump + zap + tail) * 1.6f);
        });

        LauncherShot = Clip(0.6f, 0.9f, (t, s) =>
        {
            float thoomp = s.Sine(0, Sweep(t, 240.0f, 62.0f, 0.12f)) * Decay(t, 0.13f) * 1.2f;
            float puff = s.LowNoise(1, 900.0f) * Decay(t, 0.07f);
            float hiss = s.HighNoise(2, 3000.0f) * Decay(t, 0.025f) * 0.35f;
            float ring = s.Sine(1, 520.0f) * Decay(t, 0.18f) * 0.12f;
            return MathF.Tanh((thoomp + puff + hiss + ring) * 1.4f);
        });

        Explosion = Clip(2.2f, 0.95f, (t, s) =>
        {
            float attack = MathF.Min(1.0f, t / 0.004f);
            float rumble = s.LowNoise(0, 380.0f) * Decay(t, 0.55f) * 2.2f;
            float blast = s.LowNoise(1, 2200.0f) * Decay(t, 0.09f) * 1.4f;
            float boom = s.Sine(0, Sweep(t, 90.0f, 34.0f, 0.4f)) * Decay(t, 0.35f) * 1.4f;
            float crackle = s.Crackle(random, 0.0035f * Decay(t, 0.4f)) * 0.8f;
            return MathF.Tanh((rumble + blast + boom + crackle) * attack * 1.3f);
        });

        Impact = Clip(0.12f, 0.6f, (t, s) => (s.HighNoise(0, 1800.0f) * Decay(t, 0.009f) + s.LowNoise(1, 1200.0f) * Decay(t, 0.02f) * 0.6f));

        MetalHit = Clip(0.5f, 0.6f, (t, s) =>
            s.Sine(0, 830.0f) * Decay(t, 0.12f) * 0.6f + s.Sine(1, 2210.0f) * Decay(t, 0.07f) * 0.45f
            + s.Sine(2, 3490.0f) * Decay(t, 0.04f) * 0.3f + s.HighNoise(0, 2500.0f) * Decay(t, 0.006f));

        Thud = Clip(0.25f, 0.7f, (t, s) => s.LowNoise(0, 420.0f) * Decay(t, 0.035f) * 1.6f + s.Sine(0, Sweep(t, 140.0f, 70.0f, 0.05f)) * Decay(t, 0.05f));

        HitTick = Clip(0.08f, 0.45f, (t, s) => (s.Sine(0, 2350.0f) + 0.5f * s.Sine(1, 3520.0f)) * Decay(t, 0.016f));

        KillTick = Clip(0.26f, 0.5f, (t, s) =>
        {
            float first = (s.Sine(0, 1760.0f) + 0.4f * s.Sine(1, 3520.0f)) * Decay(t, 0.03f);
            float second = t < 0.06f ? 0.0f : (s.Sine(2, 2637.0f) + 0.3f * s.Sine(3, 5274.0f)) * Decay(t - 0.06f, 0.07f);
            return first + second;
        });

        // A bell: inharmonic partials, each fading at its own rate.
        Bullseye = Clip(1.2f, 0.5f, (t, s) =>
            s.Sine(0, 1568.0f) * Decay(t, 0.45f) + 0.55f * s.Sine(1, 3136.0f) * Decay(t, 0.25f)
            + 0.4f * s.Sine(2, 4328.0f) * Decay(t, 0.16f) + 0.25f * s.Sine(3, 8467.0f) * Decay(t, 0.06f));

        TargetDown = Clip(0.45f, 0.6f, (t, s) =>
        {
            float clank = s.Sine(0, 410.0f) * Decay(t, 0.09f) + 0.6f * s.Sine(1, 1130.0f) * Decay(t, 0.05f);
            float knock = t < 0.16f ? 0.0f : s.LowNoise(0, 600.0f) * Decay(t - 0.16f, 0.025f) * 1.4f;
            return clank + knock + s.HighNoise(0, 3000.0f) * Decay(t, 0.004f);
        });

        MagOut = Clip(0.14f, 0.5f, (t, s) => s.HighNoise(0, 2000.0f) * Decay(t, 0.006f) + s.Sine(0, 950.0f) * Decay(t, 0.02f) * 0.4f);
        MagIn = Clip(0.2f, 0.6f, (t, s) =>
            s.LowNoise(0, 900.0f) * Decay(t, 0.02f) * 1.4f + s.Sine(0, 230.0f) * Decay(t, 0.04f) * 0.7f + s.HighNoise(0, 2600.0f) * Decay(t, 0.004f));
        Chamber = Clip(0.24f, 0.55f, (t, s) =>
        {
            float back = s.HighNoise(0, 1800.0f) * Decay(t, 0.008f) + s.Sine(0, 700.0f) * Decay(t, 0.02f) * 0.4f;
            float forth = t < 0.09f ? 0.0f : s.HighNoise(1, 1500.0f) * Decay(t - 0.09f, 0.01f) * 1.2f + s.Sine(1, 520.0f) * Decay(t - 0.09f, 0.03f) * 0.5f;
            return back + forth;
        });
        DryFire = Clip(0.06f, 0.35f, (t, s) => s.HighNoise(0, 3000.0f) * Decay(t, 0.004f));
        Switch = Clip(0.18f, 0.4f, (t, s) => s.LowNoise(0, 1400.0f) * Decay(t, 0.03f) + s.Sine(0, Sweep(t, 600.0f, 1200.0f, 0.1f)) * Decay(t, 0.05f) * 0.3f);

        Footstep = Clip(0.14f, 0.35f, (t, s) => s.LowNoise(0, 520.0f) * Decay(t, 0.028f) * 1.5f + s.HighNoise(0, 3000.0f) * Decay(t, 0.006f) * 0.25f);
        Land = Clip(0.3f, 0.55f, (t, s) => s.LowNoise(0, 380.0f) * Decay(t, 0.05f) * 1.6f + s.Sine(0, Sweep(t, 110.0f, 55.0f, 0.08f)) * Decay(t, 0.07f));

        // Noise through a resonant band-pass sweeping upward: a rising whoosh.
        JumpPad = Clip(0.8f, 0.6f, (t, s) =>
        {
            float envelope = MathF.Min(1.0f, t / 0.03f) * Decay(t, 0.22f);
            float whoosh = s.BandNoise(0, 300.0f + 2600.0f * MathF.Min(1.0f, t / 0.5f), 0.12f);
            float rise = s.Sine(0, Sweep(t, 180.0f, 900.0f, 0.35f)) * 0.35f;
            return (whoosh * 1.8f + rise) * envelope;
        });

        UiHover = Clip(0.04f, 0.18f, (t, s) => s.Sine(0, 1650.0f) * Decay(t, 0.01f));
        UiClick = Clip(0.09f, 0.3f, (t, s) => (s.Sine(0, 980.0f) + 0.5f * s.Sine(1, 1960.0f)) * Decay(t, 0.02f));
        Beep = Clip(0.16f, 0.4f, (t, s) => s.Sine(0, 880.0f) * Decay(t, 0.08f) * MathF.Min(1.0f, t / 0.004f));
        Go = Clip(0.5f, 0.45f, (t, s) => (s.Sine(0, 1318.5f) + s.Sine(1, 1760.0f) * 0.7f + s.Sine(2, 2637.0f) * 0.3f) * Decay(t, 0.18f) * MathF.Min(1.0f, t / 0.004f));
        Fanfare = Clip(1.3f, 0.45f, (t, s) =>
        {
            float sum = 0.0f;
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            for (int i = 0; i < notes.Length; i++)
            {
                float start = i * 0.11f;
                if (t >= start) sum += s.Sine(i, notes[i]) * Decay(t - start, i == notes.Length - 1 ? 0.35f : 0.12f);
            }

            return sum;
        });
        Zone = Clip(0.7f, 0.25f, (t, s) => (s.Sine(0, 659.25f) * Decay(t, 0.2f) + (t < 0.09f ? 0.0f : s.Sine(1, 987.77f) * Decay(t - 0.09f, 0.25f))) * MathF.Min(1.0f, t / 0.01f));

        // Loops: whole cycles of every partial in one second, so the end meets the start without a click.
        DroneHum = Clip(1.0f, 0.5f, (t, s) =>
        {
            float wobble = 0.75f + 0.25f * MathF.Sin(MathF.Tau * 6.0f * t);
            return (s.Sine(0, 110.0f) * 0.6f + s.Sine(1, 220.0f) * 0.35f + s.Sine(2, 330.0f) * 0.2f + s.Sine(3, 1320.0f) * 0.05f) * wobble;
        });
        Wind = Loop(6.0f, 0.22f, (t, s) => s.LowNoise(0, 260.0f + 120.0f * MathF.Sin(t * 0.9f)) * (0.7f + 0.3f * MathF.Sin(t * 1.3f)));
    }

    // e^(-t/tau): the classic percussive fade.
    private static float Decay(float t, float tau) => MathF.Exp(-t / tau);

    // A frequency that glides exponentially from 'from' toward 'to' with the given time constant.
    private static float Sweep(float t, float from, float to, float tau) => to + (from - to) * MathF.Exp(-t / tau);

    private static AudioClip Clip(float seconds, float peak, Func<float, Synth, float> sample)
    {
        int count = (int)(seconds * Rate);
        var buffer = new float[count];
        var synth = new Synth();
        for (int i = 0; i < count; i++)
        {
            buffer[i] = sample(i / (float)Rate, synth);
            synth.Advance();
        }

        // A short fade at the end so a clip cut mid-tail never clicks.
        int fade = Math.Min(count, Rate / 200);
        for (int i = 0; i < fade; i++)
        {
            buffer[count - 1 - i] *= i / (float)fade;
        }

        return ToClip(buffer, peak);
    }

    // A seamless loop: render a little extra and cross-fade it over the start.
    private static AudioClip Loop(float seconds, float peak, Func<float, Synth, float> sample)
    {
        int count = (int)(seconds * Rate);
        int overlap = Rate / 2;
        var buffer = new float[count + overlap];
        var synth = new Synth();
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = sample(i / (float)Rate, synth);
            synth.Advance();
        }

        var loop = new float[count];
        Array.Copy(buffer, loop, count);
        for (int i = 0; i < overlap; i++)
        {
            float w = i / (float)overlap;
            loop[i] = loop[i] * w + buffer[count + i] * (1.0f - w);
        }

        return ToClip(loop, peak);
    }

    private static AudioClip ToClip(float[] buffer, float peak)
    {
        float max = 1e-6f;
        foreach (float v in buffer) max = MathF.Max(max, MathF.Abs(v));
        float gain = peak / max;

        var pcm = new short[buffer.Length];
        for (int i = 0; i < buffer.Length; i++)
        {
            pcm[i] = (short)Math.Clamp(buffer[i] * gain * short.MaxValue, short.MinValue, short.MaxValue);
        }

        return new AudioClip(pcm, 1, Rate);
    }

    /// <summary>
    /// Per-clip oscillator and filter state: a handful of numbered sine phases and noise filters, so one sample
    /// function can layer several voices without the layers sharing state.
    /// </summary>
    private sealed class Synth
    {
        private const int Voices = 6;

        private readonly float[] _phase = new float[Voices];
        private readonly float[] _low = new float[Voices];
        private readonly float[] _lowForHigh = new float[Voices];
        private readonly float[] _band = new float[Voices];
        private readonly float[] _bandLow = new float[Voices];
        private readonly Random _noise = new(1234);
        private float _white;

        public void Advance() => _white = (float)(_noise.NextDouble() * 2.0 - 1.0);

        public float Sine(int voice, float frequency)
        {
            _phase[voice] += MathF.Tau * frequency / Rate;
            if (_phase[voice] > MathF.Tau) _phase[voice] -= MathF.Tau;
            return MathF.Sin(_phase[voice]);
        }

        // One-pole low-passed white noise: darker as the cutoff drops.
        public float LowNoise(int voice, float cutoff)
        {
            _low[voice] += OnePole(cutoff) * (_white - _low[voice]);
            return _low[voice] * 2.5f;
        }

        // White noise minus its low-passed copy: the hiss and crack above the cutoff.
        public float HighNoise(int voice, float cutoff)
        {
            _lowForHigh[voice] += OnePole(cutoff) * (_white - _lowForHigh[voice]);
            return _white - _lowForHigh[voice];
        }

        // A state-variable band-pass around a (possibly moving) center frequency.
        public float BandNoise(int voice, float center, float damping)
        {
            float f = 2.0f * MathF.Sin(MathF.PI * MathF.Min(center, Rate * 0.2f) / Rate);
            float high = _white - _bandLow[voice] - damping * _band[voice];
            _band[voice] += f * high;
            _bandLow[voice] += f * _band[voice];
            return _band[voice];
        }

        // Sparse random clicks, each a single loud sample, for the crackle of debris.
        public float Crackle(Random random, float probability) =>
            random.NextDouble() < probability ? (float)(random.NextDouble() * 2.0 - 1.0) * 3.0f : 0.0f;

        private static float OnePole(float cutoff) => 1.0f - MathF.Exp(-MathF.Tau * cutoff / Rate);
    }

    /// <summary>Plays a clip flat with a little pitch variation, so repeats don't sound mechanical.</summary>
    public static void Play(AudioClip clip, float volume = 1.0f, float pitchJitter = 0.0f, float pitch = 1.0f, string? bus = null)
    {
        float p = pitch * (1.0f + (Random.Shared.NextSingle() * 2.0f - 1.0f) * pitchJitter);
        Audio.Play(clip, volume, p, bus);
    }

    /// <summary>Plays a clip at a world position; <paramref name="reach"/> is the distance it stays at full level.</summary>
    public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1.0f, float reach = 4.0f, float pitchJitter = 0.0f)
    {
        float p = 1.0f + (Random.Shared.NextSingle() * 2.0f - 1.0f) * pitchJitter;
        AudioManager.Play(clip, volume, p, loop: false, spatial: true, position: position, minDistance: reach,
            maxDistance: 160.0f, bus: AudioMixer.SfxBus);
    }
}

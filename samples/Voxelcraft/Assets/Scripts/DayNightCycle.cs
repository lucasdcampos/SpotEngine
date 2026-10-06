using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Engine;

namespace Voxelcraft;

/// <summary>
/// The clock of the world: the sun rises in the east, crosses the southern sky and sets in the west, the moon
/// follows half a day behind, and the sky, the fog, the sunlight and the ambient light are derived from where they
/// are. Everything here is linear-light color for the shaders; the post-processing stack tone-maps it.
/// </summary>
public sealed class DayNightCycle : Component
{
    /// <summary>Gets or sets how long a full day lasts, in real minutes.</summary>
    [InspectorRange(0.5f, 60.0f, 0.5f)]
    public float DayLengthMinutes { get; set; } = 14.0f;

    /// <summary>Gets or sets the time of day the world starts at: 0 midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset.</summary>
    [InspectorRange(0.0f, 1.0f, 0.01f)]
    public float StartTime { get; set; } = 0.31f;

    /// <summary>Gets or sets how much faster time runs while the time-lapse is on.</summary>
    [InspectorRange(2.0f, 200.0f, 1.0f)]
    public float TimeLapseSpeed { get; set; } = 40.0f;

    /// <summary>Gets or sets how far from straight overhead the sun's path leans to the south, in degrees.</summary>
    [InspectorRange(0.0f, 60.0f, 1.0f)]
    public float SunTilt { get; set; } = 28.0f;

    public static DayNightCycle? Current { get; private set; }

    /// <summary>Gets or sets the time of day, 0..1.</summary>
    public float TimeOfDay { get; set; }

    public bool TimeLapse { get; set; }

    /// <summary>Gets the number of whole days since the world began.</summary>
    public int Day { get; private set; }

    public Vector3 SunDirection { get; private set; } = Vector3.UnitY;

    public Vector3 MoonDirection { get; private set; } = -Vector3.UnitY;

    /// <summary>Gets the direction to whichever of the sun or the moon lights the world and casts the shadows.</summary>
    public Vector3 LightDirection { get; private set; } = Vector3.UnitY;

    /// <summary>Gets the color and strength of that light.</summary>
    public Vector3 LightColor { get; private set; }

    /// <summary>Gets the light the open sky adds from above, and from the horizon.</summary>
    public Vector3 AmbientSky { get; private set; }

    public Vector3 AmbientHorizon { get; private set; }

    public Vector3 Zenith { get; private set; }

    public Vector3 Horizon { get; private set; }

    /// <summary>Gets the warm glow around the sun near sunrise and sunset, 0..1.</summary>
    public float Twilight { get; private set; }

    /// <summary>Gets how much of the day is out: 0 at night, 1 in full daylight.</summary>
    public float Daylight { get; private set; }

    /// <summary>Gets how visible the stars are, 0..1.</summary>
    public float Stars { get; private set; }

    /// <summary>Gets a clock time such as "06:42".</summary>
    public string Clock
    {
        get
        {
            int minutes = (int)(TimeOfDay * 24.0f * 60.0f) % (24 * 60);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }
    }

    public override void OnStart()
    {
        Current = this;
        TimeOfDay = StartTime;
        Evaluate();
    }

    public override void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        float speed = TimeLapse ? TimeLapseSpeed : 1.0f;
        TimeOfDay += deltaTime * speed / MathF.Max(DayLengthMinutes * 60.0f, 1.0f);
        if (TimeOfDay >= 1.0f)
        {
            TimeOfDay -= 1.0f;
            Day++;
        }

        Evaluate();
    }

    /// <summary>Jumps forward to the next of sunrise, noon, sunset and midnight.</summary>
    public void SkipToNextPhase()
    {
        float next = (MathF.Floor(TimeOfDay * 4.0f + 0.02f) + 1.0f) / 4.0f + 0.01f;
        if (next >= 1.0f)
        {
            next -= 1.0f;
            Day++;
        }

        TimeOfDay = next;
        Evaluate();
    }

    private void Evaluate()
    {
        float angle = (TimeOfDay - 0.25f) * MathF.Tau;
        float tilt = SunTilt * MathF.PI / 180.0f;
        SunDirection = Vector3.Normalize(new Vector3(MathF.Cos(angle), MathF.Sin(angle) * MathF.Cos(tilt), MathF.Sin(angle) * MathF.Sin(tilt)));
        MoonDirection = -SunDirection;

        float sun = SunDirection.Y;
        Daylight = SmoothStep(-0.14f, 0.22f, sun);
        Twilight = MathF.Exp(-MathF.Pow(sun / 0.2f, 2.0f)) * SmoothStep(-0.32f, -0.05f, sun);
        Stars = 1.0f - SmoothStep(-0.18f, 0.06f, sun);

        // Sunlight warms and weakens toward the horizon; the moon takes over below it.
        var noon = new Vector3(1.0f, 0.95f, 0.86f);
        var low = new Vector3(1.0f, 0.5f, 0.22f);
        Vector3 sunColor = Vector3.Lerp(low, noon, SmoothStep(0.0f, 0.45f, sun)) * (2.0f * SmoothStep(-0.03f, 0.14f, sun));
        Vector3 moonColor = new Vector3(0.42f, 0.55f, 0.9f) * (0.2f * SmoothStep(-0.03f, 0.14f, MoonDirection.Y));
        if (sun > -0.02f)
        {
            LightDirection = SunDirection;
            LightColor = sunColor;
        }
        else
        {
            LightDirection = MoonDirection;
            LightColor = moonColor;
        }

        var dayZenith = new Vector3(0.14f, 0.34f, 0.82f);
        var dayHorizon = new Vector3(0.5f, 0.66f, 0.92f);
        var nightZenith = new Vector3(0.002f, 0.0035f, 0.011f);
        var nightHorizon = new Vector3(0.008f, 0.012f, 0.028f);
        var duskZenith = new Vector3(0.12f, 0.12f, 0.3f);
        var duskHorizon = new Vector3(0.9f, 0.42f, 0.2f);
        Zenith = Vector3.Lerp(Vector3.Lerp(nightZenith, dayZenith, Daylight), duskZenith, Twilight * 0.45f);
        Horizon = Vector3.Lerp(Vector3.Lerp(nightHorizon, dayHorizon, Daylight), duskHorizon, Twilight * 0.2f);

        AmbientSky = Vector3.Lerp(new Vector3(0.014f, 0.02f, 0.045f), new Vector3(0.26f, 0.33f, 0.48f), Daylight)
                     + new Vector3(0.12f, 0.06f, 0.04f) * Twilight;
        AmbientHorizon = Vector3.Lerp(new Vector3(0.011f, 0.015f, 0.032f), new Vector3(0.2f, 0.23f, 0.28f), Daylight)
                         + new Vector3(0.14f, 0.07f, 0.03f) * Twilight;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }
}

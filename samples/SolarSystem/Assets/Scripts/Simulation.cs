using System.Globalization;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace SolarSystem;

/// <summary>
/// The simulation clock: advances the date at a chosen speed and moves every <see cref="CelestialBody"/> to where
/// it is on that date. Running all the bodies from one place, in <see cref="OnUpdate"/>, means the camera and the
/// HUD (which run later in the frame) always see this frame's positions.
/// </summary>
public sealed class Simulation : Component
{
    /// <summary>The speeds the HUD's « and » buttons step through, in simulated days per second.</summary>
    public static readonly float[] Speeds = { 0.25f, 0.5f, 1, 2, 5, 10, 20, 50, 100, 365 };

    private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Gets or sets how many days pass per real second.</summary>
    [InspectorRange(0.0f, 365.0f, 0.25f)]
    public float DaysPerSecond { get; set; } = 5.0f;

    public bool Paused { get; set; }

    /// <summary>Gets or sets how many simulated days one visual turn of a 24-hour planet takes.</summary>
    [InspectorRange(1.0f, 1000.0f, 1.0f)]
    public float SpinCompression { get; set; } = 80.0f;

    /// <summary>Gets or sets the starting date (<c>yyyy-MM-dd</c>); empty starts today.</summary>
    public string StartDate { get; set; } = "";

    /// <summary>Gets the running simulation, or <see langword="null"/> before one starts.</summary>
    public static Simulation? Current { get; private set; }

    /// <summary>Gets the simulated time, in days since the J2000 epoch.</summary>
    public double DaysSinceJ2000 { get; private set; }

    /// <summary>Gets the simulated date.</summary>
    public DateTime Date => J2000.AddDays(Math.Clamp(DaysSinceJ2000, -700000.0, 2900000.0));

    /// <summary>Gets every body, Sun first, then by distance from it.</summary>
    public IReadOnlyList<CelestialBody> Bodies { get; private set; } = Array.Empty<CelestialBody>();

    /// <summary>Gets the star at the center, if the scene has one.</summary>
    public CelestialBody? Sun { get; private set; }

    public override void OnStart()
    {
        Current = this;

        DateTime start = DateTime.UtcNow.Date.AddHours(12);
        if (!string.IsNullOrWhiteSpace(StartDate)
            && DateTime.TryParseExact(StartDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
        {
            start = parsed.AddHours(12);
        }

        DaysSinceJ2000 = (start - J2000).TotalDays;

        // Moons sort right after their planet, so the tour and Tab visit the Earth, then the Moon.
        List<CelestialBody> bodies = Scene.GetComponents<CelestialBody>();
        bodies.Sort((a, b) => SortKey(a).CompareTo(SortKey(b)));
        Bodies = bodies;
        Sun = bodies.FirstOrDefault(b => b.Kind == BodyKind.Star);
        Step();
    }

    public override void OnUpdate(float deltaTime)
    {
        if (!Paused)
        {
            DaysSinceJ2000 += deltaTime * (double)DaysPerSecond;
        }

        Step();
    }

    public override void OnDestroy()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>Steps to the next faster speed.</summary>
    public void Faster() => DaysPerSecond = Speeds.FirstOrDefault(s => s > DaysPerSecond + 0.001f, Speeds[^1]);

    /// <summary>Steps to the next slower speed.</summary>
    public void Slower() => DaysPerSecond = Speeds.LastOrDefault(s => s < DaysPerSecond - 0.001f, Speeds[0]);

    /// <summary>Describes the current speed, such as "5 days / s".</summary>
    public string DescribeSpeed()
    {
        float speed = DaysPerSecond;
        if (speed >= 365.0f)
        {
            return $"{speed / 365.0f:0.#} year / s";
        }

        string amount = speed < 1.0f ? speed.ToString("0.##", CultureInfo.InvariantCulture) : speed.ToString("0", CultureInfo.InvariantCulture);
        return speed == 1.0f ? "1 day / s" : $"{amount} days / s";
    }

    private void Step()
    {
        foreach (CelestialBody body in Bodies)
        {
            body.UpdateMotion(DaysSinceJ2000, SpinCompression);
        }
    }

    private static float SortKey(CelestialBody body) =>
        body.Primary is { } primary ? primary.Order + 0.5f : body.Order;
}

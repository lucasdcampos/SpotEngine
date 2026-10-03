using System.Globalization;

namespace SolarSystem;

/// <summary>One labelled value on the info card. <see cref="Exponent"/>, when set, is drawn as a superscript.</summary>
internal readonly record struct Fact(string Label, string Value, string Exponent = "", string Unit = "");

/// <summary>Turns a <see cref="CelestialBody"/>'s numbers into the facts its info card shows.</summary>
internal static class Facts
{
    private const float AstronomicalUnitKm = 149_597_870.7f;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Returns the card's facts for a body, in reading order (two per row).</summary>
    public static List<Fact> For(CelestialBody body)
    {
        var facts = new List<Fact>(8)
        {
            Mass(body.MassKg),
            new("Diameter", Thousands(body.DiameterKm), Unit: " km"),
            new(body.Kind == BodyKind.Star ? "Surface gravity" : "Gravity", body.GravityMs2.ToString("0.##", Invariant), Unit: " m/s²"),
            new(body.RotationHours < 0.0f ? "Day (retrograde)" : "Day length", Duration(MathF.Abs(body.RotationHours))),
        };

        switch (body.Kind)
        {
            case BodyKind.Star:
                facts.Add(new Fact("Age", "4.6", Unit: " billion years"));
                facts.Add(new Fact("Planets", "8"));
                facts.Add(new Fact("Surface temp", Thousands(body.MeanTemperatureC), Unit: " °C"));
                facts.Add(new Fact("Light to Earth", "8 min 20 s"));
                break;
            case BodyKind.Moon:
                facts.Add(new Fact("Orbit", Year(body.OrbitPeriodDays)));
                facts.Add(new Fact("From planet", Thousands(body.DistanceKm), Unit: " km"));
                facts.Add(new Fact("Mean temp", Thousands(body.MeanTemperatureC), Unit: " °C"));
                facts.Add(new Fact("Moonwalkers", "12"));
                break;
            default:
                facts.Add(new Fact("Year", Year(body.OrbitPeriodDays)));
                facts.Add(new Fact("From the Sun", (body.DistanceKm / AstronomicalUnitKm).ToString("0.00", Invariant), Unit: " AU"));
                facts.Add(new Fact("Mean temp", Thousands(body.MeanTemperatureC), Unit: " °C"));
                facts.Add(new Fact("Moons", body.Moons.ToString(Invariant)));
                break;
        }

        return facts;
    }

    /// <summary>Describes a body's diameter against the Earth's, such as "11.2 × Earth".</summary>
    public static string SizeComparedToEarth(CelestialBody body)
    {
        const float earthKm = 12_742.0f;
        float ratio = body.DiameterKm / earthKm;
        if (ratio >= 0.995f && ratio <= 1.005f) return "Earth's size";
        return ratio >= 10.0f ? $"{ratio:0} × Earth" : $"{ratio.ToString("0.##", Invariant)} × Earth";
    }

    /// <summary>Formats a position from the Sun, such as "3rd planet from the Sun".</summary>
    public static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => n.ToString(Invariant) + "th",
    };

    private static Fact Mass(float kg)
    {
        if (kg <= 0.0f) return new Fact("Mass", "n/a");
        int exponent = (int)MathF.Floor(MathF.Log10(kg));
        float mantissa = kg / MathF.Pow(10.0f, exponent);
        if (mantissa >= 9.995f)
        {
            mantissa /= 10.0f;
            exponent++;
        }

        return new Fact("Mass", mantissa.ToString("0.00", Invariant) + " × 10", exponent.ToString(Invariant), " kg");
    }

    private static string Duration(float hours) => hours switch
    {
        < 48.0f => $"{hours.ToString("0.0", Invariant)} h",
        _ => $"{(hours / 24.0f).ToString("0.#", Invariant)} days",
    };

    private static string Year(float days) => days switch
    {
        < 1000.0f => $"{days.ToString("0.#", Invariant)} days",
        _ => $"{(days / 365.256f).ToString("0.##", Invariant)} years",
    };

    private static string Thousands(float value) => value.ToString("#,0", Invariant);
}

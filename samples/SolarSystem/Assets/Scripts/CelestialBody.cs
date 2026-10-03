using System.Numerics;
using Spot.Engine.Scenes;

namespace SolarSystem;

/// <summary>What kind of body a <see cref="CelestialBody"/> is.</summary>
public enum BodyKind
{
    Star,
    Planet,
    Moon,
}

/// <summary>Which procedural surface the planet shader paints on a body.</summary>
public enum SurfaceStyle
{
    Star,
    Rocky,
    Venus,
    Earth,
    Mars,
    GasGiant,
    IceGiant,
}

/// <summary>
/// One body of the solar system: how it orbits and spins, how it looks, and the facts its info card shows. It is
/// data plus the orbit math — <see cref="Simulation"/> moves it, <see cref="SpaceRenderer"/> draws it and
/// <see cref="SolarHud"/> describes it — so every value can be tuned in the inspector.
/// </summary>
/// <remarks>
/// A body orbits its parent entity when the parent is a body too (the Moon is a child of the Earth), and the
/// origin otherwise. Sizes and distances are compressed to fit one screen; periods, tilts and facts are real.
/// </remarks>
public sealed class CelestialBody : EntityBehaviour
{
    private const float Deg2Rad = MathF.PI / 180.0f;

    [InspectorHeader("Identity")]
    public string DisplayName { get; set; } = "Body";

    public BodyKind Kind { get; set; } = BodyKind.Planet;

    /// <summary>Gets or sets the line under the name on the info card, such as "Gas giant".</summary>
    public string Category { get; set; } = "Planet";

    public string Description { get; set; } = "";

    /// <summary>Gets or sets the position from the Sun (0 for the Sun itself); orders the tour and the 1-8 keys.</summary>
    public int Order { get; set; }

    /// <summary>Gets or sets the color of the body's orbit, label and card accent.</summary>
    [InspectorColor]
    public Vector3 AccentColor { get; set; } = new(0.6f, 0.7f, 0.9f);

    [InspectorHeader("Motion")]
    [InspectorRange(0.01f, 20.0f, 0.01f)]
    public float Radius { get; set; } = 0.5f;

    /// <summary>Gets or sets the orbit radius, in scene units, around the parent body or the origin.</summary>
    [InspectorRange(0.0f, 200.0f, 0.1f)]
    public float OrbitRadius { get; set; }

    [InspectorRange(0.0f, 100000.0f, 1.0f)]
    public float OrbitPeriodDays { get; set; } = 365.256f;

    /// <summary>Gets or sets the mean longitude at the J2000 epoch, so the bodies start where they really are.</summary>
    public float MeanLongitudeDeg { get; set; }

    public float AxialTiltDeg { get; set; }

    /// <summary>Gets or sets the sidereal rotation period; negative spins backwards (Venus, Uranus).</summary>
    public float RotationHours { get; set; } = 24.0f;

    /// <summary>Gets or sets whether the body always shows its parent the same face, like the Moon.</summary>
    public bool TidallyLocked { get; set; }

    [InspectorHeader("Appearance")]
    public SurfaceStyle Surface { get; set; } = SurfaceStyle.Rocky;

    [InspectorColor]
    public Vector3 ColorA { get; set; } = new(0.6f, 0.6f, 0.6f);

    [InspectorColor]
    public Vector3 ColorB { get; set; } = new(0.4f, 0.4f, 0.4f);

    [InspectorColor]
    public Vector3 ColorC { get; set; } = new(0.8f, 0.8f, 0.8f);

    /// <summary>Gets or sets the cloud bands of a giant: how many, and how much they swirl.</summary>
    public float BandFrequency { get; set; } = 8.0f;

    public float Turbulence { get; set; } = 0.4f;

    /// <summary>Gets or sets a storm: its latitude, longitude and size in degrees, and its strength (0 = none).</summary>
    public Vector4 Storm { get; set; }

    [InspectorColor]
    public Vector3 StormColor { get; set; } = new(0.75f, 0.38f, 0.25f);

    /// <summary>Gets or sets the atmosphere's color, with its strength in <c>W</c> (0 = airless).</summary>
    [InspectorColor]
    public Vector4 Atmosphere { get; set; }

    /// <summary>Gets or sets how far the atmosphere glows past the surface, as a fraction of the radius.</summary>
    [InspectorRange(0.0f, 1.0f, 0.005f)]
    public float AtmosphereHeight { get; set; } = 0.05f;

    /// <summary>Gets or sets the rings' inner and outer edge, in body radii (0 = no rings).</summary>
    public float RingInner { get; set; }

    public float RingOuter { get; set; }

    /// <summary>Gets or sets whether the rings are a few narrow ringlets (Uranus) rather than broad sheets (Saturn).</summary>
    public bool NarrowRings { get; set; }

    [InspectorColor]
    public Vector4 RingColor { get; set; } = new(0.85f, 0.76f, 0.6f, 1.0f);

    public float Seed { get; set; }

    [InspectorHeader("Facts")]
    public float MassKg { get; set; }

    public float DiameterKm { get; set; }

    public float GravityMs2 { get; set; }

    /// <summary>Gets or sets the mean distance from what the body orbits, in kilometers.</summary>
    public float DistanceKm { get; set; }

    public float MeanTemperatureC { get; set; }

    public int Moons { get; set; }

    private CelestialBody? _primary;
    private bool _primaryResolved;

    /// <summary>Gets the body this one orbits — its parent entity's body — or <see langword="null"/> for the origin.</summary>
    public CelestialBody? Primary
    {
        get
        {
            // Resolved on first use rather than in OnCreate, which other scripts may run before.
            if (!_primaryResolved)
            {
                _primaryResolved = true;
                _primary = Entity.Parent is { } parent ? SceneScripts.Find<CelestialBody>(parent) : null;
            }

            return _primary;
        }
    }

    /// <summary>Gets the body's current world position.</summary>
    public Vector3 Position => GetComponent<TransformComponent>().WorldPosition;

    /// <summary>Gets the body's spin and axial tilt, as a rotation matrix.</summary>
    public Matrix4x4 Orientation { get; private set; } = Matrix4x4.Identity;

    /// <summary>Gets the angle the body has travelled along its orbit, in radians.</summary>
    public float OrbitAngle { get; private set; }

    /// <summary>Gets the world transform the renderer draws the body's unit sphere with.</summary>
    public Matrix4x4 World => Matrix4x4.CreateScale(Radius) * Orientation * Matrix4x4.CreateTranslation(Position);

    /// <summary>Gets the radius of everything drawn for the body, rings included.</summary>
    public float VisualRadius => Radius * MathF.Max(1.0f, RingOuter);

    /// <summary>Gets the normal of the body's equator — and of its rings — in world space.</summary>
    public Vector3 PoleAxis => Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, Orientation));

    public override void OnCreate()
    {
        // The editor shows a placeholder sphere for each body; in play the space renderer draws the real thing.
        if (Entity.TryGetComponent(out MeshComponent? placeholder))
        {
            placeholder.Enabled = false;
        }
    }

    /// <summary>
    /// Places the body where it is <paramref name="days"/> after J2000: around its orbit (circular, at the real
    /// period) and around its own axis (the real period, compressed so slow spinners still visibly turn).
    /// </summary>
    /// <param name="days">Days since the J2000 epoch.</param>
    /// <param name="spinCompression">How many simulated days one visual rotation of a 24-hour body takes.</param>
    public void UpdateMotion(double days, float spinCompression)
    {
        if (OrbitRadius > 0.0f && OrbitPeriodDays > 0.0f)
        {
            double turns = days / OrbitPeriodDays;
            OrbitAngle = (float)((MeanLongitudeDeg * Deg2Rad + (turns - Math.Floor(turns)) * Math.Tau) % Math.Tau);
            GetComponent<TransformComponent>().Position =
                new Vector3(MathF.Cos(OrbitAngle), 0.0f, -MathF.Sin(OrbitAngle)) * OrbitRadius;
        }

        float spin;
        if (TidallyLocked)
        {
            // Facing the primary: the same face turns toward it as the orbit goes round.
            spin = OrbitAngle + MathF.PI;
        }
        else
        {
            double spins = RotationHours != 0.0f ? days * 24.0 / RotationHours / MathF.Max(spinCompression, 1.0f) : 0.0;
            spin = (float)((spins - Math.Floor(spins)) * Math.Tau);
        }

        Orientation = Matrix4x4.CreateRotationY(spin) * Matrix4x4.CreateRotationX(-AxialTiltDeg * Deg2Rad);
    }
}

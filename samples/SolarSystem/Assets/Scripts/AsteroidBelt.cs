using System.Numerics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace SolarSystem;

/// <summary>
/// Scatters rocks between Mars and Jupiter. Each rock is an ordinary entity with a Mesh Renderer — the same
/// low-poly sphere, squashed and tinted differently — so the engine's lit renderer draws them, lit by the Sun's
/// point light, and its GPU instancing turns a thousand-odd rocks into a handful of draw calls. They are children
/// of this entity, which turns with the belt's orbital period.
/// </summary>
public sealed class AsteroidBelt : Component
{
    public int Count { get; set; } = 1400;

    [InspectorRange(0.0f, 200.0f, 0.1f)]
    public float InnerRadius { get; set; } = 21.5f;

    [InspectorRange(0.0f, 200.0f, 0.1f)]
    public float OuterRadius { get; set; } = 26.5f;

    /// <summary>Gets or sets how far rocks stray above and below the orbital plane.</summary>
    [InspectorRange(0.0f, 10.0f, 0.05f)]
    public float Thickness { get; set; } = 0.55f;

    [InspectorRange(0.001f, 2.0f, 0.005f)]
    public float MinSize { get; set; } = 0.035f;

    [InspectorRange(0.001f, 2.0f, 0.005f)]
    public float MaxSize { get; set; } = 0.16f;

    /// <summary>Gets or sets the time one turn of the belt takes (about 4.6 years at 2.7 AU).</summary>
    public float OrbitPeriodDays { get; set; } = 1680.0f;

    public int Seed { get; set; } = 7;

    private const string RockMesh = "builtin:Mesh/Sphere?radius=0.5&segments=7&rings=5";

    private static readonly Vector3[] Tints =
    {
        new(0.17f, 0.15f, 0.13f),
        new(0.14f, 0.13f, 0.13f),
        new(0.2f, 0.17f, 0.14f),
        new(0.12f, 0.11f, 0.1f),
        new(0.23f, 0.21f, 0.18f),
    };

    private TransformComponent _transform = null!;

    public override void OnStart()
    {
        _transform = GetComponent<TransformComponent>();
        var random = new Random(Seed);

        for (int i = 0; i < Count; i++)
        {
            // Two uniform draws averaged crowd the rocks toward the middle of the belt.
            float t = (float)(random.NextDouble() + random.NextDouble()) * 0.5f;
            float radius = InnerRadius + (OuterRadius - InnerRadius) * t;
            float angle = (float)random.NextDouble() * MathF.Tau;
            float height = Gaussian(random) * Thickness * 0.4f;

            // Most rocks are small; a few are large.
            float size = MinSize + (MaxSize - MinSize) * MathF.Pow((float)random.NextDouble(), 3.0f);
            var squash = new Vector3(1.0f, 0.55f + 0.4f * (float)random.NextDouble(), 0.7f + 0.3f * (float)random.NextDouble());
            Vector3 tint = Tints[random.Next(Tints.Length)] * (0.8f + 0.4f * (float)random.NextDouble());

            Entity rock = Instantiate("Asteroid");
            rock.SetParent(Entity);
            TransformComponent transform = rock.GetComponent<TransformComponent>();
            transform.Position = new Vector3(MathF.Cos(angle) * radius, height, -MathF.Sin(angle) * radius);
            transform.Rotation = new Vector3(Range(random, 0, 360), Range(random, 0, 360), Range(random, 0, 360));
            transform.Scale = squash * size;
            rock.AddComponent(new MeshComponent { ModelPath = RockMesh, Color = new Vector4(tint, 1.0f) });
        }
    }

    public override void OnUpdate(float deltaTime)
    {
        if (Simulation.Current is { } simulation && OrbitPeriodDays > 0.0f)
        {
            double turns = simulation.DaysSinceJ2000 / OrbitPeriodDays;
            float degrees = (float)((turns - Math.Floor(turns)) * 360.0);
            _transform.Rotation = _transform.Rotation with { Y = degrees };
        }
    }

    private static float Range(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    // A standard normal sample (Box-Muller).
    private static float Gaussian(Random random)
    {
        double u = 1.0 - random.NextDouble();
        double v = random.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(Math.Tau * v));
    }
}

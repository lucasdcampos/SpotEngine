using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine.Scenes;
using Spot.Engine.UI;
using Spot.Framework;

namespace HelloEngine;

/// <summary>
/// Drops physics cubes into the scene — on a timer, from the HUD's button, or with Space — and keeps the HUD's
/// counter current. Shows spawning entities with components from a script, and wiring a UI document
/// (<c>Assets/UI/Hud.sptui</c>, on this entity's UI Canvas) by widget name.
/// </summary>
public sealed class CubeRain : EntityBehaviour
{
    /// <summary>Gets or sets how many cubes may exist at once; the oldest is recycled past it.</summary>
    public int MaxCubes { get; set; } = 80;

    /// <summary>Gets or sets the height cubes are dropped from.</summary>
    public float DropHeight { get; set; } = 9.0f;

    /// <summary>Gets or sets the seconds between automatic drops.</summary>
    public float DropInterval { get; set; } = 0.75f;

    private static readonly Vector4[] Palette =
    {
        new(0.95f, 0.33f, 0.30f, 1.0f),
        new(0.98f, 0.78f, 0.25f, 1.0f),
        new(0.30f, 0.78f, 0.55f, 1.0f),
        new(0.30f, 0.58f, 0.95f, 1.0f),
        new(0.70f, 0.45f, 0.95f, 1.0f),
    };

    private readonly Queue<Entity> _cubes = new();
    private readonly Random _random = new();
    private Text? _counter;

    // The UI Canvas instantiates the document before scripts run, so its widgets already exist here.
    public override void OnCreate()
    {
        Button? drop = UI.Find<Button>("DropButton");
        if (drop is not null)
        {
            drop.OnClick += () => Drop(10);
        }

        _counter = UI.Find<Text>("Counter");
        InvokeRepeating(() => Drop(1), 1.0f, DropInterval);
        UpdateCounter();
    }

    public override void OnUpdate(float deltaTime)
    {
        if (Input.GetKeyDown(Key.Space))
        {
            Drop(10);
        }
    }

    private void Drop(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (_cubes.Count >= MaxCubes)
            {
                Destroy(_cubes.Dequeue());
            }

            float size = Range(0.35f, 0.8f);
            Entity cube = Instantiate("Cube");
            TransformComponent transform = cube.GetComponent<TransformComponent>();
            transform.Position = new Vector3(Range(-3.0f, 3.0f), DropHeight + Range(0.0f, 2.0f), Range(-3.0f, 3.0f));
            transform.Rotation = new Vector3(Range(0, 360), Range(0, 360), Range(0, 360));
            transform.Scale = new Vector3(size);

            cube.AddComponent(new MeshComponent { ModelPath = "primitive:Cube", Color = Palette[_random.Next(Palette.Length)] });
            cube.AddComponent(new PhysicsBody3DComponent { Mass = 4.0f * size * size * size, Friction = 0.6f, Restitution = 0.2f });
            cube.AddComponent(new BoxCollider3DComponent());
            _cubes.Enqueue(cube);
        }

        UpdateCounter();
    }

    private void UpdateCounter()
    {
        if (_counter is not null)
        {
            _counter.Content = _cubes.Count == 1 ? "1 cube" : $"{_cubes.Count} cubes";
        }
    }

    private float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);
}

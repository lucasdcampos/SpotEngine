using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Framework;

namespace Voxelcraft;

/// <summary>
/// Owns the <see cref="World"/> for the scene: creates it from the seed, streams chunks around the player every
/// frame, and frees everything when the scene stops. Other scripts reach it through <see cref="Current"/>.
/// </summary>
public sealed class VoxelWorld : Component
{
    /// <summary>Gets or sets the world seed; the same seed always grows the same world.</summary>
    public int Seed { get; set; } = 1337;

    /// <summary>Gets or sets whether every run starts a new world from a random seed.</summary>
    public bool RandomSeed { get; set; }

    /// <summary>Gets or sets how many chunks are drawn in every direction.</summary>
    [InspectorRange(4.0f, 32.0f, 1.0f)]
    public int RenderDistance { get; set; } = 16;

    /// <summary>Gets or sets how long each frame may spend uploading new chunks, in milliseconds.</summary>
    [InspectorRange(0.5f, 16.0f, 0.5f)]
    public float StreamingBudget { get; set; } = 3.0f;

    public static VoxelWorld? Current { get; private set; }

    public World? World { get; private set; }

    /// <summary>Gets where the player starts.</summary>
    public Vector3 Spawn { get; private set; }

    public override void OnStart()
    {
        Current = this;
        BlockAtlas.Build();
        int seed = RandomSeed ? Random.Shared.Next() : Seed;
        Seed = seed;
        World = new World(seed, Math.Clamp(RenderDistance, 2, 48));
        Spawn = World.FindSpawn();
    }

    public override void OnDestroy()
    {
        World?.Dispose();
        World = null;
        if (Current == this) Current = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        if (World is null) return;

        World.RenderDistance = Math.Clamp(RenderDistance, 2, 48);
        Vector3 focus = PlayerController.Current?.Position ?? Spawn;

        // Load the area around the spawn as fast as possible while the loading screen is up.
        bool loading = Game.Current is { Loaded: false };
        try
        {
            World.Update(focus, loading ? 24.0f : StreamingBudget);
        }
        catch (Exception ex)
        {
            Log.Error("Voxelcraft: streaming the world failed: {0}", ex.Message);
        }
    }
}

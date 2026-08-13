namespace Spot.Game;

/// <summary>
/// Shared, scene-wide state for the Horde Survival demo. It is deliberately a plain static holder so
/// the player, enemies, projectiles and the game manager can read and write a single source of truth
/// without looking each other's scripts up by reflection. There is only ever one horde game running,
/// so a static is the simplest thing that works. <see cref="HordeGameManager"/> calls
/// <see cref="Reset"/> in its <c>OnCreate</c>, which means reloading the scene starts a clean run.
/// </summary>
public static class HordeGameState
{
    /// <summary>Enemies destroyed this run (points vary by enemy type).</summary>
    public static int Score { get; set; }

    /// <summary>The current wave number (starts at 0, becomes 1 when the first wave spawns).</summary>
    public static int Wave { get; set; }

    /// <summary>The player's remaining health.</summary>
    public static float PlayerHealth { get; set; }

    /// <summary>The player's maximum health, used for the HUD bar.</summary>
    public static float PlayerMaxHealth { get; set; }

    /// <summary>Whether the player has died and the run is over.</summary>
    public static bool GameOver { get; set; }

    /// <summary>Half-width of the play field the player is confined to (world units, from center).</summary>
    public static float HalfWidth { get; set; } = 13.0f;

    /// <summary>Half-height of the play field the player is confined to (world units, from center).</summary>
    public static float HalfHeight { get; set; } = 7.0f;

    /// <summary>Resets every field to the start-of-run defaults.</summary>
    /// <param name="maxHealth">The player's starting and maximum health.</param>
    /// <param name="halfWidth">Half-width of the play field.</param>
    /// <param name="halfHeight">Half-height of the play field.</param>
    public static void Reset(float maxHealth, float halfWidth, float halfHeight)
    {
        Score = 0;
        Wave = 0;
        PlayerMaxHealth = maxHealth;
        PlayerHealth = maxHealth;
        GameOver = false;
        HalfWidth = halfWidth;
        HalfHeight = halfHeight;
    }
}

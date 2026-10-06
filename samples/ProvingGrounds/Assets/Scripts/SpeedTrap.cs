using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>
/// A gate at the end of the strafe-jump strip: a trigger volume that clocks the player passing through it. Anything
/// above running speed came from air-strafing, and scores.
/// </summary>
public sealed class SpeedTrap : Component
{
    /// <summary>Gets or sets the speed that counts as fast, in meters per second.</summary>
    public float Threshold { get; set; } = 9.0f;

    /// <summary>Gets the best speed clocked this session.</summary>
    public float Best { get; private set; }

    public override void OnTriggerEnter(Entity other)
    {
        if (Player.Current is not { } player || other != player.Entity || Playground.Current is not { } game) return;

        float speed = player.Speed;
        bool record = speed > Best;
        Best = MathF.Max(Best, speed);
        string text = $"Speed trap  {speed:0.0} m/s";
        if (speed >= Threshold)
        {
            game.Award((int)((speed - Threshold) * 40.0f) + 25, record ? text + "  (best)" : text, null, HudColors.Warm);
            Sfx.Play(Sfx.Zone, 0.5f, 0.0f, 1.3f);
        }
        else
        {
            game.PushFeed(text + " - strafe-jump for more", "", HudColors.Muted);
        }
    }
}

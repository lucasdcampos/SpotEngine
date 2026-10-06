using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>A trigger volume that names the area the player walks into, on the HUD.</summary>
public sealed class Zone : Component
{
    public string Title { get; set; } = "Zone";

    public string Subtitle { get; set; } = "";

    public override void OnTriggerEnter(Entity other)
    {
        if (Player.Current is { } player && other == player.Entity)
        {
            Playground.Current?.EnterZone(Title, Subtitle);
        }
    }
}

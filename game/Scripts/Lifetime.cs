using Spot.Scenes;

namespace Spot.Game.Scripts;

/// <summary>
/// Per-entity script that destroys its entity after a fixed lifetime.
/// </summary>
internal sealed class Lifetime : EntityBehaviour
{
    private readonly float _seconds;
    private float _age;

    public Lifetime(float seconds)
    {
        _seconds = seconds;
    }

    public override void OnUpdate(float deltaTime)
    {
        _age += deltaTime;
        if (_age >= _seconds)
        {
            Destroy();
        }
    }
}

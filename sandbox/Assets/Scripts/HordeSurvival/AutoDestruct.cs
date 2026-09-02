using Spot.Scenes;

namespace Spot.Game;

/// <summary>
/// Destroys its entity after <see cref="Lifetime"/> seconds. Attached to short-lived VFX emitters
/// (see <see cref="Vfx"/>) so a burst cleans itself up once its particles have faded.
/// </summary>
public sealed class AutoDestruct : EntityBehaviour
{
    /// <summary>Seconds to live before the entity is destroyed.</summary>
    public float Lifetime { get; set; } = 1.0f;

    private float _age;

    public override void OnUpdate(float deltaTime)
    {
        _age += deltaTime;
        if (_age >= Lifetime)
        {
            Destroy();
        }
    }
}

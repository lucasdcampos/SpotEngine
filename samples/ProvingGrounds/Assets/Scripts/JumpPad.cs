using System.Numerics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>
/// A launch pad: a trigger volume that throws the player when they step on it (a trigger callback). The launch is
/// given in the pad's own space, so turning the pad turns the jump.
/// </summary>
public sealed class JumpPad : Component
{
    /// <summary>Gets or sets the launch velocity in the pad's space: Y up, -Z forward.</summary>
    public Vector3 Launch { get; set; } = new(0.0f, 15.0f, -6.0f);

    private float _cooldown;

    public override void OnUpdate(float deltaTime) => _cooldown -= deltaTime;

    public override void OnTriggerEnter(Entity other)
    {
        if (_cooldown > 0.0f || Player.Current is not { } player || other != player.Entity) return;

        _cooldown = 0.4f;
        Transform transform = GetComponent<Transform>();
        Matrix4x4 yaw = Matrix4x4.CreateRotationY(transform.WorldRotation.Y * MathF.PI / 180.0f);
        player.Launch(Vector3.TransformNormal(Launch, yaw));

        Vector3 at = transform.WorldPosition + new Vector3(0.0f, 0.1f, 0.0f);
        Effects.Current?.Pulse(at, Vector3.UnitY, 2.4f, new Vector4(0.8f, 2.6f, 3.2f, 1.0f));
        Effects.Current?.Sparks(at, Vector3.UnitY, 18, 9.0f, new Vector4(1.0f, 3.0f, 4.0f, 1.0f), 0.5f, 6.0f);
        Sfx.PlayAt(Sfx.JumpPad, at, 0.8f, 6.0f, 0.05f);
    }
}

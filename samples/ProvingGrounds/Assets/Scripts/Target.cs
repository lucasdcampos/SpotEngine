using System.Numerics;
using Spot.Engine.Assets;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>
/// A pop-up bullseye on the range. A hit scores by ring — measured on the face from where the bullet landed — and by
/// distance, then the face flips back on its hinge (a tween) and pops up again a moment later. With
/// <see cref="SlideDistance"/> set it also slides side to side. Its parts are children in the scene: a "Pivot" (the
/// hinge, which tweens) holding the "Face" (the collider) and its rings, and a "Status" lamp on the base.
/// </summary>
public sealed class Target : Component, IShootable, IResettable
{
    private static readonly (float Radius, int Points, string Name)[] Rings =
    {
        (0.09f, 100, "Bullseye"),
        (0.21f, 50, "Inner ring"),
        (0.33f, 25, "Outer ring"),
        (0.46f, 10, "Edge"),
    };

    private static Material? s_ready;
    private static Material? s_down;

    /// <summary>Gets or sets how far the target slides each way along its own X axis (0 keeps it still).</summary>
    public float SlideDistance { get; set; }

    /// <summary>Gets or sets the slide speed, in meters per second.</summary>
    public float SlideSpeed { get; set; } = 2.0f;

    /// <summary>Gets or sets the seconds a knocked-down target stays down.</summary>
    public float ResetDelay { get; set; } = 2.2f;

    private TransformComponent _transform = null!;
    private TransformComponent? _pivot;
    private TransformComponent? _face;
    private MeshComponent? _status;
    private Vector3 _origin;
    private float _slideTime;
    private bool _down;
    private Coroutine? _tween;

    public Vector3 AimPoint => _face?.WorldPosition ?? _transform.WorldPosition;

    public override void OnStart()
    {
        _transform = GetComponent<TransformComponent>();
        _origin = _transform.Position;
        _slideTime = Random.Shared.NextSingle() * 10.0f;
        s_ready ??= new Material { Color = new Vector4(0.1f, 0.3f, 0.15f, 1.0f), EmissiveColor = new Vector3(0.2f, 1.0f, 0.45f), EmissiveIntensity = 3.0f };
        s_down ??= new Material { Color = new Vector4(0.3f, 0.08f, 0.05f, 1.0f), EmissiveColor = new Vector3(1.0f, 0.25f, 0.1f), EmissiveIntensity = 3.0f };

        foreach (Entity child in Entity.Children)
        {
            if (child.Name == "Pivot") _pivot = child.GetComponent<TransformComponent>();
            if (child.Name == "Status" && child.TryGetComponent(out MeshComponent? lamp)) _status = lamp;
        }

        if (_pivot?.Entity is { } pivot)
        {
            foreach (Entity child in pivot.Children)
            {
                if (child.Name == "Face") _face = child.GetComponent<TransformComponent>();
            }
        }

        SetStatus(ready: true);
    }

    public override void OnUpdate(float deltaTime)
    {
        if (SlideDistance <= 0.0f) return;

        _slideTime += deltaTime;
        float angle = _slideTime * SlideSpeed / SlideDistance;
        Matrix4x4 facing = Matrix4x4.CreateRotationY(_transform.Rotation.Y * MathF.PI / 180.0f);
        _transform.Position = _origin + Vector3.TransformNormal(Vector3.UnitX, facing) * MathF.Sin(angle) * SlideDistance;
    }

    public HitResult OnShot(in ShotInfo shot)
    {
        if (_down || _face is null || Playground.Current is not { } game) return HitResult.None;

        float distance = game.Player.IsValid ? Vector3.Distance(game.Player.GetComponent<TransformComponent>().Position, AimPoint) : 0.0f;
        float multiplier = 1.0f + MathF.Floor(distance / 10.0f) * 0.5f;

        if (shot.Kind == DamageKind.Explosion)
        {
            KnockDown();
            game.Stats.TargetsDown++;
            game.Award((int)(10 * multiplier), "Target blasted", AimPoint + new Vector3(0.0f, 0.6f, 0.0f), HudColors.Warm);
            return HitResult.Kill;
        }

        // Where on the face the bullet landed: its distance from the center, in the face's plane.
        Matrix4x4 world = _face.Matrix;
        Vector3 normal = Vector3.Normalize(new Vector3(world.M21, world.M22, world.M23));
        Vector3 offset = shot.Point - _face.WorldPosition;
        float radial = (offset - Vector3.Dot(offset, normal) * normal).Length();

        foreach ((float radius, int points, string name) in Rings)
        {
            if (radial > radius) continue;

            KnockDown();
            int score = (int)(points * multiplier);
            bool bullseye = points == 100;
            game.Stats.TargetsDown++;
            if (bullseye)
            {
                game.Stats.Bullseyes++;
                Sfx.PlayAt(Sfx.Bullseye, AimPoint, 0.8f, 30.0f);
            }

            game.Award(score, $"{name}  {distance:0} m", shot.Point - shot.Direction * 0.3f, bullseye ? HudColors.Gold : HudColors.Accent);
            return HitResult.Kill;
        }

        return HitResult.None; // the corner of the square collider, outside the disc
    }

    public void ResetState()
    {
        if (_tween is not null) StopCoroutine(_tween);
        CancelInvoke();
        _down = false;
        if (_pivot is not null) _pivot.Rotation = Vector3.Zero;
        SetStatus(ready: true);
    }

    private void KnockDown()
    {
        _down = true;
        SetStatus(ready: false);
        Sfx.PlayAt(Sfx.TargetDown, AimPoint, 0.7f, 8.0f, 0.05f);
        Swing(-88.0f, 0.22f, Ease.OutBounce);
        Invoke(Raise, ResetDelay);
    }

    private void Raise()
    {
        Swing(0.0f, 0.4f, Ease.OutBack, () =>
        {
            _down = false;
            SetStatus(ready: true);
        });
    }

    private void Swing(float to, float duration, Ease ease, Action? done = null)
    {
        if (_pivot is null) return;
        if (_tween is not null) StopCoroutine(_tween);

        TransformComponent pivot = _pivot;
        _tween = Tween(pivot.Rotation.X, to, duration, angle => pivot.Rotation = new Vector3(angle, 0.0f, 0.0f), ease, done);
    }

    private void SetStatus(bool ready)
    {
        if (_status is not null) _status.Material = ready ? s_ready : s_down;
    }
}

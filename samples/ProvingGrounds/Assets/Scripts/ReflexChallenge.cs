using System.Numerics;
using Spot.Engine.Assets;
using Spot.Engine.Physics;
using Spot.Engine;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>Where the reflex challenge is in its run.</summary>
public enum ChallengeState
{
    Idle,
    Countdown,
    Running,
    Results,
}

/// <summary>
/// The reflex challenge: shoot its "Start Button" child, survive a countdown, then shoot glowing orbs as they pop up
/// on the <see cref="Wall"/> (an entity reference, set in the inspector) for <see cref="Duration"/> seconds. Faster
/// hits score more, consecutive hits build a combo, and a miss or an orb left too long breaks it. The orbs are
/// entities spawned with a mesh and a sphere collider as children of this one, so a hit on any of them reaches this
/// script.
/// </summary>
public sealed class ReflexChallenge : Component, IShootable, IResettable
{
    /// <summary>Gets or sets the board the orbs appear on (its scale is the area they use).</summary>
    public Entity Wall;

    /// <summary>Gets or sets the length of a run, in seconds.</summary>
    public float Duration { get; set; } = 30.0f;

    /// <summary>Gets or sets how many orbs are up at once.</summary>
    public int Simultaneous { get; set; } = 3;

    /// <summary>Gets or sets the orbs' radius.</summary>
    public float OrbRadius { get; set; } = 0.3f;

    /// <summary>Gets or sets how long an orb waits before it counts as missed.</summary>
    public float OrbLifetime { get; set; } = 2.4f;

    /// <summary>Gets the challenge of the playing scene.</summary>
    public static ReflexChallenge? Current { get; private set; }

    public ChallengeState State { get; private set; }

    /// <summary>Gets the seconds since the state last changed.</summary>
    public float StateTime { get; private set; }

    public float TimeLeft { get; private set; }

    public int Score { get; private set; }

    public int Hits { get; private set; }

    public int Combo { get; private set; }

    public int BestCombo { get; private set; }

    public int Shots { get; private set; }

    public float AverageReaction => Hits == 0 ? 0.0f : _reactionTotal / Hits;

    public float Accuracy => Shots == 0 ? 0.0f : Math.Min(1.0f, (float)Hits / Shots);

    public bool NewBest { get; private set; }

    /// <summary>Gets the combo multiplier the next hit scores with.</summary>
    public float Multiplier => 1.0f + MathF.Min(Combo, 20) * 0.05f;

    private readonly List<Orb> _orbs = new();
    private readonly Material _orbMaterial = new()
    {
        Color = new Vector4(0.1f, 0.3f, 0.4f, 1.0f), EmissiveColor = new Vector3(0.3f, 0.9f, 1.0f), EmissiveIntensity = 4.0f,
    };

    private TransformComponent? _button;
    private TransformComponent? _ring;
    private Vector3 _ringScale;
    private TextComponent? _label;
    private int _shotsAtStart;
    private int _hitsSeen;
    private int _shotsSeen;
    private float _reactionTotal;
    private int _countdownStep;

    public Vector3 AimPoint => _button?.WorldPosition ?? GetComponent<TransformComponent>().WorldPosition;

    public override void OnStart()
    {
        Current = this;
        foreach (Entity child in Entity.Children)
        {
            if (child.Name != "Start Button") continue;
            _button = child.GetComponent<TransformComponent>();
            foreach (Entity part in child.Children)
            {
                if (part.TryGetComponent(out TextComponent? text)) _label = text;
                if (part.Name == "Ring")
                {
                    _ring = part.GetComponent<TransformComponent>();
                    _ringScale = _ring.Scale;
                }
            }
        }

        SetState(ChallengeState.Idle);
    }

    public override void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        if (deltaTime <= 0.0f) return;

        StateTime += deltaTime;
        switch (State)
        {
            case ChallengeState.Idle:
            case ChallengeState.Results:
                PulseButton();
                if (State == ChallengeState.Results && StateTime > 9.0f) SetState(ChallengeState.Idle);
                break;
            case ChallengeState.Countdown:
                UpdateCountdown();
                break;
            case ChallengeState.Running:
                UpdateRun(deltaTime);
                break;
        }
    }

    public HitResult OnShot(in ShotInfo shot)
    {
        if (shot.Kind == DamageKind.Explosion) return HitResult.None;

        if (State == ChallengeState.Running)
        {
            Orb? orb = Nearest(shot.Point);
            return orb is null ? HitResult.None : Pop(orb);
        }

        if (State is ChallengeState.Idle or ChallengeState.Results && _button is not null
            && Vector3.Distance(shot.Point, _button.WorldPosition) < 1.2f)
        {
            Begin();
            return HitResult.Kill;
        }

        return HitResult.None;
    }

    public void ResetState()
    {
        ClearOrbs();
        SetState(ChallengeState.Idle);
    }

    private void Begin()
    {
        ClearOrbs();
        Score = 0;
        Hits = 0;
        Combo = 0;
        BestCombo = 0;
        Shots = 0;
        NewBest = false;
        _reactionTotal = 0.0f;
        _countdownStep = 0;
        TimeLeft = Duration;
        SetState(ChallengeState.Countdown);
        Playground.Current?.EnterZone("Reflex Test", "Shoot the orbs as fast as they appear");
    }

    private void UpdateCountdown()
    {
        // Three beeps a second apart, then go.
        if (_countdownStep < 3 && StateTime >= _countdownStep)
        {
            Sfx.Play(Sfx.Beep, 0.6f);
            _countdownStep++;
        }

        if (StateTime >= 3.0f)
        {
            Sfx.Play(Sfx.Go, 0.6f);
            _shotsAtStart = Playground.Current?.Stats.ShotsFired ?? 0;
            _shotsSeen = 0;
            _hitsSeen = 0;
            SetState(ChallengeState.Running);
        }
    }

    private void UpdateRun(float deltaTime)
    {
        TimeLeft -= deltaTime;

        // A shot fired since last frame that hit no orb breaks the combo.
        Shots = (Playground.Current?.Stats.ShotsFired ?? _shotsAtStart) - _shotsAtStart;
        if (Shots - _shotsSeen > Hits - _hitsSeen && Combo > 0)
        {
            Combo = 0;
        }

        _shotsSeen = Shots;
        _hitsSeen = Hits;

        for (int i = _orbs.Count - 1; i >= 0; i--)
        {
            Orb orb = _orbs[i];
            orb.Age += deltaTime;
            float remaining = OrbLifetime - orb.Age;
            if (remaining <= 0.0f)
            {
                Remove(orb);
                Combo = 0;
                Playground.Current?.Popup("Too slow", orb.Position + new Vector3(0.0f, 0.2f, 0.8f), HudColors.Danger, 0.8f);
                continue;
            }

            // Shrink through the last moments as a warning.
            if (remaining < 0.6f)
            {
                orb.Transform.Scale = new Vector3(OrbRadius * 2.0f * (0.45f + 0.55f * remaining / 0.6f));
            }
        }

        if (TimeLeft <= 0.0f)
        {
            Finish();
            return;
        }

        while (_orbs.Count < Simultaneous)
        {
            Spawn();
        }
    }

    private HitResult Pop(Orb orb)
    {
        Playground? game = Playground.Current;
        float reaction = orb.Age;
        Hits++;
        Combo++;
        BestCombo = Math.Max(BestCombo, Combo);
        _reactionTotal += reaction;
        int points = (int)((100 + MathF.Max(0.0f, 1.2f - reaction) * 100.0f) * Multiplier);
        Score += points;

        Effects.Current?.Sparks(orb.Position, Vector3.UnitZ, 16, 7.0f, new Vector4(1.2f, 3.6f, 4.5f, 1.0f), 1.2f, 4.0f);
        Effects.Current?.Pulse(orb.Position, Vector3.UnitZ, OrbRadius * 3.0f, new Vector4(0.8f, 2.4f, 3.0f, 1.0f));
        Sfx.Play(Sfx.HitTick, 0.5f, 0.0f, 1.0f + MathF.Min(Combo, 16) * 0.04f);
        game?.Popup($"+{points}", orb.Position + new Vector3(0.0f, 0.3f, 0.3f), Combo >= 10 ? HudColors.Gold : HudColors.Accent, 0.9f);
        Remove(orb);
        return HitResult.Kill;
    }

    private void Finish()
    {
        ClearOrbs();
        Playground? game = Playground.Current;
        if (game is not null)
        {
            NewBest = Score > game.Stats.BestChallenge;
            game.Stats.BestChallenge = Math.Max(game.Stats.BestChallenge, Score);
            game.Award(Score / 10, NewBest ? "Reflex test - new best!" : "Reflex test complete", null, HudColors.Gold);
        }

        Sfx.Play(Sfx.Fanfare, 0.6f);
        SetState(ChallengeState.Results);
    }

    // A new orb somewhere on the wall, clear of the others, growing in with an overshoot.
    private void Spawn()
    {
        if (!Wall.IsValid) return;

        TransformComponent wall = Wall.GetComponent<TransformComponent>();
        Vector3 size = wall.Scale;
        Vector3 center = wall.WorldPosition;
        Vector3 position = center;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            position = center + new Vector3(
                (Random.Shared.NextSingle() - 0.5f) * (size.X - 1.6f),
                (Random.Shared.NextSingle() - 0.5f) * (size.Y - 1.4f),
                size.Z * 0.5f + OrbRadius + 0.15f);
            if (_orbs.TrueForAll(o => Vector3.Distance(o.Position, position) > 1.4f)) break;
        }

        Entity entity = Scene.Instantiate("Orb");
        entity.SetParent(Entity);
        TransformComponent transform = entity.GetComponent<TransformComponent>();
        Matrix4x4.Invert(GetComponent<TransformComponent>().Matrix, out Matrix4x4 toLocal);
        transform.Position = Vector3.Transform(position, toLocal);
        transform.Scale = new Vector3(0.01f);
        entity.AddComponent(new MeshComponent { ModelPath = "builtin:Mesh/Sphere", Material = _orbMaterial });
        entity.AddComponent(new SphereCollider3DComponent { Radius = 0.5f });

        var orb = new Orb { Entity = entity, Transform = transform, Position = position };
        _orbs.Add(orb);
        Tween(0.01f, OrbRadius * 2.0f, 0.16f, s =>
        {
            if (entity.IsValid && orb.Age < OrbLifetime - 0.6f) transform.Scale = new Vector3(s);
        }, Ease.OutBack);
    }

    private Orb? Nearest(Vector3 point)
    {
        Orb? best = null;
        float bestDistance = OrbRadius + 0.2f;
        foreach (Orb orb in _orbs)
        {
            float distance = Vector3.Distance(orb.Position, point);
            if (distance < bestDistance)
            {
                best = orb;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void Remove(Orb orb)
    {
        _orbs.Remove(orb);
        if (orb.Entity.IsValid) Destroy(orb.Entity);
    }

    private void ClearOrbs()
    {
        foreach (Orb orb in _orbs)
        {
            if (orb.Entity.IsValid) Destroy(orb.Entity);
        }

        _orbs.Clear();
    }

    private void SetState(ChallengeState state)
    {
        State = state;
        StateTime = 0.0f;
        if (_label is not null)
        {
            _label.Text = state switch
            {
                ChallengeState.Idle => "SHOOT TO START",
                ChallengeState.Results => "SHOOT TO RETRY",
                _ => "GOOD LUCK",
            };
        }
    }

    // The ring around the button breathes while it waits to be shot (the button itself, a collider, stays put).
    private void PulseButton()
    {
        if (_ring is null) return;
        float pulse = 1.0f + 0.08f * MathF.Sin(StateTime * 5.0f);
        _ring.Scale = new Vector3(_ringScale.X * pulse, _ringScale.Y, _ringScale.Z * pulse);
    }

    private sealed class Orb
    {
        public Entity Entity;
        public TransformComponent Transform = null!;
        public Vector3 Position;
        public float Age;
    }
}

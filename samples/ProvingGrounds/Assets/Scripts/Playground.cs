using System.Numerics;
using Spot.Engine.Physics;
using Spot.Engine.Scenes;
using Spot.Framework;
using Spot.Framework.Audio;

namespace ProvingGrounds;

/// <summary>A line in the HUD's event feed: what happened and what it scored.</summary>
public sealed class FeedEntry
{
    public string Text = "";
    public string Points = "";
    public Vector4 Color = Vector4.One;
    public float Age;
}

/// <summary>What the player has done this session, for the stats board.</summary>
public sealed class SessionStats
{
    public int Score;
    public int ShotsFired;
    public int ShotsHit;
    public int Bullseyes;
    public int TargetsDown;
    public int DronesDown;
    public int CratesDestroyed;
    public int RocketJumps;
    public int BestChallenge;
    public float TopSpeed;
    public float Distance;
    public float PlayTime;

    public float Accuracy => ShotsFired == 0 ? 0.0f : (float)ShotsHit / ShotsFired;
}

/// <summary>Something that can be put back as the level started — props, targets, crates, drones.</summary>
public interface IResettable
{
    void ResetState();
}

/// <summary>
/// The game's hub, on the "Game" entity: binds the input actions, sets the collision-layer matrix, owns the session
/// (score, stats, the event feed, the hit marker), the start screen, pause, slow motion and the player's settings,
/// and resets the playground. Other scripts reach it through <see cref="Current"/>.
/// </summary>
public sealed class Playground : Component
{
    private const float FeedLifetime = 4.0f;
    private const int MaxFeed = 6;

    /// <summary>Gets or sets where the player respawns after falling off the world.</summary>
    public Entity SpawnPoint;

    /// <summary>Gets or sets the camera's field of view, in degrees, when not aiming down sights.</summary>
    public float FieldOfView { get; set; } = 80.0f;

    /// <summary>Gets or sets the mouse sensitivity multiplier.</summary>
    public float MouseSensitivity { get; set; } = 1.0f;

    /// <summary>Gets or sets whether the camera bobs while walking.</summary>
    public bool HeadBob { get; set; } = true;

    /// <summary>Gets or sets whether the frame rate and frame time are shown.</summary>
    public bool ShowPerformance { get; set; }

    /// <summary>Gets or sets how loud the wind is, on the Music bus.</summary>
    public float AmbienceVolume { get; set; } = 0.5f;

    /// <summary>Gets or sets the time scale of slow motion.</summary>
    public float SlowMotionScale { get; set; } = 0.3f;

    /// <summary>Gets the game's hub while its scene is playing.</summary>
    public static Playground? Current { get; private set; }

    /// <summary>Gets whether the player has left the start screen.</summary>
    public bool Started { get; private set; }

    /// <summary>Gets whether the pause menu is open (the world is frozen).</summary>
    public bool Paused { get; private set; }

    /// <summary>Gets whether slow motion is on.</summary>
    public bool SlowMotion { get; private set; }

    /// <summary>Gets or sets whether the controls sheet is open.</summary>
    public bool HelpOpen { get; set; }

    /// <summary>Gets whether the stats board is held open.</summary>
    public bool StatsOpen { get; private set; }

    /// <summary>Gets whether the game owns the mouse (no menu or screen needs the pointer).</summary>
    public bool InControl => Started && !Paused;

    public SessionStats Stats { get; } = new();

    public IReadOnlyList<FeedEntry> Feed => _feed;

    /// <summary>Gets how long ago the last hit landed, and what it did.</summary>
    public float SinceHit { get; private set; } = 10.0f;

    public HitResult LastHit { get; private set; }

    /// <summary>Gets the zone the player last walked into and how long ago.</summary>
    public string ZoneTitle { get; private set; } = "";

    public string ZoneSubtitle { get; private set; } = "";

    public float SinceZone { get; private set; } = 100.0f;

    /// <summary>Gets the camera shake, 0..1, decaying over time (the player squares it).</summary>
    public float Trauma { get; private set; }

    /// <summary>Gets or sets the multiplier the weapon applies to mouse look while aiming.</summary>
    public float LookScale { get; set; } = 1.0f;

    public Entity Player { get; private set; }

    public CharacterController3DComponent? Controller { get; private set; }

    private readonly List<FeedEntry> _feed = new();
    private WorldPopups? _popups;
    private Voice _wind;
    private int _settleFrames;

    public override void OnStart()
    {
        Current = this;
        Sfx.Build();
        BindDefaults();

        PhysicsSettings.ResetLayerCollisions();
        PhysicsSettings.SetLayerCollision(Layers.Player, Layers.Projectile, false);
        PhysicsSettings.SetLayerCollision(Layers.Projectile, Layers.Projectile, false);
        PhysicsSettings.SetLayerCollision(Layers.Player, Layers.Debris, false);

        foreach (Entity entity in Scene.View<CharacterController3DComponent>())
        {
            Player = entity;
            Controller = entity.GetComponent<CharacterController3DComponent>();
            break;
        }

        _popups = new WorldPopups(Scene);
        Time.TimeScale = 1.0f;
        Input.CursorLocked = false;
        _wind = AudioManager.Play(Sfx.Wind, AmbienceVolume, 1.0f, loop: true, bus: AudioMixer.MusicBus);
    }

    public override void OnDestroy()
    {
        // The editor keeps the process (and these globals) alive between plays: leave them as we found them.
        Time.TimeScale = 1.0f;
        Input.CursorLocked = false;
        PhysicsSettings.ResetLayerCollisions();
        AudioManager.Stop(_wind);
        _popups?.Dispose();
        if (Current == this)
        {
            Current = null;
        }
    }

    public override void OnUpdate(float deltaTime)
    {
        float dt = Time.UnscaledDeltaTime;

        if (!Started)
        {
            if (Input.GetMouseButtonDown(MouseButton.Left) || Input.GetKeyDown(Key.Enter) || Input.GetKeyDown(Key.Space))
            {
                Begin();
            }
        }
        else
        {
            HandleKeys();
        }

        StatsOpen = Started && !Paused && Input.GetAction("stats");

        if (InControl)
        {
            Stats.PlayTime += deltaTime;
        }

        SinceHit += dt;
        SinceZone += dt;
        Trauma = MathF.Max(0.0f, Trauma - deltaTime * 1.6f);

        for (int i = _feed.Count - 1; i >= 0; i--)
        {
            _feed[i].Age += dt;
            if (_feed[i].Age > FeedLifetime)
            {
                _feed.RemoveAt(i);
            }
        }

        _popups?.Update(deltaTime);
        ApplySettings();
        TrackPlayer(deltaTime);
    }

    // Runs after the character controller and before physics: hold the player still while a menu has the mouse,
    // and re-seat mouse look for a couple of frames after it gets the mouse back so the view never jumps.
    public override void OnFixedUpdate(float deltaTime)
    {
        if (Controller is null) return;

        if (_settleFrames > 0)
        {
            Controller.FirstMouse = true;
            _settleFrames--;
        }

        if (!Started && Player.TryGetComponent(out PhysicsBody3DComponent? body))
        {
            body.Velocity = new Vector3(0.0f, MathF.Min(body.Velocity.Y, 0.0f), 0.0f);
        }
    }

    /// <summary>Adds to the score and the feed, with an optional popup in the world.</summary>
    public void Award(int points, string text, Vector3? at = null, Vector4? color = null)
    {
        Stats.Score += points;
        Vector4 tint = color ?? HudColors.Accent;
        PushFeed(text, points > 0 ? $"+{points}" : "", tint);
        if (at is { } position)
        {
            _popups?.Spawn(points > 0 ? $"+{points}" : text, position, tint);
        }
    }

    /// <summary>Adds a line to the feed without scoring.</summary>
    public void PushFeed(string text, string points, Vector4 color)
    {
        _feed.Insert(0, new FeedEntry { Text = text, Points = points, Color = color });
        if (_feed.Count > MaxFeed)
        {
            _feed.RemoveAt(_feed.Count - 1);
        }
    }

    /// <summary>Shows a word floating in the world, like a score popup.</summary>
    public void Popup(string text, Vector3 at, Vector4 color, float size = 1.0f) => _popups?.Spawn(text, at, color, size);

    /// <summary>Flashes the hit marker and plays its tick.</summary>
    public void RegisterHit(HitResult result)
    {
        if (result == HitResult.None) return;

        // A kill keeps its marker even if a plain hit lands in the same instant.
        if (result == HitResult.Kill || SinceHit > 0.08f || LastHit != HitResult.Kill)
        {
            LastHit = result;
        }

        SinceHit = 0.0f;
        Sfx.Play(result == HitResult.Kill ? Sfx.KillTick : Sfx.HitTick, result == HitResult.Kill ? 0.55f : 0.4f, 0.03f);
    }

    /// <summary>Shows a zone's banner (once per visit).</summary>
    public void EnterZone(string title, string subtitle)
    {
        // The start screen already names the place; banners begin once the player is in.
        if (!Started || (title == ZoneTitle && SinceZone < 8.0f)) return;

        ZoneTitle = title;
        ZoneSubtitle = subtitle;
        SinceZone = 0.0f;
        Sfx.Play(Sfx.Zone, 0.5f);
    }

    /// <summary>Shakes the camera; trauma adds up to 1.</summary>
    public void AddTrauma(float amount) => Trauma = Math.Clamp(Trauma + amount, 0.0f, 1.0f);

    /// <summary>Opens or closes the pause menu: the world freezes and the mouse is freed.</summary>
    public void SetPaused(bool paused)
    {
        if (!Started || paused == Paused) return;

        Paused = paused;
        Time.TimeScale = paused ? 0.0f : SlowMotion ? SlowMotionScale : 1.0f;
        Input.CursorLocked = !paused;
        if (!paused)
        {
            _settleFrames = 2;
        }
    }

    /// <summary>Puts every prop, target, crate and drone back where it started.</summary>
    public void ResetPlayground()
    {
        foreach (IResettable resettable in Scene.GetComponents<IResettable>())
        {
            resettable.ResetState();
        }

        Effects.Current?.ClearDecals();
        PushFeed("Playground reset", "", HudColors.Muted);
        Sfx.Play(Sfx.Switch, 0.6f);
    }

    /// <summary>Puts the player back on the spawn point.</summary>
    public void Respawn()
    {
        if (!Player.IsValid) return;

        TransformComponent spawn = SpawnPoint.IsValid ? SpawnPoint.GetComponent<TransformComponent>() : GetComponent<TransformComponent>();
        Player.GetComponent<TransformComponent>().Position = spawn.WorldPosition;
        if (Player.TryGetComponent(out PhysicsBody3DComponent? body))
        {
            body.Velocity = Vector3.Zero;
        }

        if (Controller is not null)
        {
            Controller.Yaw = spawn.WorldRotation.Y;
            Controller.Pitch = 0.0f;
        }
    }

    private void Begin()
    {
        Started = true;
        Input.CursorLocked = true;
        _settleFrames = 2;
        Sfx.Play(Sfx.UiClick, 0.6f);
    }

    private void HandleKeys()
    {
        if (Input.GetActionDown("pause"))
        {
            SetPaused(!Paused);
            Sfx.Play(Sfx.UiClick, 0.5f);
        }

        if (Input.GetActionDown("help"))
        {
            HelpOpen = !HelpOpen;
            Sfx.Play(Sfx.UiClick, 0.4f);
        }

        if (Paused) return;

        if (Input.GetActionDown("slowmo"))
        {
            SlowMotion = !SlowMotion;
            Time.TimeScale = SlowMotion ? SlowMotionScale : 1.0f;
            PushFeed(SlowMotion ? "Slow motion on" : "Slow motion off", "", HudColors.Muted);
            Sfx.Play(Sfx.Switch, 0.5f, pitch: SlowMotion ? 0.7f : 1.2f);
        }

        if (Input.GetActionDown("reset"))
        {
            ResetPlayground();
        }
    }

    private void ApplySettings()
    {
        if (Controller is not null)
        {
            Controller.MouseSensitivity = 0.1f * MouseSensitivity * LookScale;
            Controller.EnableBobbing = HeadBob;
        }

        AudioManager.SetVoiceGain(_wind, AmbienceVolume);
    }

    private Vector3 _lastPosition;
    private bool _hasLastPosition;

    private void TrackPlayer(float deltaTime)
    {
        if (!Player.IsValid) return;

        Vector3 position = Player.GetComponent<TransformComponent>().Position;
        if (position.Y < -25.0f)
        {
            Respawn();
            PushFeed("Back to the hub", "", HudColors.Muted);
            _hasLastPosition = false;
            return;
        }

        if (_hasLastPosition && deltaTime > 0.0f)
        {
            Vector3 step = position - _lastPosition;
            float horizontal = new Vector2(step.X, step.Z).Length();
            if (horizontal < 5.0f)
            {
                Stats.Distance += horizontal;
            }
        }

        _lastPosition = position;
        _hasLastPosition = true;

        if (Player.TryGetComponent(out PhysicsBody3DComponent? body))
        {
            float speed = new Vector2(body.Velocity.X, body.Velocity.Z).Length();
            Stats.TopSpeed = MathF.Max(Stats.TopSpeed, speed);
        }
    }

    // The game owns its action names. They are bound here, in code, so they work the same in the editor's play mode
    // and in a build; a binding the player changed from the console ('bind') is left alone.
    private static void BindDefaults()
    {
        Bind("fire", InputBinding.Mouse(MouseButton.Left));
        Bind("aim", InputBinding.Mouse(MouseButton.Right));
        Bind("reload", InputBinding.Key(Key.R));
        Bind("weapon1", InputBinding.Key(Key.Alpha1));
        Bind("weapon2", InputBinding.Key(Key.Alpha2));
        Bind("swap", InputBinding.Key(Key.Q));
        Bind("pause", InputBinding.Key(Key.Escape), InputBinding.Key(Key.P));
        Bind("stats", InputBinding.Key(Key.Tab));
        Bind("help", InputBinding.Key(Key.F1));
        Bind("slowmo", InputBinding.Key(Key.T));
        Bind("reset", InputBinding.Key(Key.Backspace));
    }

    private static void Bind(string action, params InputBinding[] bindings)
    {
        if (Input.GetBindings(action).Count > 0) return;

        foreach (InputBinding binding in bindings)
        {
            Input.Bind(action, binding);
        }
    }
}

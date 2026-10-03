using System.Numerics;
using Spot.Engine.Assets;
using Spot.Engine.Physics;
using Spot.Engine.Scenes;
using Spot.Framework;

namespace ProvingGrounds;

/// <summary>
/// The player's weapons, on the camera: fires hitscan bullets (a filtered <see cref="Scene.Raycast(Vector3, Vector3, float, out RaycastHit, uint, bool)"/>
/// that sees past the player's own capsule) and physical grenades, with spread that blooms as you fire, recoil that
/// kicks the view, aiming down sights that narrows the field of view, reloads and weapon swaps. Hits push physics
/// props with an impulse at the point they land, and anything implementing <see cref="IShootable"/> reacts.
/// </summary>
public sealed class WeaponController : Component
{
    private const float SwapTime = 0.38f;

    /// <summary>Gets the weapons of the playing scene.</summary>
    public static WeaponController? Current { get; private set; }

    /// <summary>Gets the weapon in hand.</summary>
    public WeaponSpec Weapon { get; private set; } = Arsenal.Rifle;

    /// <summary>Gets the weapons in their slots.</summary>
    public IReadOnlyList<WeaponSpec> Slots { get; } = new[] { Arsenal.Rifle, Arsenal.Launcher };

    /// <summary>Gets the rounds left in the weapon in hand's magazine.</summary>
    public int Ammo => _ammo[Index(Weapon)];

    public int AmmoIn(WeaponSpec weapon) => _ammo[Index(weapon)];

    /// <summary>Gets the reload progress, 0..1, or a negative number when not reloading.</summary>
    public float ReloadProgress => _reloading ? _reloadTime / Weapon.ReloadTime : -1.0f;

    /// <summary>Gets how far into aiming down sights, 0..1 (eased).</summary>
    public float Aim => Ease(_aim);

    /// <summary>Gets the current spread cone half-angle, in degrees.</summary>
    public float Spread { get; private set; }

    /// <summary>Gets how long since the last shot, in seconds.</summary>
    public float SinceShot { get; private set; } = 10.0f;

    /// <summary>Gets whether a swap is in progress.</summary>
    public bool Swapping => _swapTime > 0.0f;

    private readonly int[] _ammo = { Arsenal.Rifle.Magazine, Arsenal.Launcher.Magazine };
    private readonly Material _grenadeMaterial = new()
    {
        Color = new Vector4(0.3f, 0.15f, 0.05f, 1.0f), EmissiveColor = new Vector3(1.0f, 0.5f, 0.15f), EmissiveIntensity = 5.0f,
    };

    private CameraComponent _camera = null!;
    private TransformComponent _transform = null!;
    private Viewmodel? _viewmodel;
    private WeaponSpec? _pending;
    private float _swapTime;
    private float _cooldown;
    private float _aim;
    private float _bloom;
    private bool _reloading;
    private float _reloadTime;
    private bool _magInPlayed;
    private float _recoilDebt;
    private float _land;
    private Vector2 _lastMouse;

    public override void OnStart()
    {
        Current = this;
        _camera = GetComponent<CameraComponent>();
        _transform = GetComponent<TransformComponent>();
        _viewmodel = new Viewmodel(Scene, Entity);
        _lastMouse = Input.MousePosition;
    }

    public override void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    public override void OnUpdate(float deltaTime)
    {
        Playground? game = Playground.Current;
        if (game is null || _viewmodel is null) return;

        Vector2 mouse = Input.MousePosition;
        Vector2 mouseDelta = Input.CursorLocked ? mouse - _lastMouse : Vector2.Zero;
        _lastMouse = mouse;

        if (deltaTime <= 0.0f) return; // paused

        bool control = game.InControl;
        SinceShot += deltaTime;
        _cooldown -= deltaTime;

        if (control)
        {
            HandleSwapInput();
            if (Input.GetActionDown("reload")) StartReload();
        }

        UpdateSwap(deltaTime);
        UpdateReload(deltaTime);

        bool aiming = control && Input.GetAction("aim") && !_reloading && !Swapping;
        _aim = Math.Clamp(_aim + (aiming ? 1.0f : -1.0f) * deltaTime / Weapon.AimTime, 0.0f, 1.0f);
        float aim = Ease(_aim);
        _camera.FieldOfView = game.FieldOfView * (1.0f + (Weapon.AimZoom - 1.0f) * aim);
        game.LookScale = 1.0f + (Weapon.AimZoom - 1.0f) * aim;

        _bloom = MathF.Max(0.0f, _bloom - Weapon.BloomRecovery * deltaTime);
        Spread = CurrentSpread(game, aim);

        if (control && !Swapping && WantsToFire())
        {
            TryFire(game);
        }

        RecoverRecoil(game, deltaTime);
        AnimateModel(game, deltaTime, mouseDelta, aim);
    }

    /// <summary>Jolts the weapon on a hard landing (the player calls it).</summary>
    public void Land(float strength) => _land = MathF.Max(_land, Math.Clamp(strength, 0.0f, 1.0f));

    private void HandleSwapInput()
    {
        WeaponSpec? wanted = null;
        if (Input.GetActionDown("weapon1")) wanted = Arsenal.Rifle;
        if (Input.GetActionDown("weapon2")) wanted = Arsenal.Launcher;
        if (Input.GetActionDown("swap") || MathF.Abs(Input.MouseScrollDelta.Y) > 0.01f)
        {
            wanted = (_pending ?? Weapon) == Arsenal.Rifle ? Arsenal.Launcher : Arsenal.Rifle;
        }

        if (wanted is not null && wanted != (_pending ?? Weapon))
        {
            _pending = wanted;
            _reloading = false;
            if (_swapTime <= 0.0f) _swapTime = SwapTime;
            Sfx.Play(Sfx.Switch, 0.5f, 0.05f);
        }
    }

    // A swap lowers the weapon in hand, switches models at the bottom, and raises the new one.
    private void UpdateSwap(float deltaTime)
    {
        if (_swapTime <= 0.0f) return;

        float before = _swapTime;
        _swapTime -= deltaTime;
        if (before > SwapTime * 0.5f && _swapTime <= SwapTime * 0.5f && _pending is not null)
        {
            Weapon = _pending;
            _pending = null;
            _viewmodel!.Show(Weapon);
            _bloom = 0.0f;
        }

        if (_swapTime <= 0.0f)
        {
            _swapTime = 0.0f;
            if (Ammo == 0) StartReload();
        }
    }

    private float Lowered => _swapTime <= 0.0f ? 0.0f : 1.0f - MathF.Abs(_swapTime / SwapTime * 2.0f - 1.0f);

    private void StartReload()
    {
        if (_reloading || Swapping || Ammo >= Weapon.Magazine) return;

        _reloading = true;
        _reloadTime = 0.0f;
        _magInPlayed = false;
        Sfx.Play(Sfx.MagOut, 0.7f, 0.04f);
    }

    private void UpdateReload(float deltaTime)
    {
        if (!_reloading) return;

        _reloadTime += deltaTime;
        if (!_magInPlayed && _reloadTime >= Weapon.ReloadTime * 0.6f)
        {
            _magInPlayed = true;
            Sfx.Play(Sfx.MagIn, 0.8f, 0.04f);
        }

        if (_reloadTime >= Weapon.ReloadTime)
        {
            _reloading = false;
            _ammo[Index(Weapon)] = Weapon.Magazine;
            Sfx.Play(Sfx.Chamber, 0.7f, 0.04f);
        }
    }

    private bool WantsToFire() => Weapon.Automatic ? Input.GetAction("fire") : Input.GetActionDown("fire");

    private void TryFire(Playground game)
    {
        if (_cooldown > 0.0f || _reloading) return;

        if (Ammo <= 0)
        {
            if (Input.GetActionDown("fire")) Sfx.Play(Sfx.DryFire, 0.6f);
            StartReload();
            return;
        }

        _cooldown = Weapon.FireInterval;
        _ammo[Index(Weapon)]--;
        SinceShot = 0.0f;
        game.Stats.ShotsFired++;

        Vector3 origin = _transform.WorldPosition;
        Vector3 forward = Forward();
        TransformComponent muzzle = _viewmodel!.Muzzle(Weapon);
        Vector3 muzzlePosition = muzzle.WorldPosition;

        Kick(game);
        _viewmodel.Kick(Weapon);
        _bloom = MathF.Min(Weapon.MaxBloom, _bloom + Weapon.BloomPerShot);
        Effects.Current?.MuzzleFlash(muzzlePosition, Weapon.MuzzleColor, Weapon.FlashSize);

        if (Weapon.Launcher)
        {
            Sfx.Play(Sfx.LauncherShot, 0.85f, 0.04f);
            Launch(game, origin, forward, muzzlePosition);
        }
        else
        {
            Sfx.Play(Sfx.RifleShot, 0.55f, 0.06f);
            Hitscan(game, origin, Scatter(forward, Spread), muzzlePosition);
        }

        if (Ammo == 0) Invoke(StartReload, 0.25f);
    }

    private void Hitscan(Playground game, Vector3 origin, Vector3 direction, Vector3 muzzle)
    {
        if (!Scene.Raycast(origin, direction, Weapon.Range, out RaycastHit hit, Layers.Shots))
        {
            Effects.Current?.Tracer(muzzle, origin + direction * Weapon.Range, Weapon.TracerColor);
            return;
        }

        Effects.Current?.Tracer(muzzle, hit.Point, Weapon.TracerColor);

        IShootable? shootable = hit.Entity.GetComponentInParent<IShootable>();
        HitResult result = shootable?.OnShot(new ShotInfo(hit.Point, hit.Normal, direction, Weapon.Damage, DamageKind.Bullet)) ?? HitResult.None;
        if (result != HitResult.None)
        {
            game.Stats.ShotsHit++;
            game.RegisterHit(result);
        }

        if (hit.Entity.IsValid && hit.Entity.TryGetComponent(out PhysicsBody3DComponent? body) && body.IsDynamic && !body.IsKinematic)
        {
            body.AddImpulseAtPosition(direction * Weapon.Impulse, hit.Point);
        }

        Surface surface = shootable is not null ? Surface.Metal : hit.Entity.TryGetComponent(out PhysicsProp? prop) ? prop.Surface : Surface.Concrete;
        Effects.Current?.Impact(hit, direction, surface, decal: shootable is null);
    }

    private void Launch(Playground game, Vector3 origin, Vector3 forward, Vector3 muzzle)
    {
        // Leave from the muzzle unless it is buried in a wall; then from just in front of the eye.
        Vector3 start = muzzle;
        Vector3 toMuzzle = muzzle - origin;
        if (Scene.Raycast(origin, toMuzzle, toMuzzle.Length() + 0.1f, out _, Layers.Shots))
        {
            start = origin + forward * 0.2f;
        }

        Vector3 inherited = Vector3.Zero;
        if (game.Player.IsValid && game.Player.TryGetComponent(out PhysicsBody3DComponent? playerBody))
        {
            inherited = playerBody.Velocity * 0.5f;
        }

        Vector3 direction = Scatter(forward, Spread);
        Entity grenade = Scene.Instantiate("Grenade");
        TransformComponent transform = grenade.GetComponent<TransformComponent>();
        transform.Position = start;
        transform.Scale = new Vector3(0.16f);
        grenade.AddComponent(new MeshComponent { ModelPath = "builtin:Mesh/Sphere", Material = _grenadeMaterial });
        grenade.AddComponent(new SphereCollider3DComponent { Radius = 0.5f, Layer = Layers.Projectile, Restitution = 0.3f });
        grenade.AddComponent(new PhysicsBody3DComponent
        {
            Mass = 0.5f, Velocity = direction * Weapon.ProjectileSpeed + inherited + Vector3.UnitY * 1.2f, Restitution = 0.3f,
        });
        grenade.AddComponent(new Grenade { Damage = Weapon.Damage });
    }

    // Recoil kicks the view up and sideways; part of it drifts back once you stop firing.
    private void Kick(Playground game)
    {
        if (game.Controller is not { } controller) return;

        float scale = 1.0f - 0.4f * Aim;
        float pitch = Weapon.KickPitch * scale;
        controller.Pitch = Math.Clamp(controller.Pitch + pitch, -controller.MaxPitch, controller.MaxPitch);
        controller.Yaw += (Random.Shared.NextSingle() * 2.0f - 1.0f) * Weapon.KickYaw * scale;
        _recoilDebt += pitch * Weapon.KickRecovery;
    }

    private void RecoverRecoil(Playground game, float deltaTime)
    {
        if (_recoilDebt <= 0.0f || SinceShot < Weapon.FireInterval * 1.2f || game.Controller is not { } controller) return;

        float step = MathF.Min(_recoilDebt, (_recoilDebt * 7.0f + 1.5f) * deltaTime);
        _recoilDebt -= step;
        controller.Pitch = Math.Clamp(controller.Pitch - step, -controller.MaxPitch, controller.MaxPitch);
    }

    private float CurrentSpread(Playground game, float aim)
    {
        float spread = Weapon.HipSpread + (Weapon.AimSpread - Weapon.HipSpread) * aim + _bloom * (1.0f - 0.6f * aim);
        if (game.Player.IsValid && game.Player.TryGetComponent(out PhysicsBody3DComponent? body))
        {
            float speed = new Vector2(body.Velocity.X, body.Velocity.Z).Length();
            spread += Weapon.MoveSpread * MathF.Min(speed / 8.0f, 1.0f) * (1.0f - 0.5f * aim);
        }

        if (game.Controller is { } controller)
        {
            if (!controller.IsGrounded && !controller.IsNoClip) spread += Weapon.MoveSpread * 0.8f;
            spread *= 1.0f - 0.3f * controller.CrouchAmount;
        }

        return spread;
    }

    private void AnimateModel(Playground game, float deltaTime, Vector2 mouseDelta, float aim)
    {
        float speed = 0.0f;
        bool grounded = true;
        float crouch = 0.0f;
        if (game.Player.IsValid && game.Player.TryGetComponent(out PhysicsBody3DComponent? body))
        {
            speed = new Vector2(body.Velocity.X, body.Velocity.Z).Length();
        }

        if (game.Controller is { } controller)
        {
            grounded = controller.IsGrounded || controller.IsNoClip;
            crouch = controller.CrouchAmount;
        }

        // Pull the weapon back when a wall is closer than its barrel.
        float retract = 0.0f;
        uint walls = Layers.Shots & ~(1u << Layers.Debris);
        if (Scene.Raycast(_transform.WorldPosition, Forward(), 0.95f, out RaycastHit wall, walls))
        {
            retract = Math.Clamp((0.95f - wall.Distance) / 0.6f, 0.0f, 1.0f);
        }

        _land = MathF.Max(0.0f, _land - deltaTime * 4.0f);
        _viewmodel!.Update(deltaTime, new ViewmodelPose
        {
            Weapon = Weapon, Aim = aim, MouseDelta = mouseDelta, Speed = speed, Grounded = grounded, Crouch = crouch,
            Reload = ReloadProgress, Lowered = Lowered, Retract = retract, Land = _land,
        });
    }

    private Vector3 Forward()
    {
        Matrix4x4 world = _transform.Matrix;
        return Vector3.Normalize(-new Vector3(world.M31, world.M32, world.M33));
    }

    // A random direction inside a cone around the forward vector, denser toward the middle.
    private Vector3 Scatter(Vector3 forward, float spreadDegrees)
    {
        if (spreadDegrees <= 0.0f) return forward;

        Matrix4x4 world = _transform.Matrix;
        Vector3 right = Vector3.Normalize(new Vector3(world.M11, world.M12, world.M13));
        Vector3 up = Vector3.Normalize(new Vector3(world.M21, world.M22, world.M23));
        float radius = MathF.Tan(spreadDegrees * MathF.PI / 180.0f) * MathF.Sqrt(Random.Shared.NextSingle());
        float angle = Random.Shared.NextSingle() * MathF.Tau;
        return Vector3.Normalize(forward + (right * MathF.Cos(angle) + up * MathF.Sin(angle)) * radius);
    }

    private int Index(WeaponSpec weapon) => weapon == Arsenal.Launcher ? 1 : 0;

    private static float Ease(float t) => t * t * (3.0f - 2.0f * t);
}

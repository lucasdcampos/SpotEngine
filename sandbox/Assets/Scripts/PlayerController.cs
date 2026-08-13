using System;
using System.Numerics;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Game;

/// <summary>
/// The player. Moves with WASD (confined to the arena and blocked by obstacles), rotates to aim at the
/// mouse, and fires bolts toward the cursor on the left button with a muzzle flash. Enemies drain its
/// health on contact; when hit it flashes red and shakes the screen. Health lives in
/// <see cref="HordeGameState"/> so the HUD and game-over logic can read it.
/// </summary>
public sealed class PlayerController : EntityBehaviour
{
    /// <summary>Movement speed in world units per second.</summary>
    public float MoveSpeed { get; set; } = 6.5f;

    /// <summary>Minimum seconds between shots while the fire button is held.</summary>
    public float FireCooldown { get; set; } = 0.14f;

    /// <summary>Speed of the bolts this player fires.</summary>
    public float ProjectileSpeed { get; set; } = 18.0f;

    private static readonly Vector4 HurtColor = new(1.0f, 0.25f, 0.2f, 1.0f);
    private static readonly Vector4 BoltColor = new(1.0f, 0.92f, 0.35f, 1.0f);
    private static readonly Vector4 MuzzleColor = new(1.0f, 0.85f, 0.4f, 1.0f);

    private float _fireTimer;
    private Vector2 _aim = Vector2.UnitX;
    private Vector4 _baseColor = Vector4.One;
    private float _hurtFlash;
    private float _hurtPoofTimer;
    private float _lastHealth;

    public override void OnCreate()
    {
        Sprite2DComponent sprite = GetComponent<Sprite2DComponent>();
        sprite.Texture = Shapes.Triangle;
        _baseColor = sprite.Color;
        _lastHealth = HordeGameState.PlayerHealth;
    }

    public override void OnUpdate(float deltaTime)
    {
        if (HordeGameState.GameOver)
        {
            return;
        }

        TransformComponent transform = GetComponent<TransformComponent>();
        float radius = MathF.Max(transform.Scale.X, transform.Scale.Y) * 0.5f;

        Move(transform, radius, deltaTime);
        Aim(transform);
        Shoot(transform);
        HurtFeedback(transform, deltaTime);
    }

    private void Move(TransformComponent transform, float radius, float deltaTime)
    {
        var dir = Vector2.Zero;
        if (Input.GetKey(Key.W)) dir.Y += 1.0f;
        if (Input.GetKey(Key.S)) dir.Y -= 1.0f;
        if (Input.GetKey(Key.A)) dir.X -= 1.0f;
        if (Input.GetKey(Key.D)) dir.X += 1.0f;

        var pos = new Vector2(transform.Position.X, transform.Position.Y);
        if (dir.LengthSquared() > 0.0f)
        {
            pos += Vector2.Normalize(dir) * MoveSpeed * deltaTime;
        }

        GameUtil.ResolveObstacles(Scene, ref pos, radius);

        float limitX = MathF.Max(0.0f, HordeGameState.HalfWidth - radius);
        float limitY = MathF.Max(0.0f, HordeGameState.HalfHeight - radius);
        pos.X = Math.Clamp(pos.X, -limitX, limitX);
        pos.Y = Math.Clamp(pos.Y, -limitY, limitY);

        transform.Position = new Vector3(pos, 0.0f);
    }

    private void Aim(TransformComponent transform)
    {
        if (TryGetMouseWorld(out Vector2 mouseWorld))
        {
            Vector2 toMouse = mouseWorld - new Vector2(transform.Position.X, transform.Position.Y);
            if (toMouse.LengthSquared() > 0.0001f)
            {
                _aim = Vector2.Normalize(toMouse);
            }
        }

        float angle = MathF.Atan2(_aim.Y, _aim.X) * (180.0f / MathF.PI);
        transform.Rotation = new Vector3(0.0f, 0.0f, angle);
    }

    private void Shoot(TransformComponent transform)
    {
        _fireTimer -= Time.DeltaTime;
        if (_fireTimer > 0.0f || !Input.GetMouseButton(MouseButton.Left))
        {
            return;
        }

        _fireTimer = FireCooldown;

        var origin = new Vector2(transform.Position.X, transform.Position.Y);
        Vector2 muzzle = origin + _aim * 0.6f;

        Entity bolt = Instantiate("Projectile");
        bolt.AddComponent(new Sprite2DComponent { Color = BoltColor });

        TransformComponent boltTransform = bolt.GetComponent<TransformComponent>();
        boltTransform.Position = new Vector3(muzzle, 0.0f);
        boltTransform.Scale = new Vector3(0.45f, 0.16f, 1.0f);
        boltTransform.Rotation = new Vector3(0.0f, 0.0f, MathF.Atan2(_aim.Y, _aim.X) * (180.0f / MathF.PI));

        Projectile projectile = bolt.AddScript<Projectile>();
        projectile.Direction = _aim;
        projectile.Speed = ProjectileSpeed;

        Vfx.Burst(Scene, muzzle, MuzzleColor, 5, 5.0f, 0.18f, 0.12f);
    }

    private void HurtFeedback(TransformComponent transform, float deltaTime)
    {
        float damage = _lastHealth - HordeGameState.PlayerHealth;
        _lastHealth = HordeGameState.PlayerHealth;

        if (damage > 0.0f)
        {
            _hurtFlash = 1.0f;
            CameraShake.Add(MathF.Min(0.35f, damage * 0.02f));

            _hurtPoofTimer -= deltaTime;
            if (_hurtPoofTimer <= 0.0f)
            {
                _hurtPoofTimer = 0.12f;
                var pos = new Vector2(transform.Position.X, transform.Position.Y);
                Vfx.Burst(Scene, pos, HurtColor, 6, 3.5f, 0.2f, 0.25f, additive: false);
            }
        }

        // Fade the red hurt tint back to the base color.
        Sprite2DComponent sprite = GetComponent<Sprite2DComponent>();
        if (_hurtFlash > 0.0f)
        {
            _hurtFlash = MathF.Max(0.0f, _hurtFlash - deltaTime * 4.0f);
            sprite.Color = Vector4.Lerp(_baseColor, HurtColor, _hurtFlash);
        }
        else
        {
            sprite.Color = _baseColor;
        }
    }

    // Maps the mouse cursor (window pixels) into world space for the primary orthographic camera. The
    // ortho projection spans [-aspect*zoom, aspect*zoom] x [-zoom, zoom] around the camera position.
    private bool TryGetMouseWorld(out Vector2 world)
    {
        world = Vector2.Zero;

        Entity? cameraEntity = Find("Camera");
        if (cameraEntity is null || !cameraEntity.Value.TryGetComponent(out CameraComponent? camera))
        {
            return false;
        }

        Window window = Application.Instance.Window;
        if (window.Width == 0 || window.Height == 0)
        {
            return false;
        }

        Vector2 mouse = Input.MousePosition;
        float aspect = (float)window.Width / window.Height;
        float zoom = camera.ZoomLevel;

        float ndcX = (mouse.X / window.Width) * 2.0f - 1.0f;
        float ndcY = 1.0f - (mouse.Y / window.Height) * 2.0f;

        Vector3 camPos = cameraEntity.Value.GetComponent<TransformComponent>().WorldPosition;
        world = new Vector2(camPos.X + ndcX * aspect * zoom, camPos.Y + ndcY * zoom);
        return true;
    }
}

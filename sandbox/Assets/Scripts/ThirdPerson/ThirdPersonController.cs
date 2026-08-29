using System;
using System.Numerics;
using Spot.Audio;
using Spot.Core;
using Spot.Scenes;
using Spot.UI;
using AudioApi = Spot.Audio.Audio;

namespace Spot.Game;

/// <summary>
/// A GTA V-style third-person controller. WASD moves the character across the ground relative to where the
/// camera is facing, holding Shift breaks into a run, Space performs a running jump, and the mouse orbits an
/// over-the-shoulder camera around the character (scroll to zoom). Holding the <b>right mouse button</b>
/// enters an aiming stance: the camera pulls in tight over the shoulder, a crosshair appears, the character
/// turns to face the aim direction and strafes (rather than turning toward its movement). It drives an
/// <see cref="AnimatorComponent"/> through its <c>Speed</c> float (idle/walk/run), its <c>Jump</c> trigger,
/// and its <c>Aiming</c> bool so the animation follows the movement.
/// </summary>
/// <remarks>
/// Put this on the player entity (the one carrying the <see cref="AnimatorComponent"/>). It finds the camera
/// by name (<see cref="CameraName"/>) and takes ownership of its transform every frame, so the camera entity
/// needs no script of its own — just a <see cref="CameraComponent"/>. Movement is kinematic (transform
/// driven): the horizontal plane follows input while a simple gravity arc handles the jump, keeping the
/// character on its authored ground height, which is all a flat floor needs. The crosshair is built once with
/// the engine's runtime UI and shown only while aiming.
/// </remarks>
public sealed class ThirdPersonController : EntityBehaviour
{
    // ----- Movement -----

    /// <summary>Walk speed in world units per second.</summary>
    public float MoveSpeed { get; set; } = 3.2f;

    /// <summary>Run speed (Shift held) in world units per second.</summary>
    public float RunSpeed { get; set; } = 6.0f;

    /// <summary>Movement speed while aiming, in world units per second (a controlled strafe).</summary>
    public float AimMoveSpeed { get; set; } = 2.2f;

    /// <summary>How fast the character turns to face its movement direction (degrees per second).</summary>
    public float TurnSpeed { get; set; } = 720.0f;

    /// <summary>
    /// Extra yaw (degrees) added to the character's facing so the mesh's forward axis lines up with the
    /// direction of travel. Flip by 180 if the character walks backwards / moon-walks.
    /// </summary>
    public float ModelYawOffset { get; set; } = 180.0f;

    // ----- Jump -----

    /// <summary>Initial upward speed of a jump, in world units per second.</summary>
    public float JumpSpeed { get; set; } = 5.0f;

    /// <summary>Downward acceleration applied during a jump, in world units per second squared.</summary>
    public float Gravity { get; set; } = 14.0f;

    // ----- Camera -----

    /// <summary>Name of the camera entity this controller drives.</summary>
    public string CameraName { get; set; } = "Camera";

    /// <summary>Distance from the character to the camera, in world units (adjusted by the scroll wheel).</summary>
    public float CameraDistance { get; set; } = 4.5f;

    /// <summary>Closest the camera may zoom in.</summary>
    public float MinDistance { get; set; } = 2.0f;

    /// <summary>Farthest the camera may zoom out.</summary>
    public float MaxDistance { get; set; } = 9.0f;

    /// <summary>Height above the character's feet the camera looks at (roughly the shoulders).</summary>
    public float CameraHeight { get; set; } = 1.5f;

    /// <summary>Mouse look sensitivity (degrees of rotation per pixel of mouse movement).</summary>
    public float MouseSensitivity { get; set; } = 0.15f;

    /// <summary>How far the camera may pitch up (positive) and down (negative), in degrees.</summary>
    public float MinPitch { get; set; } = -35.0f;

    /// <summary>Upper pitch limit, in degrees.</summary>
    public float MaxPitch { get; set; } = 70.0f;

    // ----- Aiming -----

    /// <summary>Camera distance while aiming — pulled in tight over the shoulder.</summary>
    public float AimDistance { get; set; } = 1.8f;

    /// <summary>Camera look height while aiming (roughly shoulder/sight level).</summary>
    public float AimHeight { get; set; } = 1.55f;

    /// <summary>Sideways over-the-shoulder camera offset while aiming, in world units (positive = right).</summary>
    public float AimShoulderOffset { get; set; } = 0.55f;

    /// <summary>How quickly the camera blends in and out of the aim framing (1/seconds).</summary>
    public float AimBlendSpeed { get; set; } = 10.0f;

    /// <summary>Diameter of the on-screen crosshair, in UI pixels.</summary>
    public float CrosshairSize { get; set; } = 40.0f;

    // ----- Shooting -----

    /// <summary>Minimum seconds between shots while the fire button is held (the fire-rate cap).</summary>
    public float FireCooldown { get; set; } = 0.18f;

    /// <summary>Linear gain of the gunshot sound, where 1 is its authored level.</summary>
    public float FireVolume { get; set; } = 0.9f;

    /// <summary>The gunshot clip reference (a <c>guid:</c> reference or a source audio path).</summary>
    public string FireSoundRef { get; set; } = "guid:05a1c12ad1864dadbde66c6e73113089";

    // ----- Runtime state -----
    private float _yaw;
    private float _pitch = 15.0f;
    private float _facing;
    private float _groundY;
    private float _height;        // current height above the ground (a jump arc; 0 while grounded)
    private float _verticalVel;
    private bool _isJumping;
    private bool _aiming;
    private float _aimBlend;       // 0 = free camera, 1 = fully aimed (drives the shoulder framing)
    private Vector2 _lastMouse;
    private bool _firstFrame = true;
    private AnimatorComponent? _animator;
    private Panel? _crosshair;
    private AudioClip? _fireClip;
    private float _fireTimer;

    public override void OnCreate()
    {
        TransformComponent transform = GetComponent<TransformComponent>();
        _groundY = transform.Position.Y;

        // Orbit yaw starts from the character's authored facing, so the camera begins behind it.
        _yaw = transform.Rotation.Y;

        // Face the character away from the camera from the first frame (GTA framing), instead of letting
        // it turn 180 the moment the player presses forward.
        Vector3 startForward = Vector3.Transform(new Vector3(0.0f, 0.0f, -1.0f), Matrix4x4.CreateRotationY(_yaw * (MathF.PI / 180.0f)));
        _facing = FacingForDirection(startForward);
        transform.Rotation = new Vector3(transform.Rotation.X, _facing, transform.Rotation.Z);

        Entity.TryGetComponent(out _animator);
        BuildCrosshair();

        // Preload the gunshot so the first shot has no hitch; a bad reference logs and simply plays silent.
        try
        {
            _fireClip = AudioClip.Load(FireSoundRef);
        }
        catch (Exception ex)
        {
            Log.Warn("ThirdPersonController: could not load fire sound '{0}': {1}", FireSoundRef, ex.Message);
        }

        // Capture the mouse for continuous look, like a PC third-person game.
        Input.CursorLocked = true;
    }

    public override void OnUpdate(float deltaTime)
    {
        if (deltaTime <= 0.0f)
        {
            return;
        }

        UpdateCameraLook();

        TransformComponent transform = GetComponent<TransformComponent>();

        _aiming = Input.GetMouseButton(MouseButton.Right);
        _animator?.SetBool("Aiming", _aiming);
        if (_crosshair is not null)
        {
            _crosshair.Visible = _aiming;
        }

        // Read WASD as a local move vector (forward is -Z, matching the engine's camera convention).
        Vector3 input = Vector3.Zero;
        if (Input.GetKey(Key.W)) input.Z -= 1.0f;
        if (Input.GetKey(Key.S)) input.Z += 1.0f;
        if (Input.GetKey(Key.A)) input.X -= 1.0f;
        if (Input.GetKey(Key.D)) input.X += 1.0f;

        bool moving = input.LengthSquared() > 0.0001f;
        Vector3 position = transform.Position;
        float planarSpeed;

        if (_aiming)
        {
            // Aiming: no running, strafe at the aim speed, and always face where the camera is looking so
            // the aim direction turns the character.
            planarSpeed = moving ? AimMoveSpeed : 0.0f;
            if (moving)
            {
                Vector3 wishDir = Vector3.Transform(Vector3.Normalize(input), Matrix4x4.CreateRotationY(_yaw * (MathF.PI / 180.0f)));
                position += wishDir * AimMoveSpeed * deltaTime;
            }

            Vector3 camForwardFlat = Vector3.Transform(new Vector3(0.0f, 0.0f, -1.0f), Matrix4x4.CreateRotationY(_yaw * (MathF.PI / 180.0f)));
            _facing = FacingForDirection(camForwardFlat);
            transform.Rotation = new Vector3(transform.Rotation.X, _facing, transform.Rotation.Z);

            // Pick the directional aim-walk clip from the dominant input axis (relative to the aim facing):
            // 0 = idle, 1 = forward, 2 = back, 3 = left, 4 = right.
            _animator?.SetInt("AimMove", AimMoveDirection(input, moving));
        }
        else
        {
            bool running = moving && (Input.GetKey(Key.LeftShift) || Input.GetKey(Key.RightShift));
            planarSpeed = moving ? (running ? RunSpeed : MoveSpeed) : 0.0f;
            if (moving)
            {
                // Rotate the input by the camera's yaw so "forward" is always away from the camera.
                Vector3 wishDir = Vector3.Transform(Vector3.Normalize(input), Matrix4x4.CreateRotationY(_yaw * (MathF.PI / 180.0f)));
                position += wishDir * planarSpeed * deltaTime;

                // Turn the character to face where it is heading.
                float targetFacing = FacingForDirection(wishDir);
                _facing = MoveTowardsAngle(_facing, targetFacing, TurnSpeed * deltaTime);
                transform.Rotation = new Vector3(transform.Rotation.X, _facing, transform.Rotation.Z);
            }

            _animator?.SetInt("AimMove", 0);
        }

        // Jumping is disabled while aiming so a shot stance is never interrupted.
        UpdateJump(deltaTime, allowStart: !_aiming);
        position.Y = _groundY + _height;
        transform.Position = position;

        _animator?.SetFloat("Speed", planarSpeed);

        UpdateShooting(deltaTime);
    }

    // Fires the pistol only while aiming. Semi-automatic: one shot per click (button-down, not held) and
    // never faster than FireCooldown. Each shot plays the gunshot one-shot.
    private void UpdateShooting(float deltaTime)
    {
        _fireTimer -= deltaTime;
        if (!_aiming || _fireTimer > 0.0f || !Input.GetMouseButtonDown(MouseButton.Left))
        {
            return;
        }

        _fireTimer = FireCooldown;
        AudioApi.Play(_fireClip, FireVolume);
    }

    // Starts a jump on Space (when grounded and allowed) and integrates the vertical arc back down.
    private void UpdateJump(float deltaTime, bool allowStart)
    {
        if (allowStart && !_isJumping && Input.GetKeyDown(Key.Space))
        {
            _isJumping = true;
            _verticalVel = JumpSpeed;
            _animator?.SetTrigger("Jump");
        }

        if (!_isJumping)
        {
            return;
        }

        _verticalVel -= Gravity * deltaTime;
        _height += _verticalVel * deltaTime;
        if (_height <= 0.0f)
        {
            _height = 0.0f;
            _verticalVel = 0.0f;
            _isJumping = false;
        }
    }

    // Runs after movement so the camera tracks the character's final position for the frame.
    public override void OnLateUpdate(float deltaTime)
    {
        Entity? cameraEntity = Find(CameraName);
        if (cameraEntity is null || !cameraEntity.Value.TryGetComponent(out TransformComponent? camTransform))
        {
            return;
        }

        // Ease toward the aim framing so the camera pull-in is smooth, not a snap.
        _aimBlend = MoveTowards(_aimBlend, _aiming ? 1.0f : 0.0f, AimBlendSpeed * deltaTime);

        float distance = Lerp(CameraDistance, AimDistance, _aimBlend);
        float height = Lerp(CameraHeight, AimHeight, _aimBlend);

        float yawRad = _yaw * (MathF.PI / 180.0f);
        Vector3 right = Vector3.Transform(new Vector3(1.0f, 0.0f, 0.0f), Matrix4x4.CreateRotationY(yawRad));

        TransformComponent transform = GetComponent<TransformComponent>();
        Vector3 target = transform.Position
            + new Vector3(0.0f, height, 0.0f)
            + right * (AimShoulderOffset * _aimBlend);

        Matrix4x4 rotation = Matrix4x4.CreateFromYawPitchRoll(yawRad, _pitch * (MathF.PI / 180.0f), 0.0f);
        Vector3 forward = Vector3.Transform(new Vector3(0.0f, 0.0f, -1.0f), rotation);

        camTransform!.Position = target - forward * distance;
        camTransform.Rotation = new Vector3(_pitch, _yaw, 0.0f);
    }

    // Applies mouse movement to the orbit yaw/pitch and the scroll wheel to the zoom distance.
    private void UpdateCameraLook()
    {
        Vector2 mouse = Input.MousePosition;
        if (_firstFrame)
        {
            _lastMouse = mouse;
            _firstFrame = false;
        }

        Vector2 delta = mouse - _lastMouse;
        _lastMouse = mouse;

        _yaw -= delta.X * MouseSensitivity;
        _pitch = Math.Clamp(_pitch - delta.Y * MouseSensitivity, MinPitch, MaxPitch);

        float scroll = Input.MouseScrollDelta.Y;
        if (scroll != 0.0f)
        {
            CameraDistance = Math.Clamp(CameraDistance - scroll * 0.5f, MinDistance, MaxDistance);
        }
    }

    // Builds the centered crosshair once, hidden until the player aims: a bold white "+" of four bars and a
    // center dot, each with a dark outline so it reads over any background. Grouped under one transparent
    // panel so the whole reticle toggles with a single Visible flag.
    private void BuildCrosshair()
    {
        _crosshair = UI.Panel();
        _crosshair.Color = new Vector4(0.0f, 0.0f, 0.0f, 0.0f); // transparent container
        _crosshair.Rect = CenteredRect(new Vector2(CrosshairSize * 2.0f, CrosshairSize * 2.0f), Vector2.Zero);
        _crosshair.Visible = false;

        float length = CrosshairSize * 0.36f;
        float thickness = 3.0f;
        float gap = CrosshairSize * 0.18f;
        float offset = gap + length * 0.5f;

        AddBar(new Vector2(0.0f, -offset), new Vector2(thickness, length)); // top
        AddBar(new Vector2(0.0f, offset), new Vector2(thickness, length));  // bottom
        AddBar(new Vector2(-offset, 0.0f), new Vector2(length, thickness)); // left
        AddBar(new Vector2(offset, 0.0f), new Vector2(length, thickness));  // right
        AddBar(Vector2.Zero, new Vector2(thickness, thickness));            // center dot
    }

    // Adds one crosshair bar to the reticle: a dark, slightly larger backing rect for contrast with a bright
    // fill on top.
    private void AddBar(Vector2 offset, Vector2 size)
    {
        Panel outline = _crosshair!.Panel();
        outline.Color = new Vector4(0.0f, 0.0f, 0.0f, 0.65f);
        outline.Rect = CenteredRect(size + new Vector2(2.0f, 2.0f), offset);

        Panel fill = _crosshair.Panel();
        fill.Color = new Vector4(1.0f, 1.0f, 1.0f, 0.95f);
        fill.Rect = CenteredRect(size, offset);
    }

    // A rect centered on its parent's center, offset by the given amount (both in UI units).
    private static UIRect CenteredRect(Vector2 size, Vector2 offset) => new()
    {
        Anchor = new Vector2(0.5f, 0.5f),
        Pivot = new Vector2(0.5f, 0.5f),
        Position = offset,
        Size = size,
    };

    // Maps WASD input to the dominant strafe direction the aim-walk state machine expects:
    // 0 = idle, 1 = forward, 2 = back, 3 = left, 4 = right. Diagonals fall to the forward/back axis.
    private static int AimMoveDirection(Vector3 input, bool moving)
    {
        if (!moving)
        {
            return 0;
        }

        if (MathF.Abs(input.X) > MathF.Abs(input.Z))
        {
            return input.X < 0.0f ? 3 : 4;
        }

        return input.Z < 0.0f ? 1 : 2;
    }

    // The character yaw (degrees) that points its mesh forward along a world-space horizontal direction.
    // The engine's forward is -Z; ModelYawOffset corrects for a mesh whose modelled front differs.
    private float FacingForDirection(Vector3 dir) =>
        MathF.Atan2(-dir.X, -dir.Z) * (180.0f / MathF.PI) + ModelYawOffset;

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // Moves a value toward a target by at most maxDelta.
    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta)
        {
            return target;
        }

        return current + MathF.Sign(target - current) * maxDelta;
    }

    // Rotates an angle toward a target by at most maxDelta, taking the shortest way around the circle.
    private static float MoveTowardsAngle(float current, float target, float maxDelta)
    {
        float diff = ((target - current + 540.0f) % 360.0f) - 180.0f;
        if (MathF.Abs(diff) <= maxDelta)
        {
            return target;
        }

        return current + MathF.Sign(diff) * maxDelta;
    }
}

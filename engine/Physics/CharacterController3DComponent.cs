using System.Numerics;
using Spot.Scenes;

namespace Spot.Physics;

/// <summary>
/// A first-person character controller with Quake/CS-style movement: ground acceleration with
/// friction, air-strafing (accelerate toward the look+strafe direction under a small air-speed cap,
/// so turning the mouse while holding A/D builds speed), jumping, and smooth crouching. Pair it with a
/// dynamic <see cref="PhysicsBody3DComponent"/>, a <see cref="BoxCollider3DComponent"/>, and a child
/// entity carrying a <see cref="Spot.Scenes.CameraComponent"/> for the eyes.
/// </summary>
[ComponentMenu("Character Controller 3D", Order = 65)]
public class CharacterController3DComponent : Component
{
    // Speeds (units/second). Run is the default; hold Shift to walk, Ctrl to crouch.
    public float WalkSpeed { get; set; } = 4.0f;
    public float RunSpeed { get; set; } = 8.0f;
    public float CrouchSpeed { get; set; } = 2.5f;
    public float JumpForce { get; set; } = 6.0f;

    // Ground feel.
    public float GroundAcceleration { get; set; } = 10.0f;
    public float GroundFriction { get; set; } = 8.0f;

    // Air feel. AirAcceleration is intentionally large; MaxAirSpeed is the small cap on how much
    // speed you may gain *along* the wish direction each tick. Because the cap is on the projection
    // of velocity onto the wish direction (not on total speed), turning the mouse while holding a
    // strafe key redirects that cap perpendicular to your velocity and the total speed grows — this
    // is what makes air-strafing work.
    public float AirAcceleration { get; set; } = 100.0f;
    public float MaxAirSpeed { get; set; } = 1.0f;

    // Gravity multiplier applied to the body while controlled (higher = snappier fall).
    public float GravityMultiplier { get; set; } = 1.8f;

    // Crouch collider heights (units). Feet stay planted; the camera lowers by the difference.
    public float StandHeight { get; set; } = 1.7f;
    public float CrouchHeight { get; set; } = 0.9f;

    // Mouse look.
    public float MouseSensitivity { get; set; } = 0.1f;
    public float MaxPitch { get; set; } = 89.0f;
    public bool LockMouse { get; set; } = true;

    // Internal runtime state — hidden from the inspector and not serialized.
    [HideInInspector] public bool IsCrouching { get; set; }
    [HideInInspector] public bool IsGrounded { get; set; }
    [HideInInspector] public float CrouchAmount { get; set; }
    [HideInInspector] public float Pitch { get; set; }
    [HideInInspector] public float Yaw { get; set; }
    [HideInInspector] public Vector2 LastMousePos { get; set; }
    [HideInInspector] public bool FirstMouse { get; set; } = true;
    [HideInInspector] public float CameraBaseHeight { get; set; }
    [HideInInspector] public bool CameraCaptured { get; set; }
    [HideInInspector] public bool LoggedMissingBodyWarning { get; set; }
    [HideInInspector] public bool LoggedMissingColliderWarning { get; set; }
    [HideInInspector] public bool LoggedFallbackCameraWarning { get; set; }
}

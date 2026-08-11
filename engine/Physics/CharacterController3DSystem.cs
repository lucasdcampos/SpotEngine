using System;
using System.Numerics;
using Spot.Core;
using Spot.Scenes;
using Spot.Rendering;

namespace Spot.Physics;

/// <summary>
/// Drives every <see cref="CharacterController3DComponent"/>: mouse look, Quake/CS-style ground and
/// air movement, jumping, and smooth crouching. Runs before <see cref="Physics3DSystem"/> each tick so
/// the body velocity it writes is integrated the same frame.
/// </summary>
internal static class CharacterController3DSystem
{
    // How quickly the crouch pose blends in/out (fraction per second): ~0.1s to fully crouched.
    private const float CrouchBlendSpeed = 10.0f;

    public static void Update(Scene scene, float deltaTime)
    {
        if (deltaTime <= 0f) return;

        foreach (var entity in scene.View<CharacterController3DComponent>())
        {
            if (!entity.IsActiveInHierarchy()) continue;

            var cc = entity.GetComponent<CharacterController3DComponent>();
            if (!entity.TryGetComponent(out TransformComponent? transform) || !transform.Enabled) continue;

            // A character needs a dynamic, upright body and a collider; create sensible defaults if missing.
            if (!entity.TryGetComponent(out PhysicsBody3DComponent? body))
            {
                if (!cc.LoggedMissingBodyWarning)
                {
                    Log.CoreWarn("CharacterController3D on Entity {0} is missing a PhysicsBody3DComponent. Adding one.", entity.Id);
                    cc.LoggedMissingBodyWarning = true;
                }
                body = new PhysicsBody3DComponent { IsDynamic = true };
                entity.AddComponent(body);
            }
            body.FreezeRotation = true; // an upright character must never tip over under the simulation

            CharacterCollider? collider = ResolveCollider(entity);
            if (collider is null)
            {
                if (!cc.LoggedMissingColliderWarning)
                {
                    Log.CoreWarn("CharacterController3D on Entity {0} is missing a collider. Adding a capsule.", entity.Id);
                    cc.LoggedMissingColliderWarning = true;
                }
                var capsule = new CapsuleCollider3DComponent
                {
                    Radius = 0.3f,
                    Length = MathF.Max(0.1f, cc.StandHeight - 0.6f),
                    Offset = new Vector3(0, cc.StandHeight * 0.5f, 0)
                };
                entity.AddComponent(capsule);
                collider = new CharacterCollider(capsule);
            }

            if (!body.Enabled || (!collider.Enabled && !cc.IsNoClip)) continue;

            if (cc.FirstMouse)
            {
                cc.LastMousePos = Input.MousePosition;
                cc.FirstMouse = false;
                if (cc.LockMouse) Input.CursorLocked = true;
            }

            if (Input.GetKeyDown(Key.V))
            {
                cc.IsNoClip = !cc.IsNoClip;
                collider.Enabled = !cc.IsNoClip;
                if (cc.IsNoClip)
                {
                    body.Velocity = Vector3.Zero;
                    body.IsDynamic = false;
                }
                else
                {
                    body.IsDynamic = true;
                }
            }

            ApplyMouseLook(cc);

            // Yaw drives the body; the camera inherits it and adds pitch (see ApplyCameraPose).
            transform.Rotation = new Vector3(0, cc.Yaw, 0);

            ApplyCrouch(cc, collider, deltaTime);

            var cameraEntity = ResolveCamera(scene, entity, cc);
            ApplyCameraPose(entity, cameraEntity, transform, cc, body, deltaTime);

            if (cc.IsNoClip)
            {
                cc.IsGrounded = false;
                NoClipMove(cc, transform, deltaTime);
            }
            else
            {
                // Grounded is set by the physics step when a floor contact pushed us up last frame. This
                // beats the old "vertical velocity ≈ 0" heuristic, which also fired at the jump apex and
                // let the player jump a second time there.
                cc.IsGrounded = body.Grounded;

                Move(cc, body, deltaTime);
                body.GravityScale = cc.GravityMultiplier;
            }
        }
    }

    private static void ApplyMouseLook(CharacterController3DComponent cc)
    {
        // Only steer while the cursor is captured (unless locking is disabled).
        if (!cc.LockMouse || Input.CursorLocked)
        {
            Vector2 delta = Input.MousePosition - cc.LastMousePos;
            cc.Yaw -= delta.X * cc.MouseSensitivity;
            cc.Pitch = Math.Clamp(cc.Pitch - delta.Y * cc.MouseSensitivity, -cc.MaxPitch, cc.MaxPitch);
        }
        cc.LastMousePos = Input.MousePosition;
    }

    private static void ApplyCrouch(CharacterController3DComponent cc, CharacterCollider collider, float deltaTime)
    {
        cc.IsCrouching = Input.GetKey(Key.LeftControl);
        float target = cc.IsCrouching ? 1f : 0f;
        cc.CrouchAmount = MoveTowards(cc.CrouchAmount, target, deltaTime * CrouchBlendSpeed);

        // Blend the collider height and keep the feet planted at the transform origin, so crouching
        // shrinks from the head down.
        float height = Lerp(cc.StandHeight, cc.CrouchHeight, cc.CrouchAmount);
        collider.SetHeight(height);
    }

    private static CharacterCollider? ResolveCollider(Entity entity)
    {
        if (entity.TryGetComponent(out CapsuleCollider3DComponent? capsule)) return new CharacterCollider(capsule);
        if (entity.TryGetComponent(out BoxCollider3DComponent? box)) return new CharacterCollider(box);
        return null;
    }

    /// <summary>
    /// Adapts either a capsule or a box collider to the height/enabled operations the character needs,
    /// so a character authored with either shape works. Feet stay planted at the transform origin.
    /// </summary>
    private sealed class CharacterCollider
    {
        private readonly BoxCollider3DComponent? _box;
        private readonly CapsuleCollider3DComponent? _capsule;

        public CharacterCollider(BoxCollider3DComponent box) => _box = box;
        public CharacterCollider(CapsuleCollider3DComponent capsule) => _capsule = capsule;

        public bool Enabled
        {
            get => _box?.Enabled ?? _capsule!.Enabled;
            set
            {
                if (_box is not null) _box.Enabled = value;
                else _capsule!.Enabled = value;
            }
        }

        public void SetHeight(float height)
        {
            if (_box is not null)
            {
                _box.Size = new Vector3(_box.Size.X, height, _box.Size.Z);
                _box.Offset = new Vector3(_box.Offset.X, height * 0.5f, _box.Offset.Z);
            }
            else
            {
                float radius = _capsule!.Radius;
                _capsule.Length = MathF.Max(0.05f, height - 2f * radius);
                _capsule.Offset = new Vector3(_capsule.Offset.X, height * 0.5f, _capsule.Offset.Z);
            }
        }
    }

    private static Entity? ResolveCamera(Scene scene, Entity player, CharacterController3DComponent cc)
    {
        if (player.TryGetComponent(out RelationshipComponent? rel))
        {
            foreach (var child in rel.Children)
            {
                if (child.HasComponent<CameraComponent>())
                    return child;
            }
        }

        // No child camera: fall back to the scene's primary camera (warn once).
        if (!cc.LoggedFallbackCameraWarning)
        {
            Log.CoreWarn("CharacterController3D on Entity {0} has no child Camera; using the primary camera.", player.Id);
            cc.LoggedFallbackCameraWarning = true;
        }
        foreach (var camEnt in scene.View<CameraComponent>())
        {
            if (camEnt.GetComponent<CameraComponent>().Primary)
                return camEnt;
        }
        return null;
    }

    private static void ApplyCameraPose(Entity player, Entity? cameraEntity, TransformComponent bodyTransform, CharacterController3DComponent cc, PhysicsBody3DComponent body, float deltaTime)
    {
        if (!cameraEntity.HasValue) return;
        if (!cameraEntity.Value.TryGetComponent(out TransformComponent? camTransform)) return;

        float crouchDrop = (cc.StandHeight - cc.CrouchHeight) * cc.CrouchAmount;
        float bobOffset = 0f;

        if (cc.EnableBobbing && body.Grounded)
        {
            Vector3 horizontalVelocity = new Vector3(body.Velocity.X, 0, body.Velocity.Z);
            float speed = horizontalVelocity.Length();
            
            if (speed > 0.1f)
            {
                float speedRatio = MathF.Min(speed / cc.RunSpeed, 1.0f);
                cc.BobTimer += deltaTime * cc.BobFrequency * speedRatio;
            }
            
            float amplitudeFactor = MathF.Min(speed / cc.WalkSpeed, 1.5f);
            bobOffset = MathF.Sin(cc.BobTimer) * cc.BobAmplitude * amplitudeFactor;
        }

        bool isChild = cameraEntity.Value.TryGetComponent(out RelationshipComponent? camRel)
                       && camRel.Parent?.Id == player.Id;

        if (isChild)
        {
            // Capture the authored eye height once so crouch lowers from wherever the camera sits.
            if (!cc.CameraCaptured)
            {
                cc.CameraBaseHeight = camTransform.Position.Y;
                cc.CameraCaptured = true;
            }
            camTransform.Position = new Vector3(camTransform.Position.X, cc.CameraBaseHeight - crouchDrop + bobOffset, camTransform.Position.Z);
            camTransform.Rotation = new Vector3(cc.Pitch, 0, 0); // yaw is inherited from the body
        }
        else
        {
            // Detached camera: follow the body and apply both yaw and pitch ourselves.
            float eyeHeight = cc.StandHeight - 0.1f - crouchDrop + bobOffset;
            camTransform.Position = bodyTransform.Position + new Vector3(0, eyeHeight, 0);
            camTransform.Rotation = new Vector3(cc.Pitch, cc.Yaw, 0);
        }
    }

    private static void NoClipMove(CharacterController3DComponent cc, TransformComponent transform, float deltaTime)
    {
        Vector3 input = Vector3.Zero;
        if (Input.GetKey(Key.W)) input.Z -= 1;
        if (Input.GetKey(Key.S)) input.Z += 1;
        if (Input.GetKey(Key.A)) input.X -= 1;
        if (Input.GetKey(Key.D)) input.X += 1;
        if (Input.GetKey(Key.Space)) input.Y += 1;
        if (Input.GetKey(Key.LeftControl)) input.Y -= 1;

        input = SpotMath.SafeNormalize(input, input);

        // Noclip uses Pitch and Yaw to fly exactly where we look
        Matrix4x4 rotation = Matrix4x4.CreateFromYawPitchRoll(cc.Yaw * (MathF.PI / 180f), cc.Pitch * (MathF.PI / 180f), 0);
        Vector3 wishDir = Vector3.Transform(input, rotation);

        float speed = cc.NoClipSpeed;
        if (Input.GetKey(Key.LeftShift)) speed *= 2.5f;

        transform.Position += wishDir * speed * deltaTime;
    }

    private static void Move(CharacterController3DComponent cc, PhysicsBody3DComponent body, float deltaTime)
    {
        // Local input: X = strafe (A/D), Z = forward (W/S, forward is -Z).
        Vector3 input = Vector3.Zero;
        if (Input.GetKey(Key.W)) input.Z -= 1;
        if (Input.GetKey(Key.S)) input.Z += 1;
        if (Input.GetKey(Key.A)) input.X -= 1;
        if (Input.GetKey(Key.D)) input.X += 1;
        input = SpotMath.SafeNormalize(input, input);

        // Rotate into world space by yaw so movement follows where we look.
        Vector3 wishDir = Vector3.Transform(input, Matrix4x4.CreateRotationY(cc.Yaw * (MathF.PI / 180f)));

        float targetSpeed = cc.RunSpeed;
        if (cc.IsCrouching) targetSpeed = cc.CrouchSpeed;
        else if (Input.GetKey(Key.LeftShift)) targetSpeed = cc.WalkSpeed;

        // Split channels: gravity and jumping own the vertical, movement owns the horizontal.
        Vector3 horizontal = new Vector3(body.Velocity.X, 0, body.Velocity.Z);
        float verticalVel = body.Velocity.Y;

        if (cc.IsGrounded)
        {
            bool jumping = Input.GetKeyDown(Key.Space);

            // Skip friction on the jump tick so a well-timed jump keeps speed (bhop).
            if (!jumping)
                horizontal = ApplyFriction(horizontal, cc.GroundFriction, deltaTime);

            horizontal = Accelerate(horizontal, wishDir, targetSpeed, cc.GroundAcceleration, deltaTime);

            if (jumping)
            {
                verticalVel = cc.JumpForce;
                cc.IsGrounded = false;
            }
        }
        else
        {
            // Air-strafing: accelerate toward wishDir but only up to the small projection cap.
            horizontal = Accelerate(horizontal, wishDir, cc.MaxAirSpeed, cc.AirAcceleration, deltaTime);
        }

        body.Velocity = new Vector3(horizontal.X, verticalVel, horizontal.Z);
    }

    /// <summary>Quake-style friction: bleed a fraction of the current speed each tick.</summary>
    private static Vector3 ApplyFriction(Vector3 velocity, float friction, float deltaTime)
    {
        float speed = velocity.Length();
        if (speed < 1e-4f) return Vector3.Zero;
        float newSpeed = MathF.Max(speed - speed * friction * deltaTime, 0f);
        return velocity * (newSpeed / speed);
    }

    /// <summary>
    /// Quake-style acceleration: add speed toward <paramref name="wishDir"/> only until the velocity's
    /// projection onto it reaches <paramref name="wishSpeed"/>. On the ground <paramref name="wishSpeed"/>
    /// is the full move speed; in the air it is the small air cap that enables strafing.
    /// </summary>
    private static Vector3 Accelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float accel, float deltaTime)
    {
        float projSpeed = Vector3.Dot(velocity, wishDir);
        float addSpeed = wishSpeed - projSpeed;
        if (addSpeed <= 0f) return velocity;
        float accelSpeed = MathF.Min(accel * wishSpeed * deltaTime, addSpeed);
        return velocity + wishDir * accelSpeed;
    }

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        if (MathF.Abs(target - current) <= maxDelta) return target;
        return current + MathF.Sign(target - current) * maxDelta;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}

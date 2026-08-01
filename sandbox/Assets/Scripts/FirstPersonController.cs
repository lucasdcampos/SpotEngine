using System;
using System.Numerics;
using Spot.Core;
using Spot.Scenes;
using Spot.Physics;
using Spot.Rendering;
using Silk.NET.Input;
using Key = Spot.Core.Key;
using MouseButton = Spot.Core.MouseButton;

public class FirstPersonController : EntityBehaviour
{
    private TransformComponent? _transform;
    private PhysicsBody3DComponent? _body;
    
    private float _yaw = 0f;
    private float _pitch = 0f;
    private Vector2 _lastMousePos;
    
    public float MouseSensitivity = 0.1f;
    public float MaxSpeed = 5.0f;
    public float MaxAirSpeed = 0.6f;
    public float GroundAccel = 10.0f;
    public float AirAccel = 100.0f;
    public float Friction = 8.0f;
    public float JumpForce = 7.0f;

    public override void OnCreate()
    {
        _transform = Entity.GetComponent<TransformComponent>();

        if (!Entity.HasComponent<PhysicsBody3DComponent>())
        {
            // Adding a dynamic physics body
            _body = new PhysicsBody3DComponent { IsDynamic = true, GravityScale = 3.0f };
            Entity.AddComponent(_body);
        }
        else
        {
            _body = Entity.GetComponent<PhysicsBody3DComponent>();
        }

        if (!Entity.HasComponent<BoxCollider3DComponent>())
        {
            // Simple human-sized collider
            Entity.AddComponent(new BoxCollider3DComponent { Size = new Vector3(1, 2, 1) });
        }

        if (!Entity.HasComponent<CameraComponent>())
        {
            Entity.AddComponent(new CameraComponent 
            { 
                Primary = true, 
                ProjectionType = SceneCameraProjection.Perspective,
                FieldOfView = 60f
            });
        }
        
        _lastMousePos = Input.MousePosition;
        _yaw = _transform.Rotation.Y;
        _pitch = _transform.Rotation.X;

        // Hide and lock the cursor to the window
        var inputCtx = Application.Instance.Window.Input;
        if (inputCtx.Mice.Count > 0)
        {
            inputCtx.Mice[0].Cursor.CursorMode = CursorMode.Raw;
        }
    }

    private void ApplyFriction(ref Vector2 vel, float t)
    {
        float speed = vel.Length();
        if (speed < 0.1f)
        {
            vel = Vector2.Zero;
            return;
        }

        // Drop speed based on friction
        float control = speed < 2.0f ? 2.0f : speed;
        float drop = control * Friction * t;
        
        float newSpeed = speed - drop;
        if (newSpeed < 0) newSpeed = 0;
        newSpeed /= speed;
        
        vel *= newSpeed;
    }

    private void Accelerate(ref Vector2 vel, Vector2 wishDir, float wishSpeed, float accel, float dt)
    {
        float currentSpeed = Vector2.Dot(vel, wishDir);
        float addSpeed = wishSpeed - currentSpeed;
        if (addSpeed <= 0) return;
        
        float accelSpeed = accel * dt * wishSpeed;
        if (accelSpeed > addSpeed) accelSpeed = addSpeed;
        
        vel += wishDir * accelSpeed;
    }

    public override void OnUpdate(float ts)
    {
        if (Input.GetKeyDown(Key.Escape))
        {
            Application.Instance.Quit();
        }

        if (_body == null || _transform == null) return;

        // Mouse look
        Vector2 mouseDelta = Input.MousePosition - _lastMousePos;
        _lastMousePos = Input.MousePosition;

        _yaw -= mouseDelta.X * MouseSensitivity;
        _pitch -= mouseDelta.Y * MouseSensitivity;
        _pitch = Math.Clamp(_pitch, -89f, 89f);

        _transform.Rotation = new Vector3(_pitch, _yaw, 0);

        // Movement (relative to camera yaw)
        Vector3 forward = Vector3.Transform(new Vector3(0, 0, -1), Matrix4x4.CreateFromYawPitchRoll(_yaw * MathF.PI / 180f, 0, 0));
        Vector3 right = Vector3.Transform(new Vector3(1, 0, 0), Matrix4x4.CreateFromYawPitchRoll(_yaw * MathF.PI / 180f, 0, 0));

        Vector3 moveDir = Vector3.Zero;
        if (Input.GetKey(Key.W)) moveDir += forward;
        if (Input.GetKey(Key.S)) moveDir -= forward;
        if (Input.GetKey(Key.D)) moveDir += right;
        if (Input.GetKey(Key.A)) moveDir -= right;

        if (moveDir.LengthSquared() > 0)
        {
            moveDir = Vector3.Normalize(moveDir);
        }

        bool isGrounded = Math.Abs(_body.Velocity.Y) < 0.1f;
        Vector2 wishDirXZ = new Vector2(moveDir.X, moveDir.Z);
        Vector2 currentVelXZ = new Vector2(_body.Velocity.X, _body.Velocity.Z);

        if (isGrounded)
        {
            ApplyFriction(ref currentVelXZ, ts);
            
            float wishSpeed = wishDirXZ.LengthSquared() > 0 ? MaxSpeed : 0;
            Accelerate(ref currentVelXZ, wishDirXZ, wishSpeed, GroundAccel, ts);

            // Jump
            if (Input.GetKeyDown(Key.Space))
            {
                _body.Velocity = new Vector3(_body.Velocity.X, JumpForce, _body.Velocity.Z);
            }
        }
        else
        {
            // CS:GO style air strafing
            float wishSpeed = wishDirXZ.LengthSquared() > 0 ? MaxAirSpeed : 0;
            Accelerate(ref currentVelXZ, wishDirXZ, wishSpeed, AirAccel, ts);
        }

        // Apply updated horizontal velocity but preserve Y velocity (gravity)
        _body.Velocity = new Vector3(currentVelXZ.X, _body.Velocity.Y, currentVelXZ.Y);
    }
}


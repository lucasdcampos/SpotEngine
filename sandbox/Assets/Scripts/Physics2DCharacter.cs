using System;
using System.Numerics;
using Spot.Core;
using Spot.Physics;
using Spot.Scenes;

namespace Spot.Game;

/// <summary>
/// A physics-driven platformer character for the Physics 2D playground. Horizontal movement is
/// <b>acceleration-based</b>: each frame it reads the body's actual (post-solve) velocity and eases it toward
/// the target speed, so pushing into a wall or a jammed body never re-injects full speed and flings things.
/// Jumps when grounded — grounded is a short downward <see cref="Scene.Raycast2D"/> from the body's centre.
/// The body should have <c>FreezeRotation</c> on and <c>Friction</c> 0 (so it never clings to walls).
/// </summary>
public sealed class Physics2DCharacter : EntityBehaviour
{
    /// <summary>Top horizontal speed in world units per second.</summary>
    public float MoveSpeed { get; set; } = 7.0f;

    /// <summary>Upward velocity applied on jump, in world units per second.</summary>
    public float JumpSpeed { get; set; } = 9.5f;

    /// <summary>How quickly ground movement reaches the target speed (units/second²).</summary>
    public float GroundAccel { get; set; } = 70.0f;

    /// <summary>How quickly airborne movement reaches the target speed (units/second²).</summary>
    public float AirAccel { get; set; } = 30.0f;

    /// <summary>How quickly the character stops on the ground when there is no input (units/second²).</summary>
    public float GroundDecel { get; set; } = 80.0f;

    /// <summary>Safety cap so a stray high-energy contact can never launch the character off-screen.</summary>
    public float MaxSpeed { get; set; } = 24.0f;

    public override void OnUpdate(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        if (!Entity.TryGetComponent(out TransformComponent? transform)) return;
        if (!Entity.TryGetComponent(out PhysicsBody2DComponent? body)) return;

        int input = 0;
        if (Input.GetKey(Key.A) || Input.GetKey(Key.Left)) input -= 1;
        if (Input.GetKey(Key.D) || Input.GetKey(Key.Right)) input += 1;

        Vector2 velocity = body!.Velocity;

        // Grounded only when not rising, so the jump's ascent isn't mistaken for standing on the floor.
        float halfHeight = MathF.Abs(transform!.Scale.Y) * 0.5f;
        var origin = new Vector2(transform.Position.X, transform.Position.Y);
        bool grounded = velocity.Y <= 1.0f
            && Scene.Raycast2D(origin, new Vector2(0f, -1f), halfHeight + 0.12f, out _);

        // Ease horizontal velocity toward the target; reading the real (blocked) velocity makes this
        // self-limiting against walls and jammed bodies instead of slamming full speed into them.
        if (input != 0)
        {
            float accel = grounded ? GroundAccel : AirAccel;
            velocity.X = MoveToward(velocity.X, input * MoveSpeed, accel * deltaTime);
        }
        else if (grounded)
        {
            velocity.X = MoveToward(velocity.X, 0f, GroundDecel * deltaTime);
        }
        // Airborne with no input: keep horizontal momentum.

        if (grounded && (Input.GetKeyDown(Key.Space) || Input.GetKeyDown(Key.W) || Input.GetKeyDown(Key.Up)))
        {
            velocity.Y = JumpSpeed;
        }

        if (velocity.LengthSquared() > MaxSpeed * MaxSpeed)
        {
            velocity = Vector2.Normalize(velocity) * MaxSpeed;
        }

        body.Velocity = velocity;
    }

    private static float MoveToward(float current, float target, float maxDelta)
    {
        float delta = target - current;
        if (MathF.Abs(delta) <= maxDelta) return target;
        return current + MathF.Sign(delta) * maxDelta;
    }
}

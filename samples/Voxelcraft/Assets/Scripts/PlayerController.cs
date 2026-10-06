using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Engine;

namespace Voxelcraft;

/// <summary>
/// The player: a 0.6×1.8 box that walks, sprints, sneaks (without falling off edges), jumps, swims and flies,
/// colliding with the blocks axis by axis; a first-person camera with mouse look, a little head bob and a wider
/// view at a sprint; and the hands — break with the left button, place with the right, pick with the middle, and
/// a nine-slot hotbar on 1-9 and the wheel.
/// </summary>
public sealed class PlayerController : Component
{
    public const float HalfWidth = 0.3f;
    public const float BodyHeight = 1.8f;
    public const float EyeHeight = 1.62f;

    [InspectorRange(1.0f, 10.0f, 0.1f)]
    public float WalkSpeed { get; set; } = 4.3f;

    [InspectorRange(1.0f, 15.0f, 0.1f)]
    public float SprintSpeed { get; set; } = 5.8f;

    [InspectorRange(2.0f, 60.0f, 0.5f)]
    public float FlySpeed { get; set; } = 11.0f;

    [InspectorRange(0.5f, 3.0f, 0.05f)]
    public float JumpHeight { get; set; } = 1.25f;

    [InspectorRange(5.0f, 60.0f, 0.5f)]
    public float Gravity { get; set; } = 32.0f;

    /// <summary>Gets or sets the mouse sensitivity, in degrees per pixel.</summary>
    [InspectorRange(0.01f, 1.0f, 0.01f)]
    public float MouseSensitivity { get; set; } = 0.11f;

    /// <summary>Gets or sets how far away blocks can be reached, in blocks.</summary>
    [InspectorRange(2.0f, 12.0f, 0.5f)]
    public float Reach { get; set; } = 6.0f;

    [InspectorRange(30.0f, 120.0f, 1.0f)]
    public float FieldOfView { get; set; } = 74.0f;

    public static PlayerController? Current { get; private set; }

    /// <summary>Gets the position of the player's feet.</summary>
    public Vector3 Position { get; private set; }

    public Vector3 Velocity { get; private set; }

    public Vector3 EyePosition => Position + new Vector3(0.0f, EyeHeight - (Sneaking ? 0.12f : 0.0f), 0.0f);

    public float Yaw { get; private set; }

    public float Pitch { get; private set; }

    public bool OnGround { get; private set; }

    public bool Flying { get; private set; }

    public bool Sprinting { get; private set; }

    public bool Sneaking { get; private set; }

    public bool InWater { get; private set; }

    /// <summary>Gets the block under the crosshair, if any is within reach.</summary>
    public BlockHit? Target { get; private set; }

    /// <summary>Gets the blocks on the hotbar.</summary>
    public BlockId[] Hotbar { get; } =
    {
        BlockId.Grass, BlockId.Dirt, BlockId.Stone, BlockId.Cobblestone, BlockId.Planks,
        BlockId.OakLog, BlockId.Glass, BlockId.Bricks, BlockId.Glowstone,
    };

    public int Selected { get; set; }

    public BlockId SelectedBlock => Hotbar[Math.Clamp(Selected, 0, Hotbar.Length - 1)];

    /// <summary>Gets the time since the selected slot changed (the HUD shows the block's name for a moment).</summary>
    public float SinceSelect { get; private set; } = 100.0f;

    /// <summary>Gets how far the hand has swung, 0..1, for the HUD.</summary>
    public float Swing { get; private set; }

    private Transform? _camera;
    private Camera? _cameraComponent;
    private bool _spawned;
    private Vector2 _lastMouse;
    private bool _hadControl;
    private float _controlTime;
    private float _breakCooldown;
    private float _placeCooldown;
    private float _lastSpace = -10.0f;
    private float _lastForward = -10.0f;
    private float _time;
    private float _bob;
    private float _bobAmount;
    private float _fov;
    private float _landDip;
    private float _fallSpeed;

    public override void OnStart()
    {
        Current = this;
        foreach (Entity child in Entity.Children)
        {
            if (child.TryGetComponent(out Camera? camera))
            {
                _camera = child.GetComponent<Transform>();
                _cameraComponent = camera;
                break;
            }
        }

        _fov = FieldOfView;
    }

    public override void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    /// <summary>Puts the player somewhere, standing still.</summary>
    public void Teleport(Vector3 feet, float? yaw = null)
    {
        Position = feet;
        Velocity = Vector3.Zero;
        if (yaw is { } y) Yaw = y;
        SyncTransforms();
    }

    /// <summary>Turns the view: yaw in degrees (0 looks north, growing to the west), pitch in degrees (up is positive).</summary>
    public void SetView(float yaw, float pitch)
    {
        Yaw = yaw;
        Pitch = Math.Clamp(pitch, -89.5f, 89.5f);
        SyncTransforms();
    }

    /// <summary>Starts or stops flying.</summary>
    public void SetFlying(bool flying)
    {
        Flying = flying;
        Velocity = Vector3.Zero;
    }

    public override void OnUpdate(float deltaTime)
    {
        World? world = VoxelWorld.Current?.World;
        if (world is null) return;

        if (!_spawned)
        {
            _spawned = true;
            Teleport(VoxelWorld.Current!.Spawn + new Vector3(0.0f, 0.02f, 0.0f), 225.0f);
        }

        _time += deltaTime;
        SinceSelect += deltaTime;
        Swing = MathF.Max(0.0f, Swing - deltaTime * 4.0f);
        bool control = Game.Current?.InControl ?? true;
        if (control && !_hadControl) _controlTime = 0.0f;
        _hadControl = control;
        _controlTime += deltaTime;

        Look(control);
        if (control)
        {
            Hotkeys();
            DoubleTaps();
        }

        // Hold still until the ground under the player exists.
        bool loaded = (Game.Current?.Loaded ?? true) && world.IsLoaded((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Z));
        if (loaded)
        {
            float dt = MathF.Min(deltaTime, 0.05f);
            int steps = Math.Max(1, (int)MathF.Ceiling(dt / (1.0f / 120.0f)));
            for (int i = 0; i < steps; i++) Step(world, dt / steps, control);
        }
        else
        {
            Velocity = Vector3.Zero;
        }

        // Fell out of the world somehow: back to the surface.
        if (Position.Y < -20.0f)
        {
            int x = (int)MathF.Floor(Position.X);
            int z = (int)MathF.Floor(Position.Z);
            Teleport(new Vector3(Position.X, world.SurfaceAt(x, z) + 1.5f, Position.Z));
        }

        Target = world.Raycast(EyePosition, Forward, Reach, out BlockHit hit) ? hit : null;
        if (control && _controlTime > 0.2f) Interact(world);
        SyncTransforms();
    }

    /// <summary>Gets the direction the camera looks.</summary>
    public Vector3 Forward
    {
        get
        {
            Matrix4x4 rotation = Matrix4x4.CreateFromYawPitchRoll(Yaw * MathF.PI / 180.0f, Pitch * MathF.PI / 180.0f, 0.0f);
            return Vector3.TransformNormal(-Vector3.UnitZ, rotation);
        }
    }

    private void Look(bool control)
    {
        Vector2 mouse = Input.MousePosition;
        if (control && Input.CursorLocked)
        {
            Vector2 delta = mouse - _lastMouse;
            // Ignore the jump a re-locked cursor can report on its first frame.
            if (delta.LengthSquared() < 400.0f * 400.0f)
            {
                Yaw -= delta.X * MouseSensitivity;
                Pitch = Math.Clamp(Pitch - delta.Y * MouseSensitivity, -89.5f, 89.5f);
            }
        }

        Yaw = (Yaw % 360.0f + 360.0f) % 360.0f;
        _lastMouse = mouse;
    }

    private void Hotkeys()
    {
        for (int i = 0; i < 9; i++)
        {
            if (Input.GetKeyDown(Key.Alpha1 + i)) Select(i);
        }

        float scroll = Input.MouseScrollDelta.Y;
        if (scroll > 0.1f) Select((Selected + 8) % 9);
        else if (scroll < -0.1f) Select((Selected + 1) % 9);

        if (Input.GetKeyDown(Key.F))
        {
            Flying = !Flying;
            Velocity = Velocity with { Y = 0.0f };
            Game.Current?.Notify(Flying ? "Flying" : "Walking");
        }
    }

    // Once per frame, not per physics step: a key goes down in one frame, however many steps that frame takes.
    private void DoubleTaps()
    {
        if (Input.GetKeyDown(Key.Space))
        {
            // A double tap of the jump key takes off or lands, like the creative mode it imitates.
            if (_time - _lastSpace < 0.3f)
            {
                Flying = !Flying;
                Velocity = Velocity with { Y = 0.0f };
                _lastSpace = -10.0f;
            }
            else
            {
                _lastSpace = _time;
            }
        }

        if (Input.GetKeyDown(Key.W))
        {
            if (_time - _lastForward < 0.3f) Sprinting = true;
            _lastForward = _time;
        }
    }

    public void Select(int slot)
    {
        Selected = Math.Clamp(slot, 0, Hotbar.Length - 1);
        SinceSelect = 0.0f;
    }

    // ---- movement ----

    private void Step(World world, float dt, bool control)
    {
        Vector3 wish = Vector3.Zero;
        bool jump = false;
        bool down = false;
        if (control)
        {
            if (Input.GetKey(Key.W)) wish.Z -= 1.0f;
            if (Input.GetKey(Key.S)) wish.Z += 1.0f;
            if (Input.GetKey(Key.A)) wish.X -= 1.0f;
            if (Input.GetKey(Key.D)) wish.X += 1.0f;
            jump = Input.GetKey(Key.Space);
            down = Input.GetKey(Key.LeftShift);
        }

        if (wish.LengthSquared() > 0.0f) wish = Vector3.Normalize(wish);
        bool forward = wish.Z < 0.0f;
        if (control && forward && Input.GetKey(Key.LeftControl)) Sprinting = true;
        if (!forward || (down && !Flying)) Sprinting = false;

        Sneaking = down && !Flying && !InWater;
        Vector3 move = Vector3.Transform(wish, Matrix4x4.CreateRotationY(Yaw * MathF.PI / 180.0f));

        InWater = IsWater(world, Position + new Vector3(0.0f, 0.3f, 0.0f));
        Vector3 v = Velocity;
        if (Flying)
        {
            float speed = FlySpeed * (Sprinting ? 2.0f : 1.0f);
            Vector3 target = move * speed;
            float k = 1.0f - MathF.Exp(-10.0f * dt);
            v.X += (target.X - v.X) * k;
            v.Z += (target.Z - v.Z) * k;
            float vertical = (jump ? 1.0f : 0.0f) - (down ? 1.0f : 0.0f);
            v.Y += (vertical * speed * 0.75f - v.Y) * k;
        }
        else if (InWater)
        {
            float speed = WalkSpeed * 0.55f;
            float k = 1.0f - MathF.Exp(-6.0f * dt);
            v.X += (move.X * speed - v.X) * k;
            v.Z += (move.Z * speed - v.Z) * k;
            v.Y -= Gravity * 0.2f * dt;
            if (jump) v.Y += 26.0f * dt;
            v.Y = Math.Clamp(v.Y, -3.0f, 3.2f);
            v.Y *= MathF.Exp(-1.5f * dt);
        }
        else
        {
            float speed = Sneaking ? WalkSpeed * 0.3f : Sprinting ? SprintSpeed : WalkSpeed;
            float k = 1.0f - MathF.Exp((OnGround ? -14.0f : -2.6f) * dt);
            v.X += (move.X * speed - v.X) * k;
            v.Z += (move.Z * speed - v.Z) * k;
            v.Y -= Gravity * dt;
            v.Y = MathF.Max(v.Y, -60.0f);
            if (jump && OnGround) v.Y = MathF.Sqrt(2.0f * Gravity * JumpHeight);
        }

        Velocity = v;
        Vector3 p = Position;
        bool wasGrounded = OnGround;
        OnGround = false;

        // Sneaking on an edge: refuse any horizontal step that would leave nothing underfoot.
        float dx = v.X * dt;
        float dz = v.Z * dt;
        if (Sneaking && wasGrounded)
        {
            if (!HasGround(world, p + new Vector3(dx, 0.0f, 0.0f))) dx = 0.0f;
            if (!HasGround(world, p + new Vector3(dx, 0.0f, dz))) dz = 0.0f;
        }

        bool hitY = MoveAxis(world, ref p, 1, v.Y * dt);
        bool hitX = MoveAxis(world, ref p, 0, dx);
        bool hitZ = MoveAxis(world, ref p, 2, dz);
        if (hitY)
        {
            if (v.Y < 0.0f)
            {
                OnGround = true;
                if (!wasGrounded && _fallSpeed > 7.0f) _landDip = Math.Clamp((_fallSpeed - 7.0f) / 12.0f, 0.15f, 1.0f);
                if (Flying)
                {
                    Flying = false; // touching down ends a flight
                }
            }

            v.Y = 0.0f;
        }

        if (hitX) v.X = 0.0f;
        if (hitZ) v.Z = 0.0f;
        if (dx == 0.0f && Sneaking) v.X = 0.0f;
        if (dz == 0.0f && Sneaking) v.Z = 0.0f;
        _fallSpeed = OnGround || InWater || Flying ? 0.0f : MathF.Max(_fallSpeed, -v.Y);
        Velocity = v;
        Position = p;

        if (OnGround && !Flying && new Vector2(v.X, v.Z).LengthSquared() > 0.5f)
        {
            _bob += new Vector2(v.X, v.Z).Length() * dt * 1.9f;
            _bobAmount = MathF.Min(1.0f, _bobAmount + dt * 4.0f);
        }
        else
        {
            _bobAmount = MathF.Max(0.0f, _bobAmount - dt * 4.0f);
        }
    }

    // Moves the box along one axis and stops it flush against the first block in the way. Only blocks ahead of the
    // box's leading face can stop it, and along the other two axes the box is shrunk by a hair, so a box resting on
    // the ground or against a wall slides along it freely.
    private static bool MoveAxis(World world, ref Vector3 position, int axis, float delta)
    {
        if (delta == 0.0f) return false;
        const float epsilon = 0.001f;
        var extentLow = new Vector3(HalfWidth, 0.0f, HalfWidth);
        var extentHigh = new Vector3(HalfWidth, BodyHeight, HalfWidth);
        Vector3 oldMin = position - extentLow;
        Vector3 oldMax = position + extentHigh;
        Vector3 moved = position;
        if (axis == 0) moved.X += delta;
        else if (axis == 1) moved.Y += delta;
        else moved.Z += delta;
        Vector3 min = moved - extentLow;
        Vector3 max = moved + extentHigh;

        Span<int> lo = stackalloc int[3];
        Span<int> hi = stackalloc int[3];
        for (int i = 0; i < 3; i++)
        {
            if (i == axis)
            {
                lo[i] = delta > 0 ? (int)MathF.Floor(Get(oldMax, i) - epsilon) + 1 : (int)MathF.Floor(Get(min, i));
                hi[i] = delta > 0 ? (int)MathF.Floor(Get(max, i) - epsilon) : (int)MathF.Floor(Get(oldMin, i) + epsilon) - 1;
            }
            else
            {
                lo[i] = (int)MathF.Floor(Get(min, i) + epsilon);
                hi[i] = (int)MathF.Floor(Get(max, i) - epsilon);
            }
        }

        bool hit = false;
        float limit = Get(moved, axis);
        for (int y = lo[1]; y <= hi[1]; y++)
        {
            for (int z = lo[2]; z <= hi[2]; z++)
            {
                for (int x = lo[0]; x <= hi[0]; x++)
                {
                    if (!Blocks.IsSolid(world.GetBlock(x, y, z))) continue;
                    hit = true;
                    float stop = axis switch
                    {
                        0 => delta > 0 ? x - HalfWidth : x + 1 + HalfWidth,
                        1 => delta > 0 ? y - BodyHeight : y + 1,
                        _ => delta > 0 ? z - HalfWidth : z + 1 + HalfWidth,
                    };
                    limit = delta > 0 ? MathF.Min(limit, stop) : MathF.Max(limit, stop);
                }
            }
        }

        if (axis == 0) position.X = limit;
        else if (axis == 1) position.Y = limit;
        else position.Z = limit;
        return hit;

        static float Get(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;
    }

    private static bool HasGround(World world, Vector3 feet)
    {
        float y = feet.Y - 0.05f;
        for (int i = 0; i < 4; i++)
        {
            float x = feet.X + ((i & 1) == 0 ? -HalfWidth + 0.01f : HalfWidth - 0.01f);
            float z = feet.Z + ((i & 2) == 0 ? -HalfWidth + 0.01f : HalfWidth - 0.01f);
            if (Blocks.IsSolid(world.GetBlock((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z)))) return true;
        }

        return false;
    }

    private static bool IsWater(World world, Vector3 p) => world.GetBlock(BlockPos.Floor(p)) == BlockId.Water;

    private bool Overlaps(BlockPos block)
    {
        Vector3 min = Position - new Vector3(HalfWidth, 0.0f, HalfWidth);
        Vector3 max = Position + new Vector3(HalfWidth, BodyHeight, HalfWidth);
        return max.X > block.X && min.X < block.X + 1 && max.Y > block.Y && min.Y < block.Y + 1 && max.Z > block.Z && min.Z < block.Z + 1;
    }

    // ---- hands ----

    private void Interact(World world)
    {
        _breakCooldown -= Time.DeltaTime;
        _placeCooldown -= Time.DeltaTime;

        bool breakPressed = Input.GetMouseButtonDown(MouseButton.Left);
        bool placePressed = Input.GetMouseButtonDown(MouseButton.Right);
        if (!Input.GetMouseButton(MouseButton.Left)) _breakCooldown = 0.0f;
        if (!Input.GetMouseButton(MouseButton.Right)) _placeCooldown = 0.0f;

        if (Target is not { } hit) return;

        if (Input.GetMouseButtonDown(MouseButton.Middle))
        {
            Pick(hit.Block);
        }

        if ((breakPressed || (Input.GetMouseButton(MouseButton.Left) && _breakCooldown <= 0.0f)) && hit.Block != BlockId.Bedrock)
        {
            Vector3 foliage = FoliageAt(world, hit.Position);
            if (world.SetBlock(hit.Position, BlockId.Air))
            {
                WorldRenderer.Current?.Particles.Burst(hit.Position, hit.Block, foliage);
                Swing = 1.0f;
                _breakCooldown = 0.24f;
                Target = null;
            }

            return;
        }

        if (placePressed || (Input.GetMouseButton(MouseButton.Right) && _placeCooldown <= 0.0f))
        {
            BlockId block = SelectedBlock;
            // Grass and flowers are replaced, like air; anything else gets the block on the face you aimed at.
            BlockPos at = Blocks.Shape(hit.Block) == BlockShape.Plant ? hit.Position : hit.Position + hit.Normal;
            BlockId current = world.GetBlock(at);
            bool replaceable = current is BlockId.Air or BlockId.Water || Blocks.Shape(current) == BlockShape.Plant;
            bool plant = Blocks.Shape(block) == BlockShape.Plant;
            if (!replaceable || (Blocks.IsSolid(block) && Overlaps(at)))
            {
                return;
            }

            if (plant && !Blocks.Supports(world.GetBlock(at.X, at.Y - 1, at.Z)))
            {
                return;
            }

            if (world.SetBlock(at, block))
            {
                Swing = 1.0f;
                _placeCooldown = 0.22f;
            }
        }
    }

    private void Pick(BlockId block)
    {
        int existing = Array.IndexOf(Hotbar, block);
        if (existing >= 0)
        {
            Select(existing);
            return;
        }

        Hotbar[Selected] = block;
        SinceSelect = 0.0f;
    }

    private static Vector3 FoliageAt(World world, BlockPos p)
    {
        Chunk? chunk = world.GetChunk(p.X >> 4, p.Z >> 4);
        return chunk is null ? BlockAtlas.DefaultFoliage : TerrainGenerator.Unpack(chunk.Foliage[(p.Z & 15) * Chunk.Size + (p.X & 15)]);
    }

    // ---- camera ----

    private void SyncTransforms()
    {
        Transform transform = GetComponent<Transform>();
        transform.Position = Position;
        transform.Rotation = new Vector3(0.0f, Yaw, 0.0f);
        if (_camera is null) return;

        float dt = Time.DeltaTime;
        _landDip = MathF.Max(0.0f, _landDip - dt * 2.5f);
        float bob = MathF.Sin(_bob * MathF.PI) * 0.045f * _bobAmount;
        float sway = MathF.Cos(_bob * MathF.PI * 0.5f) * 0.6f * _bobAmount;
        float dip = _landDip * _landDip * 0.18f;
        float eye = EyeHeight - (Sneaking ? 0.12f : 0.0f) + MathF.Abs(bob) - dip;
        _camera.Position = new Vector3(0.0f, eye, 0.0f);
        _camera.Rotation = new Vector3(Pitch, 0.0f, sway * 0.5f);

        if (_cameraComponent is not null)
        {
            float target = FieldOfView * (Sprinting && new Vector2(Velocity.X, Velocity.Z).LengthSquared() > 4.0f ? 1.12f : 1.0f) * (Flying && Sprinting ? 1.06f : 1.0f);
            _fov += (target - _fov) * (1.0f - MathF.Exp(-10.0f * dt));
            if (MathF.Abs(_cameraComponent.FieldOfView - _fov) > 0.01f) _cameraComponent.FieldOfView = _fov;
            float far = (VoxelWorld.Current?.World?.RenderDistance ?? 12) * Chunk.Size + 64.0f;
            if (MathF.Abs(_cameraComponent.FarClip - far) > 1.0f) _cameraComponent.FarClip = far;
        }
    }
}

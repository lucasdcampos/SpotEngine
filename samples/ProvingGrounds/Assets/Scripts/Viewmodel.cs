using System.Numerics;
using Spot.Engine.Assets;
using Spot.Engine.Scenes;

namespace ProvingGrounds;

/// <summary>What drives the weapon model's motion this frame.</summary>
internal struct ViewmodelPose
{
    public WeaponSpec Weapon;

    /// <summary>How far into aiming down sights, 0..1.</summary>
    public float Aim;

    /// <summary>How far the mouse moved this frame, in pixels.</summary>
    public Vector2 MouseDelta;

    public float Speed;
    public bool Grounded;
    public float Crouch;

    /// <summary>Reload progress 0..1, or negative when not reloading.</summary>
    public float Reload;

    /// <summary>How far the weapon is lowered for a swap, 0..1.</summary>
    public float Lowered;

    /// <summary>How close a wall is pushing the weapon back, 0..1.</summary>
    public float Retract;

    /// <summary>The jolt of a landing, 0..1.</summary>
    public float Land;
}

/// <summary>
/// The first-person weapons: both models built from primitive meshes as child entities of a rig under the camera, so
/// the engine draws, lights and shadows them like everything else; and their motion — sway, bob, recoil, reload,
/// swap, aiming and pulling back from walls — composed on the rig every frame.
/// </summary>
internal sealed class Viewmodel
{
    private const string Cube = "builtin:Mesh/Cube";
    private const string Cylinder = "builtin:Mesh/Cylinder";
    private const string Sphere = "builtin:Mesh/Sphere";

    private readonly Scene _scene;
    private readonly TransformComponent _rig;
    private readonly TransformComponent _drum;
    private readonly Material _metal = new() { Color = new Vector4(0.1f, 0.105f, 0.115f, 1.0f), Metallic = 0.6f };
    private readonly Material _polymer = new() { Color = new Vector4(0.17f, 0.18f, 0.2f, 1.0f), Metallic = 0.1f };
    private readonly Material _dark = new() { Color = new Vector4(0.07f, 0.075f, 0.085f, 1.0f), Metallic = 0.4f };
    private readonly Material _cyan = new() { Color = new Vector4(0.1f, 0.2f, 0.25f, 1.0f), EmissiveColor = new Vector3(0.25f, 0.85f, 1.0f), EmissiveIntensity = 2.2f };
    private readonly Material _orange = new() { Color = new Vector4(0.25f, 0.12f, 0.05f, 1.0f), EmissiveColor = new Vector3(1.0f, 0.45f, 0.1f), EmissiveIntensity = 2.2f };
    private readonly Material _red = new() { Color = new Vector4(0.3f, 0.02f, 0.02f, 1.0f), EmissiveColor = new Vector3(1.0f, 0.08f, 0.05f), EmissiveIntensity = 9.0f };

    private Vector3 _swayPosition;
    private Vector3 _swayRotation;
    private float _kick;
    private float _kickPitch;
    private float _kickYaw;
    private float _bobPhase;
    private float _air;
    private float _time;
    private float _drumAngle;
    private float _drumTarget;

    public Viewmodel(Scene scene, Entity camera)
    {
        _scene = scene;
        Entity rig = scene.Instantiate("Weapon Rig");
        rig.SetParent(camera);
        _rig = rig.GetComponent<TransformComponent>();

        Rifle = BuildRifle(rig, out Entity rifleMuzzle);
        RifleMuzzle = rifleMuzzle.GetComponent<TransformComponent>();
        Launcher = BuildLauncher(rig, out Entity launcherMuzzle, out Entity drum);
        LauncherMuzzle = launcherMuzzle.GetComponent<TransformComponent>();
        _drum = drum.GetComponent<TransformComponent>();
        Show(Arsenal.Rifle);
    }

    public Entity Rifle { get; }

    public Entity Launcher { get; }

    public TransformComponent RifleMuzzle { get; }

    public TransformComponent LauncherMuzzle { get; }

    /// <summary>Shows one weapon's model and hides the other.</summary>
    public void Show(WeaponSpec weapon)
    {
        // Entity is a handle: setting Enabled on a copy changes the entity itself.
        Entity rifle = Rifle;
        Entity launcher = Launcher;
        rifle.Enabled = weapon == Arsenal.Rifle;
        launcher.Enabled = weapon == Arsenal.Launcher;
    }

    public TransformComponent Muzzle(WeaponSpec weapon) => weapon == Arsenal.Launcher ? LauncherMuzzle : RifleMuzzle;

    /// <summary>Kicks the model back for a shot.</summary>
    public void Kick(WeaponSpec weapon)
    {
        _kick += weapon.ModelKick;
        _kickPitch += weapon.ModelKickPitch;
        _kickYaw += (Random.Shared.NextSingle() - 0.5f) * weapon.ModelKickPitch * 0.4f;
        if (weapon.Launcher)
        {
            _drumTarget += 90.0f;
        }
    }

    public void Update(float deltaTime, in ViewmodelPose pose)
    {
        _time += deltaTime;
        float aim = pose.Aim;
        float free = 1.0f - aim;

        // Sway: the model lags the view a little when it turns.
        Vector2 mouse = Vector2.Clamp(pose.MouseDelta, new Vector2(-60.0f), new Vector2(60.0f)) * (0.35f + 0.65f * free);
        var swayPosition = new Vector3(-mouse.X * 0.00022f, mouse.Y * 0.00022f, 0.0f);
        var swayRotation = new Vector3(mouse.Y * 0.035f, mouse.X * 0.04f, mouse.X * 0.06f);
        float follow = 1.0f - MathF.Exp(-12.0f * deltaTime);
        _swayPosition = Vector3.Lerp(_swayPosition, swayPosition, follow);
        _swayRotation = Vector3.Lerp(_swayRotation, swayRotation, follow);

        // Bob with the stride, breathe when still.
        float stride = MathF.Min(pose.Speed / 8.0f, 1.0f) * (pose.Grounded ? 1.0f : 0.0f);
        _bobPhase += deltaTime * (4.0f + pose.Speed * 1.1f) * (stride > 0.05f ? 1.0f : 0.0f);
        float bob = stride * (1.0f - 0.85f * aim);
        var bobPosition = new Vector3(MathF.Sin(_bobPhase) * 0.009f * bob, (MathF.Cos(2.0f * _bobPhase) - 1.0f) * 0.0045f * bob, 0.0f);
        float breathe = MathF.Sin(_time * 1.7f) * 0.0016f * free;

        _air += ((pose.Grounded ? 0.0f : 1.0f) - _air) * (1.0f - MathF.Exp(-8.0f * deltaTime));

        // Recoil springs back.
        float recover = MathF.Exp(-16.0f * deltaTime);
        _kick *= recover;
        _kickPitch *= recover;
        _kickYaw *= recover;

        _drumAngle += (_drumTarget - _drumAngle) * (1.0f - MathF.Exp(-22.0f * deltaTime));
        _drum.Rotation = new Vector3(0.0f, 0.0f, _drumAngle);

        Vector3 position = Vector3.Lerp(pose.Weapon.HipOffset, pose.Weapon.AimOffset, aim)
            + _swayPosition + bobPosition + new Vector3(0.0f, breathe + 0.012f * _air * free - 0.03f * pose.Land, _kick * (0.6f + 0.4f * free));
        var rotation = new Vector3(_kickPitch * (0.5f + 0.5f * free) - 4.0f * pose.Land, 2.5f * free + _kickYaw, MathF.Sin(_bobPhase) * 1.3f * bob)
            + _swayRotation;

        // Crouching tucks the weapon in.
        position += new Vector3(-0.012f, 0.004f, 0.0f) * pose.Crouch * free;
        rotation += new Vector3(0.0f, 0.0f, -5.0f) * pose.Crouch * free;

        if (pose.Reload >= 0.0f)
        {
            float p = pose.Reload;
            float dip = MathF.Sin(MathF.Min(p * 1.15f, 1.0f) * MathF.PI);
            float slap = MathF.Exp(-MathF.Pow((p - 0.62f) / 0.05f, 2.0f));
            position += new Vector3(-0.02f * dip, -0.07f * dip + 0.012f * slap, 0.03f * dip);
            rotation += new Vector3(-18.0f * dip, 6.0f * dip, 32.0f * dip);
        }

        float lowered = pose.Lowered * pose.Lowered;
        position += new Vector3(0.0f, -0.3f * lowered, 0.05f * lowered);
        rotation += new Vector3(-45.0f * lowered, 0.0f, 0.0f);

        position += new Vector3(-0.02f, -0.03f, 0.16f) * pose.Retract;
        rotation += new Vector3(28.0f, 10.0f, 0.0f) * pose.Retract;

        _rig.Position = position;
        _rig.Rotation = rotation;
    }

    private Entity BuildRifle(Entity rig, out Entity muzzle)
    {
        Entity root = Node(rig, "VX-9 Pulse Rifle", Vector3.Zero);
        Part(root, "Receiver", Cube, new(0.0f, 0.0f, 0.02f), Vector3.Zero, new(0.052f, 0.07f, 0.3f), _metal);
        Part(root, "Rail", Cube, new(0.0f, 0.041f, 0.0f), Vector3.Zero, new(0.026f, 0.012f, 0.25f), _dark);
        Part(root, "Handguard", Cube, new(0.0f, -0.004f, -0.205f), Vector3.Zero, new(0.062f, 0.062f, 0.2f), _polymer);
        Part(root, "Strip L", Cube, new(-0.0315f, 0.006f, -0.205f), Vector3.Zero, new(0.003f, 0.01f, 0.16f), _cyan);
        Part(root, "Strip R", Cube, new(0.0315f, 0.006f, -0.205f), Vector3.Zero, new(0.003f, 0.01f, 0.16f), _cyan);
        Part(root, "Barrel", Cylinder, new(0.0f, 0.008f, -0.37f), new(90.0f, 0.0f, 0.0f), new(0.022f, 0.07f, 0.022f), _metal);
        Part(root, "Muzzle Brake", Cylinder, new(0.0f, 0.008f, -0.45f), new(90.0f, 0.0f, 0.0f), new(0.034f, 0.025f, 0.034f), _dark);
        Part(root, "Muzzle Ring", Cylinder, new(0.0f, 0.008f, -0.477f), new(90.0f, 0.0f, 0.0f), new(0.026f, 0.003f, 0.026f), _cyan);
        Part(root, "Magazine", Cube, new(0.0f, -0.09f, -0.03f), new(-10.0f, 0.0f, 0.0f), new(0.032f, 0.11f, 0.065f), _polymer);
        Part(root, "Cell Window", Cube, new(0.0165f, -0.085f, -0.03f), new(-10.0f, 0.0f, 0.0f), new(0.002f, 0.07f, 0.022f), _cyan);
        Part(root, "Grip", Cube, new(0.0f, -0.075f, 0.105f), new(15.0f, 0.0f, 0.0f), new(0.034f, 0.09f, 0.042f), _polymer);
        Part(root, "Trigger Guard", Cube, new(0.0f, -0.05f, 0.06f), Vector3.Zero, new(0.01f, 0.006f, 0.05f), _metal);
        Part(root, "Stock", Cube, new(0.0f, -0.012f, 0.2f), Vector3.Zero, new(0.042f, 0.058f, 0.09f), _polymer);
        Part(root, "Butt Pad", Cube, new(0.0f, -0.02f, 0.25f), Vector3.Zero, new(0.046f, 0.08f, 0.016f), _dark);
        Part(root, "Sight Base", Cube, new(0.0f, 0.052f, -0.035f), Vector3.Zero, new(0.036f, 0.01f, 0.045f), _dark);
        Part(root, "Sight Post L", Cube, new(-0.016f, 0.072f, -0.045f), Vector3.Zero, new(0.004f, 0.032f, 0.008f), _dark);
        Part(root, "Sight Post R", Cube, new(0.016f, 0.072f, -0.045f), Vector3.Zero, new(0.004f, 0.032f, 0.008f), _dark);
        Part(root, "Sight Top", Cube, new(0.0f, 0.089f, -0.045f), Vector3.Zero, new(0.036f, 0.004f, 0.008f), _dark);
        Part(root, "Red Dot", Sphere, new(0.0f, 0.071f, -0.045f), Vector3.Zero, new(0.0035f), _red);
        muzzle = Node(root, "Muzzle", new Vector3(0.0f, 0.008f, -0.485f));
        return root;
    }

    private Entity BuildLauncher(Entity rig, out Entity muzzle, out Entity drum)
    {
        Entity root = Node(rig, "HX-2 Pulse Launcher", Vector3.Zero);
        Part(root, "Body", Cube, new(0.0f, 0.0f, 0.04f), Vector3.Zero, new(0.075f, 0.085f, 0.26f), _metal);
        drum = Node(root, "Drum", new Vector3(0.0f, -0.01f, -0.13f));
        Part(drum, "Cylinder", Cylinder, Vector3.Zero, new(90.0f, 0.0f, 0.0f), new(0.11f, 0.06f, 0.11f), _polymer);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * MathF.PI * 0.5f + MathF.PI * 0.25f;
            Part(drum, $"Chamber {i + 1}", Cube, new(MathF.Cos(angle) * 0.05f, MathF.Sin(angle) * 0.05f, 0.0f), new(0.0f, 0.0f, angle * 180.0f / MathF.PI),
                new(0.016f, 0.016f, 0.1f), _orange);
        }

        Part(root, "Barrel", Cylinder, new(0.0f, 0.018f, -0.31f), new(90.0f, 0.0f, 0.0f), new(0.064f, 0.12f, 0.064f), _metal);
        Part(root, "Barrel Ring", Cylinder, new(0.0f, 0.018f, -0.25f), new(90.0f, 0.0f, 0.0f), new(0.07f, 0.008f, 0.07f), _orange);
        Part(root, "Bore Glow", Cylinder, new(0.0f, 0.018f, -0.431f), new(90.0f, 0.0f, 0.0f), new(0.042f, 0.003f, 0.042f), _orange);
        Part(root, "Top Rail", Cube, new(0.0f, 0.06f, -0.06f), Vector3.Zero, new(0.024f, 0.02f, 0.2f), _dark);
        Part(root, "Sight", Cube, new(0.0f, 0.082f, -0.13f), Vector3.Zero, new(0.028f, 0.026f, 0.008f), _dark);
        Part(root, "Sight Dot", Sphere, new(0.0f, 0.095f, -0.134f), Vector3.Zero, new(0.006f), _orange);
        Part(root, "Grip", Cube, new(0.0f, -0.085f, 0.1f), new(15.0f, 0.0f, 0.0f), new(0.04f, 0.1f, 0.045f), _polymer);
        Part(root, "Stock", Cube, new(0.0f, -0.005f, 0.2f), Vector3.Zero, new(0.05f, 0.065f, 0.07f), _polymer);
        Part(root, "Strip L", Cube, new(-0.0385f, 0.01f, 0.04f), Vector3.Zero, new(0.003f, 0.012f, 0.18f), _orange);
        Part(root, "Strip R", Cube, new(0.0385f, 0.01f, 0.04f), Vector3.Zero, new(0.003f, 0.012f, 0.18f), _orange);
        muzzle = Node(root, "Muzzle", new Vector3(0.0f, 0.018f, -0.44f));
        return root;
    }

    private Entity Node(Entity parent, string name, Vector3 position)
    {
        Entity entity = _scene.Instantiate(name);
        entity.SetParent(parent);
        entity.GetComponent<TransformComponent>().Position = position;
        return entity;
    }

    private void Part(Entity parent, string name, string mesh, Vector3 position, Vector3 rotation, Vector3 scale, Material material)
    {
        Entity entity = Node(parent, name, position);
        TransformComponent transform = entity.GetComponent<TransformComponent>();
        transform.Rotation = rotation;
        transform.Scale = scale;
        entity.AddComponent(new MeshComponent { ModelPath = mesh, Material = material });
    }
}

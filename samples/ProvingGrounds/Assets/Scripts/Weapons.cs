using System.Numerics;

namespace ProvingGrounds;

/// <summary>How a weapon handles: its rate, magazine, spread, recoil, and where it sits in view.</summary>
public sealed class WeaponSpec
{
    public required string Name { get; init; }

    public required string Kind { get; init; }

    public bool Automatic { get; init; }

    /// <summary>Seconds between shots.</summary>
    public float FireInterval { get; init; }

    public int Magazine { get; init; }

    public float ReloadTime { get; init; }

    public float Damage { get; init; }

    /// <summary>The push a hit gives a physics prop, in newton-seconds.</summary>
    public float Impulse { get; init; }

    public float Range { get; init; } = 250.0f;

    /// <summary>Spread cone half-angles, in degrees: from the hip, aimed, and the most that firing adds.</summary>
    public float HipSpread { get; init; }

    public float AimSpread { get; init; }

    public float BloomPerShot { get; init; }

    public float MaxBloom { get; init; }

    /// <summary>How fast the added spread settles, in degrees per second.</summary>
    public float BloomRecovery { get; init; }

    /// <summary>Spread added at full running speed, in degrees.</summary>
    public float MoveSpread { get; init; }

    /// <summary>Recoil per shot, in degrees, and how much of it the view drifts back.</summary>
    public float KickPitch { get; init; }

    public float KickYaw { get; init; }

    public float KickRecovery { get; init; }

    /// <summary>The field-of-view multiplier while aiming down sights.</summary>
    public float AimZoom { get; init; } = 1.0f;

    public float AimTime { get; init; } = 0.15f;

    /// <summary>Fires a physical grenade instead of a hitscan bullet.</summary>
    public bool Launcher { get; init; }

    public float ProjectileSpeed { get; init; }

    public Vector4 Accent { get; init; }

    public Vector4 MuzzleColor { get; init; }

    public Vector4 TracerColor { get; init; }

    public float FlashSize { get; init; }

    /// <summary>Where the model sits relative to the camera, from the hip and aiming.</summary>
    public Vector3 HipOffset { get; init; }

    public Vector3 AimOffset { get; init; }

    /// <summary>How hard the model kicks back per shot: distance and pitch (degrees).</summary>
    public float ModelKick { get; init; }

    public float ModelKickPitch { get; init; }
}

/// <summary>The two weapons the player carries.</summary>
public static class Arsenal
{
    public static readonly WeaponSpec Rifle = new()
    {
        Name = "VX-9 Pulse Rifle",
        Kind = "AUTO",
        Automatic = true,
        FireInterval = 0.092f,
        Magazine = 32,
        ReloadTime = 1.55f,
        Damage = 1.0f,
        Impulse = 7.0f,
        HipSpread = 0.85f,
        AimSpread = 0.08f,
        BloomPerShot = 0.38f,
        MaxBloom = 3.2f,
        BloomRecovery = 7.0f,
        MoveSpread = 1.6f,
        KickPitch = 0.5f,
        KickYaw = 0.22f,
        KickRecovery = 0.6f,
        AimZoom = 0.68f,
        AimTime = 0.13f,
        Accent = HudColors.Accent,
        MuzzleColor = new Vector4(3.6f, 5.5f, 7.0f, 1.0f),
        TracerColor = new Vector4(1.6f, 3.8f, 5.0f, 0.9f),
        FlashSize = 0.15f,
        HipOffset = new Vector3(0.19f, -0.185f, -0.43f),
        AimOffset = new Vector3(0.0f, -0.071f, -0.15f),
        ModelKick = 0.035f,
        ModelKickPitch = 2.5f,
    };

    public static readonly WeaponSpec Launcher = new()
    {
        Name = "HX-2 Pulse Launcher",
        Kind = "SEMI",
        Automatic = false,
        FireInterval = 0.7f,
        Magazine = 4,
        ReloadTime = 2.1f,
        Damage = 3.0f,
        Impulse = 0.0f,
        HipSpread = 0.3f,
        AimSpread = 0.0f,
        BloomPerShot = 0.0f,
        MaxBloom = 0.0f,
        BloomRecovery = 1.0f,
        MoveSpread = 0.3f,
        KickPitch = 3.2f,
        KickYaw = 0.6f,
        KickRecovery = 0.75f,
        AimZoom = 0.85f,
        AimTime = 0.18f,
        Launcher = true,
        ProjectileSpeed = 32.0f,
        Accent = HudColors.Warm,
        MuzzleColor = new Vector4(7.0f, 3.6f, 1.2f, 1.0f),
        TracerColor = new Vector4(5.0f, 2.4f, 0.7f, 1.0f),
        FlashSize = 0.26f,
        HipOffset = new Vector3(0.2f, -0.2f, -0.45f),
        AimOffset = new Vector3(0.0f, -0.095f, -0.12f),
        ModelKick = 0.08f,
        ModelKickPitch = 9.0f,
    };
}

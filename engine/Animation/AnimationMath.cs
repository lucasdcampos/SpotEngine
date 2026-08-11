using System.Numerics;

namespace Spot.Animation;

/// <summary>Shared math helpers for animation and model import.</summary>
internal static class AnimationMath
{
    private const float RadToDeg = 180.0f / MathF.PI;

    /// <summary>
    /// Converts a rotation quaternion to Euler angles in degrees using the same yaw(Y)/pitch(X)/roll(Z)
    /// convention <see cref="Spot.Scenes.TransformComponent.LocalMatrix"/> rebuilds them with (via
    /// <see cref="Matrix4x4.CreateFromYawPitchRoll"/>), so a transform written from these angles round-trips.
    /// </summary>
    /// <param name="q">The rotation to convert.</param>
    /// <returns>Euler angles in degrees (X = pitch, Y = yaw, Z = roll).</returns>
    public static Vector3 ToEulerDegrees(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        float x = q.X, y = q.Y, z = q.Z, w = q.W;

        float sinPitch = Math.Clamp(2.0f * (w * x - y * z), -1.0f, 1.0f);
        float pitch = MathF.Asin(sinPitch);

        float yaw, roll;
        if (MathF.Abs(sinPitch) > 0.99999f)
        {
            // Gimbal lock (looking straight up/down): fold the rotation into yaw and zero the roll.
            yaw = MathF.Atan2(-2.0f * (x * z - w * y), 1.0f - 2.0f * (y * y + z * z));
            roll = 0.0f;
        }
        else
        {
            yaw = MathF.Atan2(2.0f * (x * z + w * y), 1.0f - 2.0f * (x * x + y * y));
            roll = MathF.Atan2(2.0f * (x * y + w * z), 1.0f - 2.0f * (x * x + z * z));
        }

        return new Vector3(pitch * RadToDeg, yaw * RadToDeg, roll * RadToDeg);
    }
}

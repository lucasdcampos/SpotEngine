// TODO: Needs rewrite
using OpenTK.Mathematics;
using SpotEngine;

public class InternalCamera
{
    public Vector3 Position { get; private set; }
    public Vector3 Front { get; private set; }
    public Vector3 Up { get; private set; }
    public Vector3 Right => Vector3.Cross(Front, Up);

    private float _yaw;
    private float _pitch;

    public InternalCamera(Vector3 startPosition, float yaw = -90.0f, float pitch = 0.0f)
    {
        Position = startPosition;
        Front = new Vector3(0.0f, 0.0f, -1.0f); // Always facing -Z
        Up = new Vector3(0.0f, 1.0f, 0.0f);
        _yaw = yaw;
        _pitch = pitch;
        UpdateCameraVectors();
    }

    // Update vertices Front, Up and Right based on the rotation angle
    private void UpdateCameraVectors()
    {
        var front = new Vector3
        {
            X = MathF.Cos(MathUtils.DegreesToRadians(_yaw)) * MathF.Cos(MathUtils.DegreesToRadians(_pitch)),
            Y = MathF.Sin(MathUtils.DegreesToRadians(_pitch)),
            Z = MathF.Sin(MathUtils.DegreesToRadians(_yaw)) * MathF.Cos(MathUtils.DegreesToRadians(_pitch))
        };
        Front = Vector3.Normalize(front);
    }

    public Matrix4 GetViewMatrix()
    {
        return Matrix4.LookAt(Position, Position + Front, Up);
    }

    public Matrix4 GetProjectionMatrix(float fov, float aspectRatio, float nearPlane, float farPlane)
    {
        return Matrix4.CreatePerspectiveFieldOfView(MathUtils.DegreesToRadians(fov), aspectRatio, nearPlane, farPlane);
    }

    public void SetPosition(Vector3 newPosition)
    {
        Position = newPosition;
    }

    public void MoveTo(Vector3 targetPosition)
    {
        Position = targetPosition;
    }

    public void SetYaw(float newYaw)
    {
        _yaw = newYaw;
        UpdateCameraVectors();
    }

    public void SetPitch(float newPitch)
    {
        _pitch = newPitch;
        UpdateCameraVectors();
    }

    public void SetRotation(float newYaw, float newPitch)
    {
        _yaw = newYaw;
        _pitch = newPitch;
        UpdateCameraVectors();
    }

    public void Rotate(float yawOffset, float pitchOffset)
    {
        _yaw += yawOffset;
        _pitch += pitchOffset;
        UpdateCameraVectors();
    }
}


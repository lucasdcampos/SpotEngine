// TODO: Rewrite
using OpenTK.Mathematics;
using SpotEngine;

public class InternalCamera
{
    public Vec3 Position { get; set; } = new Vec3(0, 0, 5f);
    public Vec3 Rotation { get; set; } = new Vec3(0, 0, 0);
    public float Fov { get; set; } = 45.0f;
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 100.0f;

    public Matrix4 GetViewMatrix()
    {
        Matrix4 rotationMatrix = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(Rotation.X)) *
                                 Matrix4.CreateRotationY(MathHelper.DegreesToRadians(Rotation.Y)) *
                                 Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(Rotation.Z));

        Matrix4 translationMatrix = Matrix4.CreateTranslation(-Position.X, -Position.Y, -Position.Z);

        return rotationMatrix * translationMatrix;
    }

    public Matrix4 GetProjectionMatrix(float aspectRatio)
    {
        return Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(Fov),
            aspectRatio,
            Near,
            Far
        );
    }

    public void SetPosition(Vec3 position)
    {
        Position = position;
    }

    public void SetRotation(Vec3 rotation)
    {
        Rotation = rotation;
    }
}

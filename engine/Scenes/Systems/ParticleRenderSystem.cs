using System.Numerics;
using Spot.Rendering;

namespace Spot.Scenes;

/// <summary>
/// Draws every entity's <see cref="ParticleSystemComponent"/> as a batch of textured quads, together with
/// its <see cref="TransformComponent"/>. Called by <see cref="RenderSystem"/> after the opaque 3D pass so
/// particles blend over solid geometry. Billboard emitters are turned toward the camera (a shared,
/// screen-aligned basis derived from the view); flat 2D emitters use the world XY plane.
/// </summary>
public static class ParticleRenderSystem
{
    /// <summary>Draws all particle emitters in the scene through the given camera.</summary>
    /// <param name="scene">The scene whose emitters are drawn.</param>
    /// <param name="viewProjection">The view-projection matrix to render with.</param>
    public static void Render(Scene scene, Matrix4x4 viewProjection)
    {
        IReadOnlyList<Entity> emitters = scene.View<TransformComponent, ParticleSystemComponent>();
        if (emitters.Count == 0)
        {
            return;
        }

        // A shared, screen-aligned billboard basis: right/up spanning the plane perpendicular to the view
        // direction, so every billboard faces the camera. Derived from the inverse view-projection (the
        // same trick RenderSystem uses to recover camera facing), it costs one inversion per frame.
        ComputeCameraBasis(viewProjection, out Vector3 camRight, out Vector3 camUp);

        ParticleRenderer.BeginScene(viewProjection);

        foreach (Entity entity in emitters)
        {
            if (!entity.IsActiveInHierarchy()) continue;
            var particles = entity.GetComponent<ParticleSystemComponent>();
            var transform = entity.GetComponent<TransformComponent>();
            if (!particles.Enabled || !transform.Enabled) continue;

            int alive = particles.AliveCount;
            if (alive == 0) continue;

            Particle[] buffer = particles.Buffer;
            Matrix4x4 world = transform.Matrix;
            bool local = particles.Space == ParticleSimulationSpace.Local;

            Vector3 right = particles.RenderMode == ParticleRenderMode.Flat2D ? Vector3.UnitX : camRight;
            Vector3 up = particles.RenderMode == ParticleRenderMode.Flat2D ? Vector3.UnitY : camUp;

            Vector4 startColor = particles.StartColor;
            Vector4 endColor = particles.EndColor;
            float startSize = particles.StartSize;
            float endSize = particles.EndSize;
            Texture2D? texture = particles.Texture;
            ParticleBlend blend = particles.Blend;

            for (int i = 0; i < alive; i++)
            {
                ref Particle p = ref buffer[i];
                float t = p.Lifetime > 0.0f ? p.Age / p.Lifetime : 1.0f;

                Vector3 position = local ? Vector3.Transform(p.Position, world) : p.Position;
                Vector4 color = Vector4.Lerp(startColor, endColor, t);
                float halfSize = (startSize + (endSize - startSize) * t) * p.SizeScale * 0.5f;

                // Spin the billboard basis in its own plane by the particle's rotation.
                float cos = MathF.Cos(p.Rotation);
                float sin = MathF.Sin(p.Rotation);
                Vector3 axisX = (right * cos + up * sin) * halfSize;
                Vector3 axisY = (up * cos - right * sin) * halfSize;

                ParticleRenderer.Submit(position, axisX, axisY, color, texture, blend);
            }
        }

        ParticleRenderer.EndScene();
    }

    private static void ComputeCameraBasis(Matrix4x4 viewProjection, out Vector3 right, out Vector3 up)
    {
        Vector3 forward = new(0.0f, 0.0f, 1.0f);
        if (Matrix4x4.Invert(viewProjection, out Matrix4x4 inv))
        {
            Vector4 near = Vector4.Transform(new Vector4(0.0f, 0.0f, -1.0f, 1.0f), inv);
            Vector4 far = Vector4.Transform(new Vector4(0.0f, 0.0f, 1.0f, 1.0f), inv);
            Vector3 dir = new Vector3(far.X, far.Y, far.Z) / far.W - new Vector3(near.X, near.Y, near.Z) / near.W;
            if (dir.LengthSquared() > 1e-6f)
            {
                forward = Vector3.Normalize(dir);
            }
        }

        // Build an orthonormal right/up perpendicular to the view direction. 
        // We want the quad normal (Cross(right, up)) to point TOWARD the camera (-forward), 
        // otherwise backface culling will discard the particles.
        Vector3 worldUp = MathF.Abs(forward.Y) >= 0.999f ? Vector3.UnitZ : Vector3.UnitY;
        right = Vector3.Cross(forward, worldUp);
        right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.UnitX;
        up = Vector3.Normalize(Vector3.Cross(right, forward));
    }
}

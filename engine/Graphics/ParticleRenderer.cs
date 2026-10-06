using System.Numerics;
using Spot.Engine.Graphics;

namespace Spot.Engine.Graphics;

/// <summary>
/// The engine's particle and world-text pass, on top of the framework's <see cref="BillboardBatch"/>: blended quads
/// given as a center plus two half-axis vectors (camera-facing billboards or flat quads alike), batched by texture
/// and blend mode. It writes no depth but honors the depth test, so solid geometry still occludes particles.
/// </summary>
public static class ParticleRenderer
{
    /// <summary>
    /// Gets the built-in soft round particle texture, used when an emitter has no texture of its own.
    /// </summary>
    public static Texture2D DefaultTexture => BillboardBatch.SoftDotTexture;

    /// <summary>Creates the shared batch resources (they are also created on first use).</summary>
    internal static void Init() => BillboardBatch.Init();

    /// <summary>Releases the shared batch resources.</summary>
    internal static void Shutdown() => BillboardBatch.Shutdown();

    /// <summary>Begins a batch of particles rendered with the given view-projection matrix.</summary>
    /// <param name="viewProjection">The view-projection matrix to use for this batch.</param>
    public static void BeginScene(Matrix4x4 viewProjection) => BillboardBatch.Begin(viewProjection);

    /// <summary>
    /// Submits one particle quad spanning <c>center ± axisX ± axisY</c> — pass camera right/up (already rotated
    /// and half-sized) for billboards, or world axes for flat quads.
    /// </summary>
    /// <param name="center">The quad's center.</param>
    /// <param name="axisX">Half the quad's width, as a direction.</param>
    /// <param name="axisY">Half the quad's height, as a direction.</param>
    /// <param name="color">The particle color, multiplied with the texture.</param>
    /// <param name="texture">The texture, or <see langword="null"/> for <see cref="DefaultTexture"/>.</param>
    /// <param name="blend">How the particle blends.</param>
    public static void Submit(Vector3 center, Vector3 axisX, Vector3 axisY, Vector4 color, Texture2D? texture, ParticleBlend blend) =>
        BillboardBatch.Draw(center, axisX, axisY, color, texture, ToBlendMode(blend));

    /// <summary>
    /// Submits one textured quad sampling an atlas sub-rectangle — the world-text path. <paramref name="uv"/> is
    /// <c>(u0, v0, u1, v1)</c>, with the atlas top (<c>v0</c>) on the <c>+axisY</c> edge.
    /// </summary>
    /// <param name="center">The quad's center.</param>
    /// <param name="axisX">Half the quad's width, as a direction.</param>
    /// <param name="axisY">Half the quad's height, as a direction.</param>
    /// <param name="color">The color, multiplied with the texture.</param>
    /// <param name="texture">The atlas texture.</param>
    /// <param name="blend">How the quad blends.</param>
    /// <param name="uv">The atlas rectangle.</param>
    public static void SubmitTextured(Vector3 center, Vector3 axisX, Vector3 axisY, Vector4 color, Texture2D texture,
        ParticleBlend blend, Vector4 uv) =>
        BillboardBatch.Draw(center, axisX, axisY, color, texture, ToBlendMode(blend), uv);

    /// <summary>Ends the current batch, drawing any particles submitted since <see cref="BeginScene"/>.</summary>
    public static void EndScene() => BillboardBatch.End();

    private static BlendMode ToBlendMode(ParticleBlend blend) =>
        blend == ParticleBlend.Additive ? BlendMode.Additive : BlendMode.Alpha;
}

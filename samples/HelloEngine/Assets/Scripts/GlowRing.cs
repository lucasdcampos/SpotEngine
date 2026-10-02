using System.Numerics;
using Spot.Engine.Scenes;
using Spot.Framework.Graphics;

namespace HelloEngine;

/// <summary>
/// Draws a ring of glowing dots on the ground around its entity — drawing the engine has no component for — with
/// the framework's <see cref="BillboardBatch"/>, from a custom render pass. The pass runs at
/// <see cref="RenderStage.AfterTransparent"/>, inside the HDR capture, so the over-bright dots bloom like the rest
/// of the scene.
/// </summary>
public sealed class GlowRing : EntityBehaviour
{
    /// <summary>Gets or sets the ring's radius.</summary>
    public float Radius { get; set; } = 3.2f;

    /// <summary>Gets or sets how many dots make up the ring.</summary>
    public int Dots { get; set; } = 48;

    /// <summary>Gets or sets the dots' color; components above 1 bloom.</summary>
    public Vector4 Color { get; set; } = new(0.35f, 0.9f, 2.6f, 1.0f);

    private DelegateRenderPass? _pass;
    private float _time;

    public override void OnCreate()
    {
        _pass = new DelegateRenderPass(RenderStage.AfterTransparent, Draw, name: "Glow Ring");
        Scene.AddRenderPass(_pass);
    }

    public override void OnUpdate(float deltaTime) => _time += deltaTime;

    public override void OnDestroy()
    {
        if (_pass is not null)
        {
            Scene.RemoveRenderPass(_pass);
        }
    }

    private void Draw(RenderContext context)
    {
        Vector3 center = GetComponent<TransformComponent>().Position;

        BillboardBatch.Begin(context.ViewProjection);
        for (int i = 0; i < Dots; i++)
        {
            float angle = _time * 0.5f + i * MathF.Tau / Dots;
            float pulse = 0.4f + 0.6f * (0.5f + 0.5f * MathF.Sin(_time * 3.0f - i * 0.4f));
            Vector3 position = center + new Vector3(MathF.Cos(angle) * Radius, 0.02f, MathF.Sin(angle) * Radius);
            float half = 0.1f + 0.12f * pulse;

            // Two ground-plane axes lay the quad flat; the camera's right/up would make it face the viewer instead.
            BillboardBatch.Draw(position, Vector3.UnitZ * half, Vector3.UnitX * half,
                Color * new Vector4(pulse, pulse, pulse, 1.0f), blend: BlendMode.Additive);
        }

        BillboardBatch.End();
    }
}

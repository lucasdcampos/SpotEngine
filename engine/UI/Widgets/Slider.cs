using System.Numerics;
using Spot.Rendering;

namespace Spot.UI;

/// <summary>
/// A horizontal slider: drag the handle to pick a value between <see cref="Min"/> and <see cref="Max"/>.
/// <see cref="OnValueChanged"/> fires whenever the value moves. Dragging keeps tracking the pointer even
/// past the widget's edges, since the root captures the slider on press.
/// </summary>
public class Slider : Widget
{
    /// <summary>The current value, clamped to <see cref="Min"/>..<see cref="Max"/> when drawn.</summary>
    public float Value = 0.5f;

    /// <summary>The minimum value (at the left edge).</summary>
    public float Min = 0f;

    /// <summary>The maximum value (at the right edge).</summary>
    public float Max = 1f;

    /// <summary>The track (unfilled groove) color.</summary>
    public Vector4 TrackColor = new(0.12f, 0.13f, 0.16f, 1f);

    /// <summary>The filled portion color (left of the handle).</summary>
    public Vector4 FillColor = new(0.30f, 0.55f, 0.95f, 1f);

    /// <summary>The draggable handle color.</summary>
    public Vector4 HandleColor = new(0.85f, 0.87f, 0.92f, 1f);

    /// <summary>The handle width in UI units.</summary>
    public float HandleWidth = 14f;

    /// <summary>Raised with the new value whenever it changes.</summary>
    public event Action<float>? OnValueChanged;

    /// <inheritdoc />
    protected override bool IsInteractive => true;

    private float Normalized => Max > Min ? Math.Clamp((Value - Min) / (Max - Min), 0f, 1f) : 0f;

    /// <inheritdoc />
    protected override void OnDraw()
    {
        var pos = new Vector2(ScreenRect.X, ScreenRect.Y);
        var size = new Vector2(ScreenRect.Z, ScreenRect.W);

        float trackHeight = MathF.Min(6f, size.Y);
        var trackPos = new Vector2(pos.X, pos.Y + (size.Y - trackHeight) * 0.5f);
        float t = Normalized;

        UIRenderer.DrawQuad(trackPos, new Vector2(size.X, trackHeight), TrackColor);
        UIRenderer.DrawQuad(trackPos, new Vector2(size.X * t, trackHeight), FillColor);

        float handleX = pos.X + size.X * t - HandleWidth * 0.5f;
        UIRenderer.DrawQuad(new Vector2(handleX, pos.Y), new Vector2(HandleWidth, size.Y), HandleColor);
    }

    /// <inheritdoc />
    protected override void OnInteract(in UIInput input, bool over)
    {
        if (!input.Down || ScreenRect.Z <= 0f) return;

        float t = Math.Clamp((input.Pointer.X - ScreenRect.X) / ScreenRect.Z, 0f, 1f);
        float newValue = Min + t * (Max - Min);
        if (MathF.Abs(newValue - Value) > 1e-6f)
        {
            Value = newValue;
            OnValueChanged?.Invoke(Value);
        }
    }
}

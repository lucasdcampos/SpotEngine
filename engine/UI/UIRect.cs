using System.Numerics;

namespace Spot.UI;

/// <summary>
/// An anchored rectangle describing where a <see cref="Widget"/> sits inside its parent. A point on the
/// parent (<see cref="Anchor"/>, in 0..1) is paired with a point on the widget (<see cref="Pivot"/>, in
/// 0..1); <see cref="Position"/> is the pixel offset between them and <see cref="Size"/> is the widget's
/// pixel size. For example an <see cref="Anchor"/>/<see cref="Pivot"/> of (1,0) with a small negative X
/// pins a widget to the top-right corner regardless of screen size.
/// </summary>
public struct UIRect
{
    /// <summary>The point on the parent rectangle this widget anchors to, in 0..1 (0,0 = top-left).</summary>
    public Vector2 Anchor;

    /// <summary>The point on this widget aligned to the anchor, in 0..1 (0,0 = top-left, 0.5 = center).</summary>
    public Vector2 Pivot;

    /// <summary>The pixel offset from the anchor point to the pivot.</summary>
    public Vector2 Position;

    /// <summary>The widget's size in pixels.</summary>
    public Vector2 Size;

    /// <summary>A default 100x100 rect anchored to its parent's top-left.</summary>
    public static UIRect Default => new()
    {
        Anchor = Vector2.Zero,
        Pivot = Vector2.Zero,
        Position = Vector2.Zero,
        Size = new Vector2(100f, 100f),
    };

    /// <summary>
    /// Resolves this rect to an absolute screen rectangle given the parent's absolute rectangle.
    /// </summary>
    /// <param name="parent">The parent rectangle as <c>(x, y, width, height)</c>.</param>
    /// <returns>This widget's absolute rectangle as <c>(x, y, width, height)</c>.</returns>
    public readonly Vector4 Resolve(Vector4 parent)
    {
        var parentPos = new Vector2(parent.X, parent.Y);
        var parentSize = new Vector2(parent.Z, parent.W);
        Vector2 anchorPoint = parentPos + Anchor * parentSize;
        Vector2 topLeft = anchorPoint + Position - Pivot * Size;
        return new Vector4(topLeft.X, topLeft.Y, Size.X, Size.Y);
    }
}

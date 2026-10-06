using System.Numerics;

namespace Spot.Engine.Graphics;

/// <summary>
/// The colors <see cref="EditorGrid"/> draws with. Every color is RGBA with straight (non-premultiplied) alpha,
/// blended over the scene; lower the alpha to make a part of the grid quieter.
/// </summary>
/// <param name="MinorLineColor">The finest lines on screen, which fade out as they get too dense.</param>
/// <param name="MajorLineColor">Every tenth line, which a minor line grows into as the camera pulls away.</param>
/// <param name="AxisXColor">The X axis through the origin.</param>
/// <param name="AxisYColor">The Y axis: a line of the 2D grid, and the vertical line through the origin in 3D.</param>
/// <param name="AxisZColor">The Z axis through the origin (3D only).</param>
public readonly record struct EditorGridStyle(
    Vector4 MinorLineColor,
    Vector4 MajorLineColor,
    Vector4 AxisXColor,
    Vector4 AxisYColor,
    Vector4 AxisZColor)
{
    /// <summary>
    /// Gets the default style: neutral grey lines that read over both dark and bright backgrounds, and the
    /// conventional red, green and blue axes.
    /// </summary>
    public static EditorGridStyle Default { get; } = new(
        MinorLineColor: new Vector4(0.5f, 0.5f, 0.5f, 0.35f),
        MajorLineColor: new Vector4(0.5f, 0.5f, 0.5f, 0.7f),
        AxisXColor: new Vector4(0.86f, 0.33f, 0.36f, 1.0f),
        AxisYColor: new Vector4(0.45f, 0.75f, 0.38f, 1.0f),
        AxisZColor: new Vector4(0.33f, 0.52f, 0.9f, 1.0f));
}

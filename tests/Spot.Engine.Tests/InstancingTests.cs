using System.Numerics;
using System.Runtime.InteropServices;
using Spot.Rendering;

namespace Spot.Engine.Tests;

public class InstancingTests
{
    // The instanced draw path uploads InstanceData straight to a vertex buffer laid out as a mat4 (locations
    // 3-6) followed by a vec4 color (location 7). If the struct ever gains padding or reorders, that raw
    // upload silently corrupts every instanced mesh — so pin the layout here.
    [Fact]
    public void InstanceData_IsTightlyPackedTwentyFloats()
    {
        Assert.Equal(80, Marshal.SizeOf<Renderer3D.InstanceData>());
    }

    [Fact]
    public void InstanceData_CastToFloats_YieldsMatrixThenColor()
    {
        var instance = new Renderer3D.InstanceData
        {
            Model = Matrix4x4.CreateTranslation(7, 8, 9),
            Color = new Vector4(0.1f, 0.2f, 0.3f, 0.4f),
        };

        Span<Renderer3D.InstanceData> one = [instance];
        ReadOnlySpan<float> floats = MemoryMarshal.Cast<Renderer3D.InstanceData, float>(one);

        Assert.Equal(20, floats.Length);
        // Row-major System.Numerics translation lives in the 4th row (floats 12,13,14), and the color
        // follows the 16 matrix floats.
        Assert.Equal(7f, floats[12]);
        Assert.Equal(8f, floats[13]);
        Assert.Equal(9f, floats[14]);
        Assert.Equal(0.1f, floats[16]);
        Assert.Equal(0.4f, floats[19]);
    }
}

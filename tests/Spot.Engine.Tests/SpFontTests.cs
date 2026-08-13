using Spot.Assets;

namespace Spot.Engine.Tests;

public class SpFontTests
{
    [Fact]
    public void WriteThenRead_RoundTripsNameAndBytes()
    {
        byte[] ttf = { 1, 2, 3, 4, 5, 250, 128, 0, 42 };

        byte[] blob = SpFont.Write("Inter-Regular", ttf);
        SpFontData decoded = SpFont.Read(blob);

        Assert.Equal("Inter-Regular", decoded.Name);
        Assert.Equal(ttf, decoded.Ttf);
    }

    [Fact]
    public void Read_RejectsGarbageWithBadMagic()
    {
        byte[] garbage = { 0, 0, 0, 0, 0, 0, 0, 0 };

        Assert.Throws<InvalidDataException>(() => SpFont.Read(garbage));
    }
}

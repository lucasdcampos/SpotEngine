using System.IO;
using System.Text;
using Spot.Assets;

namespace Spot.Engine.Tests;

public class AssetProviderTests
{
    [Fact]
    public void Current_DefaultsToFileProvider()
    {
        Assert.IsType<FileAssetProvider>(AssetProvider.Current);
    }

    [Fact]
    public void Current_SetNull_RestoresFileProvider()
    {
        IAssetProvider original = AssetProvider.Current;
        try
        {
            AssetProvider.Current = new InMemoryProvider();
            Assert.IsType<InMemoryProvider>(AssetProvider.Current);

            AssetProvider.Current = null!;
            Assert.IsType<FileAssetProvider>(AssetProvider.Current);
        }
        finally
        {
            AssetProvider.Current = original;
        }
    }

    [Fact]
    public void FileProvider_ReadsBytesAndText()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "asset.bin");
        byte[] bytes = { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(path, bytes);

        var provider = new FileAssetProvider();

        Assert.True(provider.Exists(path));
        Assert.Equal(bytes, provider.ReadAllBytes(path));
        Assert.Equal(Encoding.UTF8.GetString(bytes), provider.ReadAllText(path));
    }

    [Fact]
    public void FileProvider_Exists_FalseForMissingFile()
    {
        using var dir = new TempDir();
        var provider = new FileAssetProvider();

        Assert.False(provider.Exists(Path.Combine(dir.Path, "does-not-exist.bin")));
    }

    // A minimal alternative provider, standing in for the browser's fetched content store, to prove the
    // runtime routes reads through whichever provider is installed rather than the filesystem directly.
    private sealed class InMemoryProvider : IAssetProvider
    {
        public bool Exists(string path) => true;

        public byte[] ReadAllBytes(string path) => Array.Empty<byte>();

        public string ReadAllText(string path) => string.Empty;
    }
}

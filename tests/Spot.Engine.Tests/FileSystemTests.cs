using System.IO;
using System.Text;
using Spot.IO;

namespace Spot.Engine.Tests;

public class FileSystemTests
{
    [Fact]
    public void Current_DefaultsToDisk()
    {
        Assert.IsType<DiskFileSystem>(FileSystem.Current);
    }

    [Fact]
    public void Current_SetNull_RestoresDisk()
    {
        IFileSystem original = FileSystem.Current;
        try
        {
            FileSystem.Current = new InMemoryFileSystem();
            Assert.IsType<InMemoryFileSystem>(FileSystem.Current);

            FileSystem.Current = null!;
            Assert.IsType<DiskFileSystem>(FileSystem.Current);
        }
        finally
        {
            FileSystem.Current = original;
        }
    }

    [Fact]
    public void Disk_ReadsBytesAndText()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "asset.bin");
        byte[] bytes = { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(path, bytes);

        var provider = new DiskFileSystem();

        Assert.True(provider.Exists(path));
        Assert.Equal(bytes, provider.ReadAllBytes(path));
        Assert.Equal(Encoding.UTF8.GetString(bytes), provider.ReadAllText(path));
    }

    [Fact]
    public void Disk_Exists_FalseForMissingFile()
    {
        using var dir = new TempDir();
        var provider = new DiskFileSystem();

        Assert.False(provider.Exists(Path.Combine(dir.Path, "does-not-exist.bin")));
    }

    // A minimal alternative provider, standing in for the browser's fetched content store, to prove the
    // runtime routes reads through whichever provider is installed rather than the filesystem directly.
    private sealed class InMemoryFileSystem : IFileSystem
    {
        public bool Exists(string path) => true;

        public byte[] ReadAllBytes(string path) => Array.Empty<byte>();

        public string ReadAllText(string path) => string.Empty;
    }
}

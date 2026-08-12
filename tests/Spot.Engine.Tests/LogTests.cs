using System.IO;
using Spot.Core;
using Xunit;

namespace Spot.Engine.Tests;

public class LogTests
{
    [Fact]
    public void Init_PersistsInformationLogsToRollingFile()
    {
        using var tmp = new TempDir();

        // Point logging at an isolated directory, write a line, then flush/close so the file is readable.
        // The test suite runs serially (see TestBootstrap), so re-pointing the global logger is safe as
        // long as we restore the default afterward for the remaining tests.
        Log.Init(logDirectory: tmp.Path);
        try
        {
            Log.Info("filesink smoke {Answer}", 42);
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Init();
        }

        string[] files = Directory.GetFiles(tmp.Path, "spot*.log");
        Assert.NotEmpty(files);
        Assert.Contains("filesink smoke 42", File.ReadAllText(files[0]));
    }

    [Fact]
    public void Init_WithUnusableLogDirectory_DoesNotThrow()
    {
        using var tmp = new TempDir();

        // A file where the log directory should be makes CreateDirectory fail; logging must degrade to
        // console-only instead of taking the process down (the engine's "never crash" rule).
        string clash = Path.Combine(tmp.Path, "not-a-dir");
        File.WriteAllText(clash, "occupied");

        try
        {
            Log.Init(logDirectory: clash);
            Log.Info("still alive");
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Init();
        }

        Assert.False(Directory.Exists(clash));
    }
}

using System.IO;
using Spot.Engine;
using Spot.Framework;
using Spot.Tests;

namespace Spot.Engine.Tests;

/// <summary>
/// Covers the engine's logging layer: Serilog terminal + rolling-file output and an extra sink, on top of
/// the framework's <see cref="Log"/>.
/// </summary>
public class EngineLoggingTests
{
    [Fact]
    public void EngineLogging_PersistsInformationLogsToRollingFile()
    {
        using var tmp = new TempDir();

        EngineLogging.Init(logDirectory: tmp.Path);
        try
        {
            Log.Info("filesink smoke {Answer}", 42);
            Log.Trace("trace stays out of the file");
        }
        finally
        {
            EngineLogging.CloseAndFlush();
        }

        string[] files = Directory.GetFiles(tmp.Path, "spot*.log");
        Assert.NotEmpty(files);
        string text = File.ReadAllText(files[0]);
        Assert.Contains("APP: filesink smoke 42", text);
        Assert.DoesNotContain("trace stays out", text);
    }

    [Fact]
    public void EngineLogging_WithUnusableLogDirectory_DoesNotThrow()
    {
        using var tmp = new TempDir();

        // A file where the log directory should be makes CreateDirectory fail; logging must degrade to
        // terminal-only instead of taking the process down (the engine's "never crash" rule).
        string clash = Path.Combine(tmp.Path, "not-a-dir");
        File.WriteAllText(clash, "occupied");

        try
        {
            EngineLogging.Init(logDirectory: clash);
            Log.Info("still alive");
        }
        finally
        {
            EngineLogging.CloseAndFlush();
        }

        Assert.False(Directory.Exists(clash));
    }

    [Fact]
    public void EngineLogging_RoutesToTheExtraSinkAndRestoresDefaultsOnClose()
    {
        using var tmp = new TempDir();
        var extra = new ListSink();

        EngineLogging.Init(extra, tmp.Path);
        Log.CoreWarn("to the console");
        Assert.Equal(2, Log.Sinks.Count);

        EngineLogging.CloseAndFlush();

        Assert.Equal("to the console", Assert.Single(extra.Entries).Message);
        Assert.IsType<ConsoleLogSink>(Assert.Single(Log.Sinks));
    }

    private sealed class ListSink : ILogSink
    {
        public List<LogEntry> Entries { get; } = new();

        public void Write(in LogEntry entry) => Entries.Add(entry);
    }
}

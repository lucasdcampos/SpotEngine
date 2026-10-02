using System.Globalization;
using System.IO;
using Spot.Framework;
using Spot.Tests.Fakes;
using Xunit;
using Spot.Tests;

namespace Spot.Framework.Tests;

public class LogTests
{
    [Fact]
    public void Defaults_WriteToTheTerminalWithoutAnySetup()
    {
        Log.ResetToDefault();

        ILogSink sink = Assert.Single(Log.Sinks);
        Assert.IsType<ConsoleLogSink>(sink);
        Assert.Equal(LogLevel.Trace, Log.MinimumLevel);
    }

    [Fact]
    public void CoreAndClientMethods_TagTheirSourceAndLevel()
    {
        using RecordingLogSink sink = RecordingLogSink.Capture();

        Log.CoreTrace("a");
        Log.CoreInfo("b");
        Log.CoreWarn("c");
        Log.CoreError("d");
        Log.Trace("e");
        Log.Info("f");
        Log.Warn("g");
        Log.Error("h");

        Assert.Equal(
            new[]
            {
                (LogLevel.Trace, Log.CoreSource, "a"), (LogLevel.Info, Log.CoreSource, "b"),
                (LogLevel.Warn, Log.CoreSource, "c"), (LogLevel.Error, Log.CoreSource, "d"),
                (LogLevel.Trace, Log.ClientSource, "e"), (LogLevel.Info, Log.ClientSource, "f"),
                (LogLevel.Warn, Log.ClientSource, "g"), (LogLevel.Error, Log.ClientSource, "h"),
            },
            sink.Entries.Select(e => (e.Level, e.Source, e.Message)));
    }

    [Fact]
    public void MinimumLevel_FiltersLowerEntries()
    {
        using RecordingLogSink sink = RecordingLogSink.Capture();
        Log.MinimumLevel = LogLevel.Warn;

        Log.Info("dropped");
        Log.Warn("kept");

        Assert.Equal("kept", Assert.Single(sink.Entries).Message);
    }

    [Fact]
    public void AThrowingSink_NeverReachesTheCallerAndOtherSinksStillReceive()
    {
        using RecordingLogSink sink = RecordingLogSink.Capture();
        Log.AddSink(new ThrowingSink());

        Log.CoreError("survives");

        Assert.True(sink.Contains(LogLevel.Error, "survives"));
    }

    [Fact]
    public void Sinks_AreAddedOnceAndRemovable()
    {
        using RecordingLogSink sink = RecordingLogSink.Capture();
        var extra = new ThrowingSink();

        Log.AddSink(extra);
        Log.AddSink(extra);
        Assert.Equal(2, Log.Sinks.Count);

        Assert.True(Log.RemoveSink(extra));
        Assert.False(Log.RemoveSink(extra));
        Assert.Same(sink, Assert.Single(Log.Sinks));
    }

    [Fact]
    public void ClearSinks_SilencesLogging()
    {
        using RecordingLogSink sink = RecordingLogSink.Capture();
        Log.ClearSinks();

        Log.Error("nowhere");

        Assert.Empty(sink.Entries);
        Assert.Empty(Log.Sinks);
    }

    [Theory]
    [InlineData("plain text", new object[0], "plain text")]
    [InlineData("Font '{0}' at {1}px", new object[] { "Inter", 16 }, "Font 'Inter' at 16px")]
    [InlineData("{1} before {0}", new object[] { "a", "b" }, "b before a")]
    [InlineData("named {Path} then {Count}", new object[] { "x.png", 3 }, "named x.png then 3")]
    [InlineData("{0:F2}", new object[] { 1.5 }, "1.50")]
    [InlineData("[{0,4}]", new object[] { 7 }, "[   7]")]
    [InlineData("[{0,-4}]", new object[] { 7 }, "[7   ]")]
    [InlineData("{@Value}", new object[] { 9 }, "9")]
    [InlineData("{{literal}} {0}", new object[] { 1 }, "{literal} 1")]
    [InlineData("missing {0} {1}", new object[] { "one" }, "missing one {1}")]
    [InlineData("unclosed {0", new object[] { 1 }, "unclosed {0")]
    public void Render_SubstitutesPositionalAndNamedHoles(string template, object[] args, string expected)
    {
        Assert.Equal(expected, Log.Render(template, args));
    }

    [Fact]
    public void Render_NullArgumentRendersAsNull()
    {
        Assert.Equal("value: null", Log.Render("value: {0}", new object?[] { null }));
    }

    [Fact]
    public void Render_UsesInvariantCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            Assert.Equal("1.5", Log.Render("{0}", 1.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Entry_FormatsAsTimestampedLine()
    {
        var entry = new LogEntry(new DateTimeOffset(2026, 1, 2, 13, 4, 5, TimeSpan.Zero), LogLevel.Info, "SPOT", "hi");

        Assert.Equal("[13:04:05] SPOT: hi", entry.ToString());
    }

    private sealed class ThrowingSink : ILogSink
    {
        public void Write(in LogEntry entry) => throw new InvalidOperationException("sink failure");
    }
}

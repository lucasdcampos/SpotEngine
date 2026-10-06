using System.Numerics;
using Spot.Engine.Console;

namespace Spot.Engine.Tests;

public class DevConsoleLogTests
{
    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Info)]
    [InlineData(LogLevel.Warn)]
    [InlineData(LogLevel.Error)]
    public void Sink_PreservesSeverityEvenWhenMessageLooksLikeAnError(LogLevel level)
    {
        var console = new DevConsole();
        console.ClearLines();
        var entry = new LogEntry(DateTimeOffset.Now, level, "APP", "[error] misleading message");

        new DevConsoleSink(console).Write(entry);

        Assert.Equal(level, Assert.Single(console.GetLines()).Level);
        Assert.Equal(1, console.GetLineCount(level));
    }

    [Fact]
    public void Filters_CombineSeveritiesAndCaseInsensitiveSearchWithoutDroppingOutput()
    {
        var console = new DevConsole();
        console.ClearLines();
        console.Print("Texture loaded", Vector4.One, LogLevel.Info);
        console.Print("TEXTURE missing", Vector4.One, LogLevel.Error);
        console.Print("Shader missing", Vector4.One, LogLevel.Error);
        console.Print("Texture fallback", Vector4.One, LogLevel.Warn);

        console.SearchText = "texture";
        console.SetLevelVisible(LogLevel.Info, false);
        console.SetLevelVisible(LogLevel.Warn, false);

        Assert.Equal("TEXTURE missing", Assert.Single(console.GetLines(applyFilters: true)).Text);
        Assert.Equal(4, console.GetLines().Length);
        Assert.Equal(2, console.GetLineCount(LogLevel.Error));
        Assert.Equal(1, console.GetLineCount(LogLevel.Info));
        Assert.Equal(1, console.GetLineCount(LogLevel.Warn));

        console.SetLevelVisible(LogLevel.Warn, true);
        Assert.Equal(2, console.GetLines(applyFilters: true).Length);
        console.SearchText = "no match";
        Assert.Empty(console.GetLines(applyFilters: true));
    }

    [Fact]
    public void Clear_RemovesHiddenEntriesAndCountsButKeepsFiltersAndCommands()
    {
        var console = new DevConsole();
        console.Print("failure", Vector4.One, LogLevel.Error);
        console.SearchText = "failure";
        console.SetLevelVisible(LogLevel.Error, false);

        console.Execute("clear");

        Assert.Empty(console.GetLines());
        Assert.All(Enum.GetValues<LogLevel>(), level => Assert.Equal(0, console.GetLineCount(level)));
        Assert.Equal("failure", console.SearchText);
        Assert.False(console.IsLevelVisible(LogLevel.Error));
        console.Execute("help");
        Assert.NotEmpty(console.GetLines());
    }

    [Fact]
    public void Counts_OnlyIncludeTheLatest500Entries()
    {
        var console = new DevConsole();
        console.ClearLines();
        console.Print("old error", Vector4.One, LogLevel.Error);
        for (int i = 0; i < 500; i++) console.Print($"info {i}", Vector4.One, LogLevel.Info);

        Assert.Equal(500, console.GetLines().Length);
        Assert.Equal(0, console.GetLineCount(LogLevel.Error));
        Assert.Equal(500, console.GetLineCount(LogLevel.Info));
        Assert.Equal("info 0", console.GetLines()[0].Text);
    }

    [Fact]
    public void PlainOutput_ClassifiesCommandErrorsAndKeepsExplicitColors()
    {
        var console = new DevConsole();
        console.ClearLines();
        console.Print("[error] failure", Vector4.One);
        Assert.Equal(LogLevel.Error, console.LastLine!.Value.Level);
        Assert.Equal(Vector4.One, console.LastLine!.Value.Color);

        console.Execute("unknown_command");
        Assert.Equal(LogLevel.Error, console.LastLine!.Value.Level);
        Assert.Equal(2, console.GetLineCount(LogLevel.Error));
        Assert.Equal(1, console.GetLineCount(LogLevel.Info));
    }

    [Fact]
    public void BackgroundWriters_CanAppendWhileOutputIsReadAndCleared()
    {
        var console = new DevConsole();
        Parallel.For(0, 1000, i =>
        {
            console.Print($"message {i}", Vector4.One, LogLevel.Warn);
            Assert.InRange(console.GetLines().Length, 0, 500);
            if (i % 100 == 0) console.ClearLines();
        });
        console.ClearLines();
        console.Print("last", Vector4.One, LogLevel.Error);

        Assert.Equal("last", Assert.Single(console.GetLines()).Text);
        Assert.Equal(0, console.GetLineCount(LogLevel.Warn));
        Assert.Equal(1, console.GetLineCount(LogLevel.Error));
    }
}

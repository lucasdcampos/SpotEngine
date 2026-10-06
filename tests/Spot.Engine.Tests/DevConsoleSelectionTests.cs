using System.Numerics;
using Spot.Engine.Console;

namespace Spot.Engine.Tests;

public class DevConsoleSelectionTests
{
    [Fact]
    public void IndividualAndControlSelection_CopyInLogOrderAndKeepDuplicateMessagesDistinct()
    {
        var console = CreateConsole("same", "middle", "same");
        var lines = console.GetLines();
        var selection = new DevConsole.LogSelection();
        selection.Select(lines[2].Id, lines, additive: false, range: false);
        Assert.Equal("same", selection.BuildText(lines));
        selection.Select(lines[0].Id, lines, additive: true, range: false);
        Assert.Equal(2, selection.Count);
        Assert.Equal("same\nsame", selection.BuildText(lines));
        selection.Select(lines[2].Id, lines, additive: true, range: false);
        Assert.Equal(1, selection.Count);
        Assert.True(selection.Contains(lines[0].Id));
        selection.Select(lines[1].Id, lines, additive: false, range: false);
        Assert.Equal("middle", selection.BuildText(lines));
    }

    [Fact]
    public void ShiftSelection_UsesVisibleRangesAndSupportsAddingToExistingSelection()
    {
        var console = CreateConsole("a", "hidden", "b", "c", "d");
        var visible = console.GetLines().Where(line => line.Text != "hidden").ToArray();
        var selection = new DevConsole.LogSelection();
        selection.Select(visible[2].Id, visible, additive: false, range: false);
        selection.Select(visible[0].Id, visible, additive: false, range: true);
        Assert.Equal("a\nb\nc", selection.BuildText(visible));
        selection.Select(visible[3].Id, visible, additive: false, range: true);
        Assert.Equal("c\nd", selection.BuildText(visible));
        selection.Select(visible[0].Id, visible, additive: true, range: true);
        Assert.Equal("a\nb\nc\nd", selection.BuildText(visible));
    }

    [Fact]
    public void AppendsAndEviction_DoNotSelectAnUnrelatedEntry()
    {
        var console = CreateConsole("old", "keep");
        var selection = new DevConsole.LogSelection();
        var initial = console.GetLines();
        selection.Select(initial[1].Id, initial, additive: false, range: false);
        for (int i = 0; i < 499; i++) console.Print($"new {i}");
        var current = console.GetLines();
        selection.Update(current);
        Assert.Equal("keep", selection.BuildText(current));
        console.Print("next");
        selection.Update(console.GetLines());
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void FilterChanges_PruneHiddenSelectionAndResetHiddenAnchor()
    {
        var console = CreateConsole("a", "b", "c");
        var selection = new DevConsole.LogSelection();
        var all = console.GetLines();
        selection.SelectAll(all);
        console.SearchText = "b";
        var visible = console.GetLines(applyFilters: true);
        selection.Update(visible);
        Assert.Equal("b", selection.BuildText(visible));
        selection.Select(visible[0].Id, visible, additive: false, range: true);
        Assert.Equal("b", selection.BuildText(visible));
        console.SearchText = string.Empty;
        selection.Update(all);
        Assert.Equal("b", selection.BuildText(all));
    }

    [Fact]
    public void ClearAndMultilineMessages_PreserveMessageTextAndNeverReuseSelectionIds()
    {
        var console = CreateConsole("first\nstack trace ## detail", "second");
        var selection = new DevConsole.LogSelection();
        var lines = console.GetLines();
        selection.SelectAll(lines);
        Assert.Equal("first\nstack trace ## detail\nsecond", selection.BuildText(lines));
        selection.Clear();
        Assert.Equal(string.Empty, selection.BuildText(lines));
        selection.SelectAll(lines);
        console.ClearLines();
        console.Print("replacement");
        selection.Update(console.GetLines());
        Assert.Equal(0, selection.Count);
    }

    private static DevConsole CreateConsole(params string[] messages)
    {
        var console = new DevConsole();
        console.ClearLines();
        foreach (string message in messages) console.Print(message, Vector4.One);
        return console;
    }
}

using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Spot.Engine.Console;

namespace Spot.Engine.Tests;

[Collection(nameof(ApplicationInstanceCollection))]
public sealed class DevConsoleRenderingTests : IDisposable
{
    private readonly IntPtr _context;
    private readonly SetClipboardFn _clipboardWriter;
    private string? _copiedText;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetClipboardFn(IntPtr userData, IntPtr text);

    public DevConsoleRenderingTests()
    {
        _context = ImGui.CreateContext();
        ImGuiIOPtr io = ImGui.GetIO();
        unsafe { io.NativePtr->IniFilename = null; }
        _clipboardWriter = (_, text) => _copiedText = Marshal.PtrToStringUTF8(text);
        io.SetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_clipboardWriter);
        io.DisplaySize = new Vector2(1280, 720);
        io.DeltaTime = 1f / 60;
        io.Fonts.AddFontDefault();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _);
        io.Fonts.SetTexID(1);
    }

    [Theory]
    [InlineData(220, ConsolePresentation.Editor)]
    [InlineData(640, ConsolePresentation.Editor)]
    [InlineData(640, ConsolePresentation.Runtime)]
    public void Rendering_LeavesImGuiStacksBalanced(int width, ConsolePresentation presentation)
    {
        var console = new DevConsole { SearchText = "missing" };
        console.Print("missing texture", Vector4.One, LogLevel.Error);
        console.SetLevelVisible(LogLevel.Info, false);
        ImGuiStylePtr style = ImGui.GetStyle();
        Vector2 framePadding = style.FramePadding;
        Vector2 spacing = style.ItemSpacing;
        float childBorder = style.ChildBorderSize;

        Draw(console, width, presentation);

        Assert.Equal(framePadding, style.FramePadding);
        Assert.Equal(spacing, style.ItemSpacing);
        Assert.Equal(childBorder, style.ChildBorderSize);
    }

    [Fact]
    public void ControlF_FocusesSearchWithoutThePromptStealingTyping()
    {
        var console = new DevConsole();
        console.RequestInputFocus();
        Draw(console);
        Draw(console);
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddKeyEvent(ImGuiKey.ModCtrl, true);
        io.AddKeyEvent(ImGuiKey.F, true);
        Draw(console);
        io.AddKeyEvent(ImGuiKey.F, false);
        io.AddKeyEvent(ImGuiKey.ModCtrl, false);
        Draw(console);
        io.AddInputCharactersUTF8("texture");
        Draw(console);
        Draw(console);

        Assert.Equal("texture", console.SearchText);
    }

    [Fact]
    public void ControlL_ClearsOutputWhileSearchKeepsFocus()
    {
        var console = new DevConsole { SearchText = "texture" };
        Draw(console);
        Draw(console);
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddKeyEvent(ImGuiKey.ModCtrl, true);
        io.AddKeyEvent(ImGuiKey.L, true);
        Draw(console);

        Assert.Empty(console.GetLines());
        Assert.Equal("texture", console.SearchText);
    }

    [Fact]
    public void FocusedOutput_SelectsAndCopiesVisibleLogsWithoutThePromptReclaimingFocus()
    {
        var console = new DevConsole();
        console.ClearLines();
        console.Print("hidden info", Vector4.One, LogLevel.Info);
        console.Print("error\nstack ## trace", Vector4.One, LogLevel.Error);
        console.Print("second error", Vector4.One, LogLevel.Error);
        console.SetLevelVisible(LogLevel.Info, false);
        console.RequestInputFocus();
        Draw(console);
        Draw(console);
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddMousePosEvent(80, 260);
        io.AddMouseButtonEvent(0, true);
        Draw(console);
        io.AddMouseButtonEvent(0, false);
        Draw(console);
        PressControlKey(console, ImGuiKey.A);
        PressControlKey(console, ImGuiKey.C);

        Assert.Equal("error\nstack ## trace\nsecond error", _copiedText);
    }

    [Fact]
    public void FocusedPrompt_ControlCStillCopiesCommandText()
    {
        var console = new DevConsole();
        console.RequestInputFocus();
        Draw(console);
        Draw(console);
        ImGui.GetIO().AddInputCharactersUTF8("command text");
        Draw(console);
        PressControlKey(console, ImGuiKey.A);
        PressControlKey(console, ImGuiKey.C);

        Assert.Equal("command text", _copiedText);
    }

    [Fact]
    public void ClickAndControlClick_CopyIndividualAndMultipleRows()
    {
        var console = new DevConsole();
        console.ClearLines();
        console.Print("first");
        console.Print("middle");
        console.Print("last");
        Draw(console, presentation: ConsolePresentation.Runtime);
        Draw(console, presentation: ConsolePresentation.Runtime);
        ImGuiIOPtr io = ImGui.GetIO();
        // Runtime output starts below the title, outer/child padding, and the small leading spacer.
        float firstRowY = ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y * 2
            + 2 + 3 + ImGui.GetTextLineHeight() * 0.5f;
        ClickOutput(console, new Vector2(80, firstRowY), ConsolePresentation.Runtime);
        PressControlKey(console, ImGuiKey.C, ConsolePresentation.Runtime);
        Assert.Equal("first", _copiedText);

        io.AddKeyEvent(ImGuiKey.ModCtrl, true);
        ClickOutput(console, new Vector2(80, firstRowY + (ImGui.GetTextLineHeight() + 3) * 2),
            ConsolePresentation.Runtime);
        io.AddKeyEvent(ImGuiKey.ModCtrl, false);
        Draw(console, presentation: ConsolePresentation.Runtime);
        PressControlKey(console, ImGuiKey.C, ConsolePresentation.Runtime);
        Assert.Equal("first\nlast", _copiedText);
    }

    [Fact]
    public void CopyAllContextAction_IncludesHiddenLogsWithoutRequiringSelection()
    {
        var console = new DevConsole();
        console.ClearLines();
        console.Print("hidden info", Vector4.One, LogLevel.Info);
        console.Print("visible error", Vector4.One, LogLevel.Error);
        console.SetLevelVisible(LogLevel.Info, false);
        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        Draw(console);
        Draw(console);
        io.AddMousePosEvent(80, 260);
        io.AddMouseButtonEvent(1, true);
        Draw(console);
        io.AddMouseButtonEvent(1, false);
        Draw(console);
        // Enter keyboard navigation, then move from Copy filtered to Copy all.
        io.AddKeyEvent(ImGuiKey.DownArrow, true);
        Draw(console);
        io.AddKeyEvent(ImGuiKey.DownArrow, false);
        Draw(console);
        io.AddKeyEvent(ImGuiKey.UpArrow, true);
        Draw(console);
        io.AddKeyEvent(ImGuiKey.UpArrow, false);
        Draw(console);
        io.AddKeyEvent(ImGuiKey.Enter, true);
        Draw(console);
        io.AddKeyEvent(ImGuiKey.Enter, false);
        Draw(console);

        Assert.Equal("hidden info\nvisible error\n", _copiedText);
    }

    private static void ClickOutput(DevConsole console, Vector2 position, ConsolePresentation presentation)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddMousePosEvent(position.X, position.Y);
        io.AddMouseButtonEvent(0, true);
        Draw(console, presentation: presentation);
        io.AddMouseButtonEvent(0, false);
        Draw(console, presentation: presentation);
    }

    private static void PressControlKey(DevConsole console, ImGuiKey key,
        ConsolePresentation presentation = ConsolePresentation.Editor)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.AddKeyEvent(ImGuiKey.ModCtrl, true);
        io.AddKeyEvent(key, true);
        Draw(console, presentation: presentation);
        io.AddKeyEvent(key, false);
        io.AddKeyEvent(ImGuiKey.ModCtrl, false);
        Draw(console, presentation: presentation);
    }

    private static void Draw(DevConsole console, int width = 640,
        ConsolePresentation presentation = ConsolePresentation.Editor)
    {
        ImGui.NewFrame();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(width, 400));
        ImGui.Begin("Console");
        uint id = ImGui.GetID("probe");
        console.DrawContents(presentation);
        Assert.Equal(id, ImGui.GetID("probe"));
        ImGui.End();
        ImGui.Render();
    }

    public void Dispose()
    {
        ImGui.DestroyContext(_context);
        GC.KeepAlive(_clipboardWriter);
    }
}

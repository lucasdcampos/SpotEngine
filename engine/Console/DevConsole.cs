using System.Numerics;
using System.Text;
using ImGuiNET;
using Spot.Engine;

namespace Spot.Engine.Console;

/// <summary>
/// Executes a registered console command.
/// </summary>
/// <param name="args">The command arguments.</param>
public delegate void CommandFn(IReadOnlyList<string> args);

/// <summary>
/// Stores a command handler together with its help text.
/// </summary>
public readonly struct CommandInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandInfo"/> struct.
    /// </summary>
    /// <param name="fn">The command handler.</param>
    /// <param name="help">The command help text.</param>
    public CommandInfo(CommandFn fn, string help)
    {
        Fn = fn;
        Help = help;
    }

    /// <summary>
    /// Gets the command handler.
    /// </summary>
    public CommandFn Fn { get; }

    /// <summary>
    /// Gets the command help text.
    /// </summary>
    public string Help { get; }
}

/// <summary>
/// Selects how <see cref="DevConsole.DrawContents"/> styles itself.
/// </summary>
public enum ConsolePresentation
{
    /// <summary>
    /// Standalone runtime console: an opaque, Source-engine inspired command line that reads clearly
    /// when drawn on top of a live game, regardless of the game's own colors.
    /// </summary>
    Runtime,

    /// <summary>
    /// Docked editor panel: inherits the active editor theme (surfaces, borders, frames) so the console
    /// reads as a native part of the editor rather than a separate overlay.
    /// </summary>
    Editor,
}

/// <summary>
/// An in-game developer console rendered with ImGui.
/// </summary>
public sealed class DevConsole
{
    private const int MaxLines = 500;
    private static readonly LogLevel[] Levels = Enum.GetValues<LogLevel>();

    /// <summary>
    /// Gets or sets the color used for regular console output. Exposed so the editor's theming can
    /// override it; the engine keeps a sensible default so the console works standalone.
    /// </summary>
    public static Vector4 DefaultTextColor { get; set; } = new(0.9f, 0.9f, 0.9f, 1.0f);

    /// <summary>Gets or sets the color used to echo entered commands.</summary>
    public static Vector4 CommandColor { get; set; } = new(0.85f, 0.85f, 0.2f, 1.0f);

    /// <summary>Gets or sets the color used for error output.</summary>
    public static Vector4 ErrorColor { get; set; } = new(1.0f, 0.35f, 0.35f, 1.0f);

    /// <summary>
    /// Gets or sets an optional monospaced font for the console body and input. Exposed so the editor
    /// can hand the console a mono face from its atlas (the engine keeps this null, using the default
    /// font, so the console still works standalone).
    /// </summary>
    public static ImFontPtr? MonospaceFont { get; set; }

    /// <summary>
    /// Gets or sets the severity icons supplied by the host's UI font. Defaults to simple symbols
    /// supported by the default font; the editor supplies its Font Awesome glyphs.
    /// </summary>
    public static IReadOnlyDictionary<LogLevel, string> LevelIcons { get; set; } = new Dictionary<LogLevel, string>
    {
        [LogLevel.Trace] = "...",
        [LogLevel.Info] = "i",
        [LogLevel.Warn] = "!",
        [LogLevel.Error] = "x",
    };

    private readonly Dictionary<string, CommandInfo> _commands = new();
    private readonly List<ConsoleLine> _lines = new();

    // Log lines can arrive from background threads (for example a build process piping its output
    // through the logger), so every access to _lines is guarded. Rendering copies into this scratch
    // buffer under the lock and then draws from it, keeping the lock off the ImGui calls.
    private readonly object _linesLock = new();
    private readonly List<ConsoleLine> _renderBuffer = new();
    private readonly List<ConsoleLine> _visibleLines = new();
    private readonly LogSelection _selection = new();
    private long _nextLineId;
    private bool _outputTakesFocus;
    private readonly HashSet<LogLevel> _visibleLevels = new(Levels);
    private string _searchText = string.Empty;
    private bool _focusSearch;
    private bool _searchTakesFocus;

    private readonly List<string> _history = new();
    private readonly byte[] _inputBuf = new byte[256];
    private readonly ImGuiInputTextCallback _textEditCallback;

    private int _historyPos = -1;
    private bool _open;
    private bool _scrollToBottom;
    private bool _justOpened;

    // Set when the input lost keyboard focus for a non-deliberate reason (e.g. selecting all and
    // deleting the text, which ImGui otherwise leaves unfocused). Reasserts focus on the next frame so
    // the console input stays "hot" like a real terminal.
    private bool _reclaimFocus;

    // Set by a host that draws the console itself through DrawContents — the editor's docked Console
    // panel. Null in a standalone game, where the engine owns the floating console window.
    private Action? _hostFocusRequest;

    /// <summary>
    /// Initializes a new instance of the <see cref="DevConsole"/> class.
    /// </summary>
    public unsafe DevConsole()
    {
        _textEditCallback = HandleTextEdit;
        RegisterBuiltins();
        Print("SpotEngine Developer Console");
        Print("Type 'help' for available commands.");
    }

    /// <summary>
    /// Gets a value indicating whether the console is open.
    /// </summary>
    public bool IsOpen => _open;

    /// <summary>
    /// Gets whether the console's window belongs to the host application rather than the engine.
    /// </summary>
    /// <remarks>
    /// The editor docks the console as a native panel, drawing the body itself via
    /// <see cref="DrawContents"/>. The engine must then not draw its own floating window: ImGui merges
    /// windows that share a name, so a second "Console" would append into the panel and draw the body —
    /// command input included — twice. While hosted the console also never reports
    /// <see cref="IsOpen"/>, so it never takes engine-level input capture: the host already decides who
    /// owns input from its own panel focus.
    /// </remarks>
    public bool IsHosted => _hostFocusRequest is not null;

    /// <summary>Gets or sets the editor's case-insensitive search over console messages.</summary>
    public string SearchText
    {
        get => _searchText;
        set => _searchText = value ?? string.Empty;
    }

    /// <summary>Gets whether a severity is included in the editor's output.</summary>
    public bool IsLevelVisible(LogLevel level) => _visibleLevels.Contains(level);

    /// <summary>Includes or excludes a severity without discarding its messages.</summary>
    public void SetLevelVisible(LogLevel level, bool visible)
    {
        if (visible) _visibleLevels.Add(level);
        else _visibleLevels.Remove(level);
    }

    /// <summary>Returns a thread-safe snapshot of retained lines, optionally applying the editor filters.</summary>
    public ConsoleLine[] GetLines(bool applyFilters = false)
    {
        lock (_linesLock)
        {
            return applyFilters ? _lines.Where(MatchesFilters).ToArray() : _lines.ToArray();
        }
    }

    /// <summary>Counts retained entries of a severity, regardless of the editor filters.</summary>
    public int GetLineCount(LogLevel level)
    {
        lock (_linesLock)
        {
            return _lines.Count(line => line.Level == level);
        }
    }

    private bool MatchesFilters(ConsoleLine line) => IsLevelVisible(line.Level)
        && line.Text.Contains(_searchText, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the most recently printed line, if any.</summary>
    public ConsoleLine? LastLine
    {
        get
        {
            lock (_linesLock)
            {
                return _lines.Count > 0 ? _lines[^1] : null;
            }
        }
    }

    /// <summary>
    /// Hands ownership of the console's window to the host application (see <see cref="IsHosted"/>).
    /// </summary>
    /// <param name="focusRequest">
    /// Called when something asks for the console (the <c>'</c> key); the host should reveal its console
    /// panel and give it keyboard focus. Pass <see langword="null"/> to return ownership to the engine.
    /// </param>
    public void SetHost(Action? focusRequest)
    {
        _hostFocusRequest = focusRequest;
        if (focusRequest is not null)
        {
            // The engine's own window is going away; don't leave the open flag (and the input capture
            // the application derives from it) latched on.
            _open = false;
        }
    }

    /// <summary>
    /// Asks the console's command input to take keyboard focus on the next frame it is drawn. Used by a
    /// host that owns the window, so focusing its panel also puts the caret in the prompt.
    /// </summary>
    public void RequestInputFocus()
    {
        _justOpened = true;
        _scrollToBottom = true;
    }

    /// <summary>
    /// Toggles the visibility of the console. While a host owns the window (see <see cref="IsHosted"/>)
    /// there is nothing to toggle, so this reveals and focuses the host's panel instead.
    /// </summary>
    public void Toggle()
    {
        if (_hostFocusRequest is { } focusRequest)
        {
            RequestInputFocus();
            focusRequest();
            return;
        }

        _open = !_open;
        if (_open)
        {
            RequestInputFocus();
        }
    }

    /// <summary>
    /// Registers a command with the console.
    /// </summary>
    /// <param name="name">The command name.</param>
    /// <param name="fn">The command handler.</param>
    /// <param name="help">The command help text.</param>
    public void Register(string name, CommandFn fn, string help = "")
    {
        _commands[name.ToLowerInvariant()] = new CommandInfo(fn, help);
    }

    /// <summary>
    /// Appends a line of text to the console output, choosing a color from its content.
    /// </summary>
    /// <param name="text">The text to append.</param>
    public void Print(string text) => Print(text, ColorFor(text));

    /// <summary>
    /// Appends a line of text to the console output with an explicit color.
    /// </summary>
    /// <param name="text">The text to append.</param>
    /// <param name="color">The color to render the line with.</param>
    public void Print(string text, Vector4 color) =>
        Print(text, color, text.StartsWith("[error]", StringComparison.Ordinal) ? LogLevel.Error : LogLevel.Info);

    /// <summary>Appends a line with its original severity and an explicit display color.</summary>
    public void Print(string text, Vector4 color, LogLevel level)
    {
        lock (_linesLock)
        {
            _lines.Add(new ConsoleLine(text, color, level, ++_nextLineId));
            if (_lines.Count > MaxLines)
            {
                _lines.RemoveRange(0, _lines.Count - MaxLines);
            }
        }

        // Note: we intentionally do not force a scroll-to-bottom here. DrawContents keeps the view pinned to
        // the bottom while the user is already there, but leaves it alone once they scroll up to read history.
    }

    private static Vector4 ColorFor(string text)
    {
        if (text.Length >= 2 && text[0] == ']' && text[1] == ' ')
        {
            return CommandColor;
        }

        if (text.StartsWith("[error]", StringComparison.Ordinal))
        {
            return ErrorColor;
        }

        return DefaultTextColor;
    }

    /// <summary>
    /// Parses and executes a command line.
    /// </summary>
    /// <param name="line">The command line to execute.</param>
    public void Execute(string line)
    {
        if (line.Length == 0)
        {
            return;
        }

        Print("] " + line);

        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return;
        }

        string cmd = tokens[0].ToLowerInvariant();
        string[] args = tokens[1..];

        if (!_commands.TryGetValue(cmd, out CommandInfo info))
        {
            Print($"[error] Unknown command: '{cmd}'  (type 'help' for list)");
            return;
        }

        info.Fn(args);
    }

    public void OnImGuiRender()
    {
        // Nothing to draw when the host owns the window: it calls DrawContents from its own panel, and a
        // same-named window here would merge into that panel and duplicate the body.
        if (!_open || IsHosted)
        {
            return;
        }

        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(new Vector2(viewport.Size.X * 0.6f, viewport.Size.Y * 0.45f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X + 20.0f, viewport.Pos.Y + 20.0f), ImGuiCond.FirstUseEver);

        // Modern, Source-engine inspired styling: deep dark gray/brownish background, sharp edges
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.12f, 0.13f, 0.14f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.08f, 0.09f, 0.10f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.18f, 0.20f, 0.22f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.25f, 0.27f, 0.30f, 1.0f));
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 2.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8.0f, 8.0f));

        if (!ImGui.Begin("Console", ref _open, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(4);
            return;
        }

        DrawContents(ConsolePresentation.Runtime);

        ImGui.End();

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
    }

    /// <summary>
    /// Renders the inner contents of the console (logs and input).
    /// </summary>
    /// <param name="presentation">
    /// Which visual skin to use. <see cref="ConsolePresentation.Runtime"/> keeps the standalone
    /// Source-style command line; <see cref="ConsolePresentation.Editor"/> inherits the editor theme.
    /// </param>
    public void DrawContents(ConsolePresentation presentation = ConsolePresentation.Runtime)
    {
        bool editor = presentation == ConsolePresentation.Editor;

        // The runtime console overlays a live game, so it uses a roomier, Source-style command line
        // (pushed for the whole body so the footer height and prompt share the padding). The editor
        // panel instead keeps the active theme's frame metrics so it reads as a native dockable panel.
        if (!editor)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8.0f, 7.0f));
        }

        bool consoleFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

        // Use one snapshot for the toolbar counts and output, even when a build logs on another thread.
        lock (_linesLock)
        {
            _renderBuffer.Clear();
            _renderBuffer.AddRange(_lines);
        }

        if (editor)
        {
            // Toolbar icons are merged into the host's body font, not the console's monospaced font.
            DrawEditorToolbar(consoleFocused);
        }

        // Keep the output and command input monospaced while the toolbar uses the UI's body font.
        bool pushedFont = MonospaceFont.HasValue;
        if (pushedFont)
        {
            ImGui.PushFont(MonospaceFont!.Value);
        }

        float footerHeight = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing() + 4.0f;

        DrawOutput(new Vector2(0.0f, -footerHeight), editor);

        // Subtly spaced from the output box
        ImGui.Dummy(new Vector2(0, 2.0f));

        DrawInputRow(consoleFocused, editor);

        if (!editor)
        {
            ImGui.PopStyleVar();
        }

        if (pushedFont)
        {
            ImGui.PopFont();
        }
    }

    private void DrawEditorToolbar(bool consoleFocused)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (consoleFocused && io.KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.F, false))
            {
                _focusSearch = true;
                _justOpened = false;
                _reclaimFocus = false;
            }

            if (ImGui.IsKeyPressed(ImGuiKey.L, false))
            {
                ClearLines();
                _renderBuffer.Clear();
            }
        }

        float rightEdge = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.Button("Clear"))
        {
            ClearLines();
            _renderBuffer.Clear();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Clear all console output (Ctrl+L)");

        foreach (LogLevel level in Levels)
        {
            int count = _renderBuffer.Count(line => line.Level == level);
            string name = level == LogLevel.Warn ? "Warning" : level.ToString();
            string icon = LevelIcons.TryGetValue(level, out string? glyph) ? glyph : "?";
            // Reserve the maximum retained count so incoming logs don't move the click targets.
            float width = Math.Max(ImGui.GetFrameHeight(),
                ImGui.CalcTextSize($"{icon}  {MaxLines}").X + ImGui.GetStyle().FramePadding.X * 2);
            if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= rightEdge)
            {
                ImGui.SameLine();
            }

            bool visible = IsLevelVisible(level);
            Vector4 tint = level switch
            {
                LogLevel.Info => ImGui.GetStyle().Colors[(int)ImGuiCol.NavHighlight],
                LogLevel.Warn => CommandColor,
                LogLevel.Error => ErrorColor,
                _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
            };
            Vector4 background = ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
            ImGui.PushStyleColor(ImGuiCol.Button, visible ? Vector4.Lerp(background, tint, 0.24f) : background);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Lerp(background, tint, visible ? 0.38f : 0.14f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Lerp(background, tint, 0.48f));
            ImGui.PushStyleColor(ImGuiCol.Border, visible
                ? Vector4.Lerp(background, tint, 0.65f) : ImGui.GetStyle().Colors[(int)ImGuiCol.Border]);
            ImGui.PushStyleColor(ImGuiCol.Text, visible ? tint : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            if (ImGui.Button($"{icon}  {count}###console_level_{level}", new Vector2(width, 0)))
            {
                SetLevelVisible(level, !visible);
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(5);
            if (ImGui.IsItemHovered())
            {
                bool enabled = IsLevelVisible(level);
                ImGui.SetTooltip($"{name}: {count} entries | {(enabled ? "Shown" : "Hidden")}\n"
                    + $"Click to {(enabled ? "hide" : "show")}. Count includes hidden entries.");
            }
        }

        // Fill the rest of the toolbar with search; wrap only when a narrow dock cannot fit a usable field.
        float searchLeft = ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X;
        if (rightEdge - searchLeft >= 120) ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(1, ImGui.GetContentRegionAvail().X));
        _searchTakesFocus = _focusSearch;
        if (_focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            _focusSearch = false;
        }
        ImGui.InputTextWithHint("##console_search", "Search messages...", ref _searchText, 512,
            ImGuiInputTextFlags.EscapeClearsAll);
        _searchTakesFocus |= ImGui.IsItemActive();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Search messages (Ctrl+F). Matches without case sensitivity. Escape clears the search.");
    }

    // Each entry is one selectable row, including messages with multiple lines or literal ImGui markers.
    private void DrawOutput(Vector2 size, bool editor)
    {
        // Editor: derive the inset frame from the active theme (ChildBg + a hairline Border) so it
        // matches every other panel. Runtime: the fixed dark Source-style box.
        int pushedColors;
        if (editor)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1.0f);
            pushedColors = 0;
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.06f, 0.07f, 0.08f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.20f, 0.22f, 0.25f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 2.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1.0f);
            pushedColors = 2;
        }

        ImGui.BeginChild("##output", size, ImGuiChildFlags.Border, ImGuiWindowFlags.HorizontalScrollbar);

        _visibleLines.Clear();
        _visibleLines.AddRange(editor ? _renderBuffer.Where(MatchesFilters) : _renderBuffer);
        _selection.Update(_visibleLines);
        _outputTakesFocus = ImGui.IsWindowFocused();

        // Was the view pinned to the bottom coming into this frame? Checked before drawing this frame's
        // content (so GetScrollMaxY still reflects last frame's height) so a new log line keeps the console
        // stuck to the bottom only when the user was already there — never yanking them off history they
        // scrolled up to read.
        bool stickToBottom = ImGui.GetScrollMaxY() <= 0f || ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1.0f;

        // A touch more vertical spacing between log lines keeps a busy console legible.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 3.0f));

        ImGui.Dummy(new Vector2(0, 2.0f));
        ImGui.Indent(6.0f);

        foreach (ConsoleLine line in _visibleLines)
        {
            Vector2 position = ImGui.GetCursorScreenPos();
            Vector2 textSize = ImGui.CalcTextSize(line.Text);
            Vector2 rowSize = new(Math.Max(ImGui.GetContentRegionAvail().X, textSize.X),
                Math.Max(ImGui.GetTextLineHeight(), textSize.Y));
            bool clicked = ImGui.Selectable($"##log_{line.Id}", _selection.Contains(line.Id),
                ImGuiSelectableFlags.None, rowSize);
            bool rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
            if (clicked || rightClicked)
            {
                ImGuiIOPtr io = ImGui.GetIO();
                if (clicked || !_selection.Contains(line.Id))
                    _selection.Select(line.Id, _visibleLines, clicked && io.KeyCtrl, clicked && io.KeyShift);
                FocusOutput();
            }

            // Draw separately so ## in a message remains literal and a multiline entry shares one hit target.
            ImGui.GetWindowDrawList().AddText(position, ImGui.GetColorU32(line.Color), line.Text);
        }

        if (_renderBuffer.Count == 0)
        {
            ImGui.TextDisabled("No logs yet.");
        }
        else if (_visibleLines.Count == 0)
        {
            ImGui.TextDisabled("No logs match the selected types and search.");
        }

        ImGui.Unindent(6.0f);
        ImGui.Dummy(new Vector2(0, 2.0f));

        ImGui.PopStyleVar();

        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered())
        {
            if (!ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift) _selection.Clear();
            FocusOutput();
        }

        bool popupOpen = ImGui.IsPopupOpen(string.Empty, ImGuiPopupFlags.AnyPopup);
        if (ImGui.IsWindowFocused() && !ImGui.IsAnyItemActive() && !popupOpen)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C, false) && _selection.Count > 0)
                ImGui.SetClipboardText(_selection.BuildText(_visibleLines));
            if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.A, false)) _selection.SelectAll(_visibleLines);
            if (ImGui.IsKeyPressed(ImGuiKey.Escape, false)) _selection.Clear();
        }

        if (ImGui.BeginPopupContextWindow("##output_ctx"))
        {
            if (ImGui.MenuItem("Copy selected", "Ctrl+C", false, _selection.Count > 0))
            {
                ImGui.SetClipboardText(_selection.BuildText(_visibleLines));
            }

            if (ImGui.MenuItem("Copy all"))
            {
                ImGui.SetClipboardText(BuildLogText());
            }

            if (editor && ImGui.MenuItem("Copy filtered"))
            {
                ImGui.SetClipboardText(string.Join('\n', _visibleLines.Select(line => line.Text)));
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Select all visible", "Ctrl+A", false, _visibleLines.Count > 0))
                _selection.SelectAll(_visibleLines);
            if (ImGui.MenuItem("Clear selection", "Esc", false, _selection.Count > 0)) _selection.Clear();

            if (ImGui.MenuItem("Clear"))
            {
                ClearLines();
            }

            ImGui.EndPopup();
        }

        if (_scrollToBottom || stickToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
        }
        _scrollToBottom = false;

        ImGui.EndChild();

        ImGui.PopStyleVar(editor ? 1 : 2);
        if (pushedColors > 0)
        {
            ImGui.PopStyleColor(pushedColors);
        }
    }

    private void FocusOutput()
    {
        ImGui.SetWindowFocus();
        _outputTakesFocus = true;
        _justOpened = false;
        _reclaimFocus = false;
    }

    // Draws the "] input" command line and keeps it focused while the console is open. The runtime skin
    // boxes the input and adds a Submit button; the editor skin is a clean, theme-driven line where
    // Enter submits.
    private void DrawInputRow(bool consoleFocused, bool editor)
    {
        const ImGuiInputTextFlags inputFlags = ImGuiInputTextFlags.EnterReturnsTrue
                                             | ImGuiInputTextFlags.EscapeClearsAll
                                             | ImGuiInputTextFlags.CallbackHistory;

        // Grab focus when the console just opened, or when we lost it for a non-deliberate reason last
        // frame (see below). SetKeyboardFocusHere(0) targets the next widget: the input.
        if (_justOpened || (_reclaimFocus && !_outputTakesFocus && !(editor && _searchTakesFocus)))
        {
            ImGui.SetKeyboardFocusHere(0);
            _justOpened = false;
            _reclaimFocus = false;
        }

        // The command prompt marker, shared by both skins.
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(CommandColor, "]");
        ImGui.SameLine();

        bool submitted;
        bool inputDeactivated;

        if (editor)
        {
            // Native, theme-driven input: the row's frame inherits the editor's FrameBg, and Enter is
            // the only way to submit (no Submit button), keeping the panel uncluttered.
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            submitted = ImGui.InputText("##input", _inputBuf, (uint)_inputBuf.Length, inputFlags, _textEditCallback);
            inputDeactivated = ImGui.IsItemDeactivated();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.08f, 0.09f, 0.10f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.20f, 0.22f, 0.25f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2.0f);

            float submitButtonWidth = 80.0f;
            ImGui.SetNextItemWidth(-submitButtonWidth - ImGui.GetStyle().ItemSpacing.X - 4.0f);

            submitted = ImGui.InputText("##input", _inputBuf, (uint)_inputBuf.Length, inputFlags, _textEditCallback);

            // Captured immediately after the widget: true on the frame the input stops being active.
            inputDeactivated = ImGui.IsItemDeactivated();

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.22f, 0.25f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.30f, 0.33f, 0.38f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.15f, 0.17f, 0.20f, 1.0f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2.0f);

            if (ImGui.Button("Submit", new Vector2(submitButtonWidth, 0)))
            {
                submitted = true;
            }

            ImGui.PopStyleVar();
            ImGui.PopStyleColor(3);
        }

        if (submitted)
        {
            string cmd = GetInputText();
            if (cmd.Length != 0)
            {
                Execute(cmd);
                if (_history.Count == 0 || _history[^1] != cmd)
                {
                    _history.Add(cmd);
                }

                _historyPos = -1;
            }

            _inputBuf[0] = 0;
        }

        // Keep the input glued to focus while the console owns the screen. Deleting all the text (e.g.
        // Ctrl+A then Backspace) makes ImGui drop the input's active state; without this it would go
        // unfocused mid-typing. We deliberately do NOT reclaim when the user pressed Escape (their way
        // out of the field) or has a context popup open, so those interactions still work.
        bool escape = ImGui.IsKeyPressed(ImGuiKey.Escape, false);
        bool popupOpen = ImGui.IsPopupOpen(string.Empty, ImGuiPopupFlags.AnyPopup);
        bool interactingWithItem = ImGui.IsAnyItemActive();
        if ((submitted || (inputDeactivated && !interactingWithItem)) && consoleFocused && !escape && !popupOpen
            && !_outputTakesFocus && !(editor && _searchTakesFocus))
        {
            _reclaimFocus = true;
        }
    }

    // Flattens the current log into newline-joined text for the clipboard.
    private string BuildLogText()
    {
        var sb = new StringBuilder();
        lock (_linesLock)
        {
            foreach (ConsoleLine line in _lines)
            {
                sb.Append(line.Text).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>Clears retained output, preserving command history and editor filters.</summary>
    public void ClearLines()
    {
        lock (_linesLock)
        {
            _lines.Clear();
        }
    }

    private void RegisterBuiltins()
    {
        Register("quit", _ => Application.Instance.Quit(), "Exit the application");

        Register("help", _ =>
        {
            List<string> names = _commands.Keys.ToList();
            names.Sort(StringComparer.Ordinal);

            Print("Available commands:");
            foreach (string name in names)
            {
                CommandInfo cmd = _commands[name];
                Print(cmd.Help.Length == 0 ? "  " + name : "  " + name + " - " + cmd.Help);
            }
        }, "List all available commands");

        Register("clear", _ => ClearLines(), "Clear console output");

        Register("physics_debug", args =>
        {
            if (args.Count > 0 && bool.TryParse(args[0], out bool val))
            {
                Spot.Engine.Physics.PhysicsDebug.ShowColliders = val;
                Print($"Physics debug colliders set to {val}");
            }
            else
            {
                Spot.Engine.Physics.PhysicsDebug.ShowColliders = !Spot.Engine.Physics.PhysicsDebug.ShowColliders;
                Print($"Physics debug colliders toggled to {Spot.Engine.Physics.PhysicsDebug.ShowColliders}");
            }
        }, "Toggles global debug rendering of colliders (e.g., 'physics_debug' or 'physics_debug true')");

        Register("fullbright", args =>
        {
            if (args.Count > 0 && bool.TryParse(args[0], out bool val))
            {
                Spot.Engine.Graphics.RendererDebug.Fullbright = val;
                Print($"Fullbright set to {val}");
            }
            else
            {
                Spot.Engine.Graphics.RendererDebug.Fullbright = !Spot.Engine.Graphics.RendererDebug.Fullbright;
                Print($"Fullbright toggled to {Spot.Engine.Graphics.RendererDebug.Fullbright}");
            }
        }, "Toggles fullbright rendering mode (disables lighting)");

        Register("wireframe", args =>
        {
            if (args.Count > 0 && bool.TryParse(args[0], out bool val))
            {
                Spot.Engine.Graphics.RendererDebug.Wireframe = val;
                Print($"Wireframe set to {val}");
            }
            else
            {
                Spot.Engine.Graphics.RendererDebug.Wireframe = !Spot.Engine.Graphics.RendererDebug.Wireframe;
                Print($"Wireframe toggled to {Spot.Engine.Graphics.RendererDebug.Wireframe}");
            }
        }, "Toggles wireframe rendering for 3D meshes");

        Register("occlusion", args =>
        {
            if (args.Count > 0 && bool.TryParse(args[0], out bool val))
            {
                Spot.Engine.Graphics.RenderSettings.OcclusionCulling = val;
            }
            else
            {
                Spot.Engine.Graphics.RenderSettings.OcclusionCulling = !Spot.Engine.Graphics.RenderSettings.OcclusionCulling;
            }

            Spot.Engine.Graphics.RendererDebug.DisableOcclusionCulling = false;
            Print($"Occlusion culling {(Spot.Engine.Graphics.RenderSettings.OcclusionCulling ? "on" : "off")}");
            Print($"Last pass: {Spot.Engine.Graphics.RendererDebug.VisibleMeshCount} drawn, "
                + $"{Spot.Engine.Graphics.RendererDebug.CulledMeshCount} off screen, "
                + $"{Spot.Engine.Graphics.RendererDebug.OccludedMeshCount} behind {Spot.Engine.Graphics.RendererDebug.OccluderCount} occluder(s)");
        }, "Toggles occlusion culling and prints what the last 3D pass culled (e.g., 'occlusion', 'occlusion off')");

        Register("vsync", args =>
        {
            if (args.Count > 0 && bool.TryParse(args[0], out bool val))
            {
                Spot.Engine.Graphics.RenderSettings.VSync = val;
            }
            else
            {
                Spot.Engine.Graphics.RenderSettings.VSync = !Spot.Engine.Graphics.RenderSettings.VSync;
            }

            Print($"VSync {(Spot.Engine.Graphics.RenderSettings.VSync ? "on" : "off")} (use 'vsync off' to uncap and profile true frame time)");
        }, "Toggles vertical sync (e.g., 'vsync', 'vsync off'); off uncaps the frame rate for profiling");

        Register("stats", _ =>
        {
            Print($"Frame: {Spot.Engine.FrameStats.FrameTimeMs:0.00} ms ({Spot.Engine.FrameStats.Fps:0} FPS); last {Spot.Engine.FrameStats.LastFrameMs:0.00} ms");
            Print($"VSync: {(Spot.Engine.Graphics.RenderSettings.VSync ? "on" : "off")}");
            Print($"Meshes: {Spot.Engine.Graphics.RendererDebug.VisibleMeshCount} drawn, "
                + $"{Spot.Engine.Graphics.RendererDebug.CulledMeshCount} off screen, "
                + $"{Spot.Engine.Graphics.RendererDebug.OccludedMeshCount} occluded");
        }, "Prints the current smoothed frame time / FPS, VSync state, and what the last 3D pass culled");

        Register("bind", args =>
        {
            if (args.Count < 2)
            {
                Print("[error] Usage: bind <key> <action>  (e.g. 'bind w forward')");
                return;
            }

            if (!InputBinding.TryParse(args[0], out InputBinding binding))
            {
                Print($"[error] Unknown key or button: '{args[0]}'");
                return;
            }

            string action = args[1];
            Input.Bind(action, binding);
            Print($"Bound {binding} -> {action}");
        }, "Binds a key/button to an action (e.g., 'bind w forward')");

        Register("unbind", args =>
        {
            if (args.Count < 1)
            {
                Print("[error] Usage: unbind <key|action>  (e.g. 'unbind w' or 'unbind forward')");
                return;
            }

            string token = args[0];

            // A token that names a key/button unbinds that physical input everywhere; otherwise it's
            // treated as an action name and the whole action is removed. This lets both 'unbind w' and
            // 'unbind forward' do the obvious thing.
            if (InputBinding.TryParse(token, out InputBinding binding))
            {
                Print(Input.Unbind(binding) ? $"Unbound {binding}" : $"{binding} was not bound to any action");
            }
            else if (Input.UnbindAction(token))
            {
                Print($"Unbound action '{token}'");
            }
            else
            {
                Print($"[error] '{token}' is not a known key/button or a bound action");
            }
        }, "Unbinds a key/button, or a whole action by name (e.g., 'unbind w' or 'unbind forward')");

        Register("bindings", _ =>
        {
            List<string> names = Input.GetActionNames().ToList();
            if (names.Count == 0)
            {
                Print("No input bindings.");
                return;
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            Print("Input bindings:");
            foreach (string name in names)
            {
                string keys = string.Join(", ", Input.GetBindings(name).Select(b => b.ToString()));
                Print($"  {name}: {keys}");
            }
        }, "Lists all action bindings");

        Register("resetbinds", _ =>
        {
            Input.ResetBindingsToDefaults();
            Print("Input bindings reset to defaults.");
        }, "Resets all input bindings to the project defaults");

        Register("volume", args =>
        {
            // 'volume' alone lists the mix; 'volume 0.5' is shorthand for the master bus, since that is what a
            // player reaching for the console almost always means.
            if (args.Count == 0)
            {
                PrintBuses();
                return;
            }

            string busName = args.Count >= 2 ? args[0] : Spot.Engine.Audio.AudioMixer.MasterBus;
            string value = args.Count >= 2 ? args[1] : args[0];

            Spot.Engine.Audio.AudioBus? bus = Spot.Engine.Audio.AudioMixer.Find(busName);
            if (bus == null)
            {
                Print($"[error] Unknown audio bus: '{busName}' (try 'buses')");
                return;
            }

            if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float level))
            {
                Print($"[error] '{value}' is not a number. Usage: volume [bus] <0..1>");
                return;
            }

            bus.Volume = level;
            Print($"{bus.Name} volume set to {bus.Volume:0.00}");
        }, "Sets a mixer bus volume, e.g. 'volume 0.5' (master) or 'volume music 0.2'; alone, lists the mix");

        Register("mute", args =>
        {
            string busName = args.Count >= 1 ? args[0] : Spot.Engine.Audio.AudioMixer.MasterBus;
            Spot.Engine.Audio.AudioBus? bus = Spot.Engine.Audio.AudioMixer.Find(busName);
            if (bus == null)
            {
                Print($"[error] Unknown audio bus: '{busName}' (try 'buses')");
                return;
            }

            bus.Mute = args.Count >= 2 && bool.TryParse(args[1], out bool on) ? on : !bus.Mute;
            Print($"{bus.Name} {(bus.Mute ? "muted" : "unmuted")}");
        }, "Mutes or unmutes a mixer bus (e.g. 'mute', 'mute sfx', 'mute music true')");

        Register("solo", args =>
        {
            if (args.Count == 0)
            {
                Spot.Engine.Audio.AudioMixer.ClearSolos();
                Print("Cleared all solos.");
                return;
            }

            Spot.Engine.Audio.AudioBus? bus = Spot.Engine.Audio.AudioMixer.Find(args[0]);
            if (bus == null)
            {
                Print($"[error] Unknown audio bus: '{args[0]}' (try 'buses')");
                return;
            }

            bus.Solo = !bus.Solo;
            Print($"{bus.Name} solo {(bus.Solo ? "on" : "off")}");
        }, "Solos a mixer bus in isolation, or clears every solo when called with no bus");

        Register("buses", _ => PrintBuses(), "Lists the audio mixer buses with their levels and routing");
    }

    // Prints the mixer as an indented tree: each bus with its own fader, the gain that actually applies after
    // its parents/mute/solo, and how many voices are sounding through it.
    private void PrintBuses()
    {
        Print("Audio buses:");
        foreach (Spot.Engine.Audio.AudioBus bus in Spot.Engine.Audio.AudioMixer.Buses)
        {
            int depth = 0;
            for (Spot.Engine.Audio.AudioBus? p = Spot.Engine.Audio.AudioMixer.ParentOf(bus); p != null && depth < 8; p = Spot.Engine.Audio.AudioMixer.ParentOf(p))
            {
                depth++;
            }

            string flags = (bus.Mute ? " [muted]" : string.Empty) + (bus.Solo ? " [solo]" : string.Empty);
            Print($"  {new string(' ', depth * 2)}{bus.Name}: {bus.Volume:0.00} " +
                  $"(effective {Spot.Engine.Audio.AudioMixer.GetGain(bus.Name):0.00}, {bus.ActiveVoices} voice(s)){flags}");
        }
    }

    private string GetInputText()
    {
        int length = Array.IndexOf(_inputBuf, (byte)0);
        if (length < 0)
        {
            length = _inputBuf.Length;
        }

        return Encoding.UTF8.GetString(_inputBuf, 0, length);
    }

    private unsafe int HandleTextEdit(ImGuiInputTextCallbackData* data)
    {
        var ptr = new ImGuiInputTextCallbackDataPtr(data);
        if (ptr.EventFlag != ImGuiInputTextFlags.CallbackHistory)
        {
            return 0;
        }

        int prev = _historyPos;
        if (ptr.EventKey == ImGuiKey.UpArrow)
        {
            if (_historyPos == -1)
            {
                _historyPos = _history.Count - 1;
            }
            else if (_historyPos > 0)
            {
                _historyPos--;
            }
        }
        else if (ptr.EventKey == ImGuiKey.DownArrow)
        {
            if (_historyPos != -1 && ++_historyPos >= _history.Count)
            {
                _historyPos = -1;
            }
        }

        if (prev != _historyPos)
        {
            string entry = _historyPos >= 0 ? _history[_historyPos] : string.Empty;
            ptr.DeleteChars(0, ptr.BufTextLen);
            ptr.InsertChars(0, entry);
        }

        return 0;
    }

    public readonly struct ConsoleLine
    {
        public ConsoleLine(string text, System.Numerics.Vector4 color)
            : this(text, color, LogLevel.Info)
        {
        }

        public ConsoleLine(string text, Vector4 color, LogLevel level)
            : this(text, color, level, 0)
        {
        }

        internal ConsoleLine(string text, Vector4 color, LogLevel level, long id)
        {
            Text = text;
            Color = color;
            Level = level;
            Id = id;
        }

        public string Text { get; }

        public Vector4 Color { get; }

        /// <summary>Gets the severity used by the editor's counters and filters.</summary>
        public LogLevel Level { get; }

        internal long Id { get; }
    }

    // Selection belongs to the UI thread; background writers only change the retained log buffer.
    internal sealed class LogSelection
    {
        private readonly HashSet<long> _ids = new();
        private readonly HashSet<long> _visibleIds = new();
        private long? _anchor;
        public int Count => _ids.Count;
        public bool Contains(long id) => _ids.Contains(id);

        public void Update(IReadOnlyList<ConsoleLine> visibleLines)
        {
            _visibleIds.Clear();
            foreach (ConsoleLine line in visibleLines) _visibleIds.Add(line.Id);
            _ids.IntersectWith(_visibleIds);
            if (_anchor.HasValue && !_visibleIds.Contains(_anchor.Value)) _anchor = null;
        }

        public void Select(long id, IReadOnlyList<ConsoleLine> visibleLines, bool additive, bool range)
        {
            int clicked = -1;
            int anchor = -1;
            for (int i = 0; i < visibleLines.Count; i++)
            {
                if (visibleLines[i].Id == id) clicked = i;
                if (visibleLines[i].Id == _anchor) anchor = i;
            }
            if (clicked < 0) return;
            if (!additive) _ids.Clear();
            if (range && anchor >= 0)
            {
                for (int i = Math.Min(anchor, clicked); i <= Math.Max(anchor, clicked); i++)
                    _ids.Add(visibleLines[i].Id);
            }
            else
            {
                if (!_ids.Add(id)) _ids.Remove(id);
                _anchor = id;
            }
        }

        public void SelectAll(IReadOnlyList<ConsoleLine> visibleLines)
        {
            _ids.Clear();
            _ids.UnionWith(visibleLines.Select(line => line.Id));
            _anchor = visibleLines.Count > 0 ? visibleLines[0].Id : null;
        }

        public string BuildText(IReadOnlyList<ConsoleLine> visibleLines) =>
            string.Join('\n', visibleLines.Where(line => _ids.Contains(line.Id)).Select(line => line.Text));

        public void Clear()
        {
            _ids.Clear();
            _anchor = null;
        }
    }
}

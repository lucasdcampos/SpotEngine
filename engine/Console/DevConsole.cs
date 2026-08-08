using System.Numerics;
using System.Text;
using ImGuiNET;
using Spot.Core;

namespace Spot.Console;

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
/// An in-game developer console rendered with ImGui.
/// </summary>
public sealed class DevConsole
{
    private const int MaxLines = 500;

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

    private readonly Dictionary<string, CommandInfo> _commands = new();
    private readonly List<ConsoleLine> _lines = new();

    // Log lines can arrive from background threads (for example a build process piping its output
    // through the logger), so every access to _lines is guarded. Rendering copies into this scratch
    // buffer under the lock and then draws from it, keeping the lock off the ImGui calls.
    private readonly object _linesLock = new();
    private readonly List<ConsoleLine> _renderBuffer = new();

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
    /// Toggles the visibility of the console.
    /// </summary>
    public void Toggle()
    {
        _open = !_open;
        if (_open)
        {
            _justOpened = true;
            _scrollToBottom = true;
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
    public void Print(string text, Vector4 color)
    {
        lock (_linesLock)
        {
            _lines.Add(new ConsoleLine(text, color));
            if (_lines.Count > MaxLines)
            {
                _lines.RemoveRange(0, _lines.Count - MaxLines);
            }
        }

        _scrollToBottom = true;
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
        if (!_open)
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

        DrawContents();

        ImGui.End();

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
    }

    /// <summary>
    /// Renders the inner contents of the console (logs and input).
    /// </summary>
    public void DrawContents()
    {
        // A monospaced face (when the host provides one) makes logs, timestamps and typed commands line
        // up like a terminal. Pushed around the whole body so the output and input share it.
        bool pushedFont = MonospaceFont.HasValue;
        if (pushedFont)
        {
            ImGui.PushFont(MonospaceFont!.Value);
        }

        // A roomier command line, Source-console style. Pushed for the whole body so the footer height
        // below (which reads the frame height) accounts for the taller input, and the Submit button and
        // prompt inherit the same padding.
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8.0f, 7.0f));

        bool consoleFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

        float footerHeight = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing() + 4.0f;

        DrawOutput(new Vector2(0.0f, -footerHeight));

        // Subtly spaced from the output box
        ImGui.Dummy(new Vector2(0, 2.0f));

        DrawInputRow(consoleFocused);

        ImGui.PopStyleVar();

        if (pushedFont)
        {
            ImGui.PopFont();
        }
    }

    // Draws the scrolling, colored log region. Right-clicking it offers copy/clear, the pragmatic
    // stand-in for character-level selection (which ImGui can't do while keeping per-line colors).
    private void DrawOutput(Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.06f, 0.07f, 0.08f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.20f, 0.22f, 0.25f, 1.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 2.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1.0f);

        ImGui.BeginChild("##output", size, ImGuiChildFlags.Border, ImGuiWindowFlags.HorizontalScrollbar);

        // Snapshot the lines under the lock so a background thread appending output cannot mutate the
        // list while we enumerate it, then render from the copy without holding the lock.
        lock (_linesLock)
        {
            _renderBuffer.Clear();
            _renderBuffer.AddRange(_lines);
        }

        // A touch more vertical spacing between log lines keeps a busy console legible.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 3.0f));

        ImGui.Dummy(new Vector2(0, 2.0f));
        ImGui.Indent(6.0f);

        foreach (ConsoleLine line in _renderBuffer)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, line.Color);
            ImGui.TextUnformatted(line.Text);
            ImGui.PopStyleColor();
        }

        ImGui.Unindent(6.0f);
        ImGui.Dummy(new Vector2(0, 2.0f));

        ImGui.PopStyleVar();

        if (ImGui.BeginPopupContextWindow("##output_ctx"))
        {
            if (ImGui.MenuItem("Copy all"))
            {
                ImGui.SetClipboardText(BuildLogText());
            }

            if (ImGui.MenuItem("Clear"))
            {
                ClearLines();
            }

            ImGui.EndPopup();
        }

        if (_scrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            _scrollToBottom = false;
        }

        ImGui.EndChild();

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    // Draws the "] input  [Submit]" command line and keeps it focused while the console is open.
    private void DrawInputRow(bool consoleFocused)
    {
        const ImGuiInputTextFlags inputFlags = ImGuiInputTextFlags.EnterReturnsTrue
                                             | ImGuiInputTextFlags.EscapeClearsAll
                                             | ImGuiInputTextFlags.CallbackHistory;

        // Grab focus when the console just opened, or when we lost it for a non-deliberate reason last
        // frame (see below). SetKeyboardFocusHere(0) targets the next widget: the input.
        if (_justOpened || _reclaimFocus)
        {
            ImGui.SetKeyboardFocusHere(0);
            _justOpened = false;
            _reclaimFocus = false;
        }

        // The classic Source command prompt.
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(CommandColor, "]");
        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.08f, 0.09f, 0.10f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.20f, 0.22f, 0.25f, 1.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2.0f);

        float submitButtonWidth = 80.0f;
        ImGui.SetNextItemWidth(-submitButtonWidth - ImGui.GetStyle().ItemSpacing.X - 4.0f);

        bool submitted = ImGui.InputText("##input", _inputBuf, (uint)_inputBuf.Length, inputFlags, _textEditCallback);

        // Captured immediately after the widget: true on the frame the input stops being active.
        bool inputDeactivated = ImGui.IsItemDeactivated();

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
        if ((submitted || inputDeactivated) && consoleFocused && !escape && !popupOpen)
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

    private void ClearLines()
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
                Spot.Physics.PhysicsDebug.ShowColliders = val;
                Print($"Physics debug colliders set to {val}");
            }
            else
            {
                Spot.Physics.PhysicsDebug.ShowColliders = !Spot.Physics.PhysicsDebug.ShowColliders;
                Print($"Physics debug colliders toggled to {Spot.Physics.PhysicsDebug.ShowColliders}");
            }
        }, "Toggles global debug rendering of colliders (e.g., 'physics_debug' or 'physics_debug true')");

        Register("fullbright", args =>
        {
            if (args.Count > 0 && bool.TryParse(args[0], out bool val))
            {
                Spot.Rendering.RendererDebug.Fullbright = val;
                Print($"Fullbright set to {val}");
            }
            else
            {
                Spot.Rendering.RendererDebug.Fullbright = !Spot.Rendering.RendererDebug.Fullbright;
                Print($"Fullbright toggled to {Spot.Rendering.RendererDebug.Fullbright}");
            }
        }, "Toggles fullbright rendering mode (disables lighting)");

        Register("wireframe", args =>
        {
            if (args.Count > 0 && bool.TryParse(args[0], out bool val))
            {
                Spot.Rendering.RendererDebug.Wireframe = val;
                Print($"Wireframe set to {val}");
            }
            else
            {
                Spot.Rendering.RendererDebug.Wireframe = !Spot.Rendering.RendererDebug.Wireframe;
                Print($"Wireframe toggled to {Spot.Rendering.RendererDebug.Wireframe}");
            }
        }, "Toggles wireframe rendering for 3D meshes");
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
        {
            Text = text;
            Color = color;
        }

        public string Text { get; }

        public Vector4 Color { get; }
    }
}

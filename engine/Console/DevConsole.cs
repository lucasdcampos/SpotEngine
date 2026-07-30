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
        if (text.Length >= 2 && text[0] == '>' && text[1] == ' ')
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

        Print("> " + line);

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

        if (!ImGui.Begin("Developer Console", ref _open, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        DrawContents();

        ImGui.End();
    }

    /// <summary>
    /// Renders the inner contents of the console (logs and input).
    /// </summary>
    public void DrawContents()
    {
        float footerHeight = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing();
        ImGui.BeginChild("##output", new Vector2(0.0f, -footerHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        // Snapshot the lines under the lock so a background thread appending output cannot mutate the
        // list while we enumerate it, then render from the copy without holding the lock.
        lock (_linesLock)
        {
            _renderBuffer.Clear();
            _renderBuffer.AddRange(_lines);
        }

        foreach (ConsoleLine line in _renderBuffer)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, line.Color);
            ImGui.TextUnformatted(line.Text);
            ImGui.PopStyleColor();
        }

        if (_scrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            _scrollToBottom = false;
        }

        ImGui.EndChild();
        ImGui.Separator();

        const ImGuiInputTextFlags inputFlags = ImGuiInputTextFlags.EnterReturnsTrue
                                             | ImGuiInputTextFlags.EscapeClearsAll
                                             | ImGuiInputTextFlags.CallbackHistory;

        if (_justOpened)
        {
            ImGui.SetKeyboardFocusHere(0);
            _justOpened = false;
        }

        ImGui.SetNextItemWidth(-1.0f);
        bool submitted = ImGui.InputText("##input", _inputBuf, (uint)_inputBuf.Length, inputFlags, _textEditCallback);

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
            ImGui.SetKeyboardFocusHere(-1);
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

        Register("clear", _ =>
        {
            lock (_linesLock)
            {
                _lines.Clear();
            }
        }, "Clear console output");
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

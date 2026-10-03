using System.Numerics;
using System.Text;
using ImGuiNET;
using Spot.Engine;
using Spot.Framework;

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

        // A monospaced face (when the host provides one) makes logs, timestamps and typed commands line
        // up like a terminal. Pushed around the whole body so the output and input share it.
        bool pushedFont = MonospaceFont.HasValue;
        if (pushedFont)
        {
            ImGui.PushFont(MonospaceFont!.Value);
        }

        // The runtime console overlays a live game, so it uses a roomier, Source-style command line
        // (pushed for the whole body so the footer height and prompt share the padding). The editor
        // panel instead keeps the active theme's frame metrics so it reads as a native dockable panel.
        if (!editor)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8.0f, 7.0f));
        }

        bool consoleFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

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

    // Draws the scrolling, colored log region. Right-clicking it offers copy/clear, the pragmatic
    // stand-in for character-level selection (which ImGui can't do while keeping per-line colors).
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

        // Was the view pinned to the bottom coming into this frame? Checked before drawing this frame's
        // content (so GetScrollMaxY still reflects last frame's height) so a new log line keeps the console
        // stuck to the bottom only when the user was already there — never yanking them off history they
        // scrolled up to read.
        bool stickToBottom = ImGui.GetScrollMaxY() <= 0f || ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1.0f;

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
        if (_justOpened || _reclaimFocus)
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
        if ((submitted || (inputDeactivated && !interactingWithItem)) && consoleFocused && !escape && !popupOpen)
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
                Spot.Engine.Rendering.RendererDebug.Fullbright = val;
                Print($"Fullbright set to {val}");
            }
            else
            {
                Spot.Engine.Rendering.RendererDebug.Fullbright = !Spot.Engine.Rendering.RendererDebug.Fullbright;
                Print($"Fullbright toggled to {Spot.Engine.Rendering.RendererDebug.Fullbright}");
            }
        }, "Toggles fullbright rendering mode (disables lighting)");

        Register("wireframe", args =>
        {
            if (args.Count > 0 && bool.TryParse(args[0], out bool val))
            {
                Spot.Engine.Rendering.RendererDebug.Wireframe = val;
                Print($"Wireframe set to {val}");
            }
            else
            {
                Spot.Engine.Rendering.RendererDebug.Wireframe = !Spot.Engine.Rendering.RendererDebug.Wireframe;
                Print($"Wireframe toggled to {Spot.Engine.Rendering.RendererDebug.Wireframe}");
            }
        }, "Toggles wireframe rendering for 3D meshes");

        Register("occlusion", args =>
        {
            if (args.Count > 0 && bool.TryParse(args[0], out bool val))
            {
                Spot.Engine.Rendering.RenderSettings.OcclusionCulling = val;
            }
            else
            {
                Spot.Engine.Rendering.RenderSettings.OcclusionCulling = !Spot.Engine.Rendering.RenderSettings.OcclusionCulling;
            }

            Spot.Engine.Rendering.RendererDebug.DisableOcclusionCulling = false;
            Print($"Occlusion culling {(Spot.Engine.Rendering.RenderSettings.OcclusionCulling ? "on" : "off")}");
            Print($"Last pass: {Spot.Engine.Rendering.RendererDebug.VisibleMeshCount} drawn, "
                + $"{Spot.Engine.Rendering.RendererDebug.CulledMeshCount} off screen, "
                + $"{Spot.Engine.Rendering.RendererDebug.OccludedMeshCount} behind {Spot.Engine.Rendering.RendererDebug.OccluderCount} occluder(s)");
        }, "Toggles occlusion culling and prints what the last 3D pass culled (e.g., 'occlusion', 'occlusion off')");

        Register("vsync", args =>
        {
            if (args.Count > 0 && bool.TryParse(args[0], out bool val))
            {
                Spot.Engine.Rendering.RenderSettings.VSync = val;
            }
            else
            {
                Spot.Engine.Rendering.RenderSettings.VSync = !Spot.Engine.Rendering.RenderSettings.VSync;
            }

            Print($"VSync {(Spot.Engine.Rendering.RenderSettings.VSync ? "on" : "off")} (use 'vsync off' to uncap and profile true frame time)");
        }, "Toggles vertical sync (e.g., 'vsync', 'vsync off'); off uncaps the frame rate for profiling");

        Register("stats", _ =>
        {
            Print($"Frame: {Spot.Framework.FrameStats.FrameTimeMs:0.00} ms ({Spot.Framework.FrameStats.Fps:0} FPS); last {Spot.Framework.FrameStats.LastFrameMs:0.00} ms");
            Print($"VSync: {(Spot.Engine.Rendering.RenderSettings.VSync ? "on" : "off")}");
            Print($"Meshes: {Spot.Engine.Rendering.RendererDebug.VisibleMeshCount} drawn, "
                + $"{Spot.Engine.Rendering.RendererDebug.CulledMeshCount} off screen, "
                + $"{Spot.Engine.Rendering.RendererDebug.OccludedMeshCount} occluded");
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

            string busName = args.Count >= 2 ? args[0] : Spot.Framework.Audio.AudioMixer.MasterBus;
            string value = args.Count >= 2 ? args[1] : args[0];

            Spot.Framework.Audio.AudioBus? bus = Spot.Framework.Audio.AudioMixer.Find(busName);
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
            string busName = args.Count >= 1 ? args[0] : Spot.Framework.Audio.AudioMixer.MasterBus;
            Spot.Framework.Audio.AudioBus? bus = Spot.Framework.Audio.AudioMixer.Find(busName);
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
                Spot.Framework.Audio.AudioMixer.ClearSolos();
                Print("Cleared all solos.");
                return;
            }

            Spot.Framework.Audio.AudioBus? bus = Spot.Framework.Audio.AudioMixer.Find(args[0]);
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
        foreach (Spot.Framework.Audio.AudioBus bus in Spot.Framework.Audio.AudioMixer.Buses)
        {
            int depth = 0;
            for (Spot.Framework.Audio.AudioBus? p = Spot.Framework.Audio.AudioMixer.ParentOf(bus); p != null && depth < 8; p = Spot.Framework.Audio.AudioMixer.ParentOf(p))
            {
                depth++;
            }

            string flags = (bus.Mute ? " [muted]" : string.Empty) + (bus.Solo ? " [solo]" : string.Empty);
            Print($"  {new string(' ', depth * 2)}{bus.Name}: {bus.Volume:0.00} " +
                  $"(effective {Spot.Framework.Audio.AudioMixer.GetGain(bus.Name):0.00}, {bus.ActiveVoices} voice(s)){flags}");
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
        {
            Text = text;
            Color = color;
        }

        public string Text { get; }

        public Vector4 Color { get; }
    }
}

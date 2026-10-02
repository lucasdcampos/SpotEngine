using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Spot.Framework.Audio;
using Spot.DebugUI.UI;

namespace Spot.DebugUI.Panels;

/// <summary>
/// The audio mixer: one channel strip per <see cref="AudioBus"/>, laid out like a console desk — a vertical
/// fader with a live level meter, mute and solo, and the bus it feeds. Editing here drives
/// <see cref="AudioMixer"/> directly, so a fader move is audible immediately on sounds already playing.
///
/// It lives in Spot.DebugUI (not the editor) so the same panel serves the editor's Audio Mixer window and the
/// runtime debug overlay. Persisting the layout is the host's business: the editor hands us
/// <see cref="LayoutChanged"/> to write the project file, while the runtime overlay leaves it null and treats a
/// tweak as session-only.
/// </summary>
public sealed class AudioMixerPanel
{
    private const float StripWidth = 104.0f;
    private const float FaderHeight = 168.0f;
    private const float MeterWidth = 10.0f;
    private const float VolumeNudge = 0.05f;

    /// <summary>
    /// Raised when the bus layout or an authored level changes and should be persisted (the editor saves the
    /// project). Not raised for solo, which is an audition tool rather than authored data.
    /// </summary>
    public Action? LayoutChanged { get; set; }

    // The strip the keyboard acts on. Held by name (not by reference) so a removed or renamed bus simply stops
    // resolving instead of pinning a dead object.
    private string? _selected;

    // The bus being renamed inline, plus its edit buffer; cleared on commit or cancel.
    private string? _renaming;
    private string _renameBuffer = string.Empty;
    private bool _renameFocusPending;

    /// <summary>Draws the mixer window. <paramref name="isOpen"/> is cleared when the user closes it.</summary>
    public void OnImGuiRender(ref bool isOpen)
    {
        if (!isOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(560.0f, 340.0f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Audio Mixer", ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        EnsureSelection();
        DrawToolbar();
        ImGui.Separator();

        // The strips scroll horizontally as a group; each is its own child so a long bus list stays navigable.
        if (ImGui.BeginChild("##strips", new Vector2(0.0f, -ImGui.GetFrameHeightWithSpacing()),
                ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            IReadOnlyList<AudioBus> buses = AudioMixer.Buses;
            for (int i = 0; i < buses.Count; i++)
            {
                if (i > 0)
                {
                    ImGui.SameLine();
                }

                DrawStrip(buses[i]);
            }
        }

        ImGui.EndChild();

        DrawFooter();

        // Shortcuts are handled after the strips so a click this frame has already moved the selection.
        HandleShortcuts();

        ImGui.End();
    }

    // ----- Chrome -------------------------------------------------------------------------------------

    private void DrawToolbar()
    {
        if (ImGui.Button("Add Bus"))
        {
            AddBusUnderSelection();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Add a bus under the selected one  (Ctrl+N)");
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!AudioMixer.AnySolo);
        if (ImGui.Button("Clear Solos"))
        {
            AudioMixer.ClearSolos();
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Reset to Defaults"))
        {
            AudioMixer.ResetToDefaults();
            _selected = AudioMixer.MasterBus;
            _renaming = null;
            LayoutChanged?.Invoke();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Replace the layout with Master / Music / SFX / UI");
        }

        // Solo is loud in its consequences (everything else goes silent), so say so plainly while it is on.
        if (AudioMixer.AnySolo)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.25f, 1.0f), "SOLO ACTIVE");
        }
    }

    private void DrawFooter()
    {
        AudioBus? selected = AudioMixer.Find(_selected);
        string name = selected?.Name ?? "—";
        ImGui.TextDisabled($"{name}   •   M mute   S solo   F2 rename   ←/→ select   ↑/↓ level   0 reset   Del remove");
    }

    // ----- Channel strip ----------------------------------------------------------------------------

    private void DrawStrip(AudioBus bus)
    {
        EditorPalette palette = EditorThemeManager.Current.Palette;
        bool isMaster = ReferenceEquals(bus, AudioMixer.Master);
        bool isSelected = string.Equals(bus.Name, _selected, StringComparison.OrdinalIgnoreCase);

        ImGui.PushID(bus.Name);

        // A selected strip is outlined in the accent color; everything else keeps the normal child border.
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1.0f);
        ImGui.PushStyleColor(ImGuiCol.Border, isSelected ? palette.Accent : palette.Border);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, isSelected ? palette.FrameBgHovered : palette.ChildBg);

        if (ImGui.BeginChild("##strip", new Vector2(StripWidth, 0.0f), ImGuiChildFlags.Border))
        {
            DrawStripHeader(bus, isMaster, palette);
            DrawRouting(bus, isMaster, palette);
            ImGui.Spacing();
            DrawFaderAndMeter(bus, palette);
            DrawLevelReadout(bus, palette);
            ImGui.Spacing();
            DrawMuteSolo(bus, palette);
            DrawVoiceCount(bus);

            // Clicking anywhere in the strip's empty space selects it too, so the whole card is a target.
            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _selected = bus.Name;
            }

            DrawContextMenu(bus, isMaster);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();
        ImGui.PopID();
    }

    private void DrawStripHeader(AudioBus bus, bool isMaster, EditorPalette palette)
    {
        if (string.Equals(_renaming, bus.Name, StringComparison.OrdinalIgnoreCase))
        {
            bool justFocused = _renameFocusPending;
            if (_renameFocusPending)
            {
                ImGui.SetKeyboardFocusHere();
                _renameFocusPending = false;
            }

            ImGui.SetNextItemWidth(-1.0f);
            bool commit = ImGui.InputText("##rename", ref _renameBuffer, 64,
                ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

            // Enter commits; clicking away or Escape abandons the edit, which is what a user expects from an
            // inline rename and keeps a half-typed name out of the layout. The field is not yet active on the
            // frame focus was requested, so losing focus only counts from the frame after that.
            if (commit)
            {
                CommitRename(bus);
            }
            else if (!justFocused && !ImGui.IsItemActive())
            {
                _renaming = null;
            }

            return;
        }

        // The name doubles as the strip's select/rename target: single click selects, double click renames.
        Vector4 nameColor = isMaster ? palette.Accent : palette.Text;
        ImGui.PushStyleColor(ImGuiCol.Text, nameColor);
        ImGui.Selectable(Truncate(bus.Name, 12), string.Equals(bus.Name, _selected, StringComparison.OrdinalIgnoreCase));
        ImGui.PopStyleColor();

        if (ImGui.IsItemClicked())
        {
            _selected = bus.Name;
        }

        if (!isMaster && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            BeginRename(bus);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(isMaster
                ? $"{bus.Name} — every sound passes through this bus"
                : $"{bus.Name} — double-click to rename");
        }
    }

    private void DrawRouting(AudioBus bus, bool isMaster, EditorPalette palette)
    {
        if (isMaster)
        {
            ImGui.TextColored(palette.TextDisabled, "→ output");
            return;
        }

        // Only buses that would not create a cycle are offered; AudioMixer.Reparent is the authority, so the
        // list is built by asking it what it would accept.
        var options = new List<string>();
        foreach (AudioBus candidate in AudioMixer.Buses)
        {
            if (!ReferenceEquals(candidate, bus) && !IsAncestorOf(bus, candidate))
            {
                options.Add(candidate.Name);
            }
        }

        int current = Math.Max(0, options.IndexOf(bus.Parent ?? AudioMixer.MasterBus));
        ImGui.SetNextItemWidth(-1.0f);
        if (ImGui.Combo("##route", ref current, options.ToArray(), options.Count) &&
            AudioMixer.Reparent(bus.Name, options[current]))
        {
            LayoutChanged?.Invoke();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("The bus this one feeds into");
        }
    }

    private void DrawFaderAndMeter(AudioBus bus, EditorPalette palette)
    {
        float volume = bus.Volume;
        float faderWidth = StripWidth - MeterWidth - (ImGui.GetStyle().ItemSpacing.X * 2.0f) -
                           (ImGui.GetStyle().WindowPadding.X * 2.0f);

        // A muted or solo-silenced bus shows a dimmed grab, so the fader position never implies audio that
        // isn't actually coming out.
        bool silenced = AudioMixer.GetGain(bus.Name) <= 0.0f;
        if (silenced)
        {
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, palette.TextDisabled);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, palette.TextDisabled);
        }

        if (ImGui.VSliderFloat("##fader", new Vector2(MathF.Max(faderWidth, 24.0f), FaderHeight),
                ref volume, 0.0f, 1.0f, string.Empty))
        {
            bus.Volume = volume;
        }

        if (silenced)
        {
            ImGui.PopStyleColor(2);
        }

        if (ImGui.IsItemClicked())
        {
            _selected = bus.Name;
        }

        // Only a finished drag is persisted: writing the project file on every pixel of a drag would be a
        // hundred saves per second.
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            LayoutChanged?.Invoke();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{bus.Name}: {FormatDecibels(bus.Volume)}");
        }

        ImGui.SameLine();
        DrawMeter(bus, new Vector2(MeterWidth, FaderHeight), palette);
    }

    private static void DrawMeter(AudioBus bus, Vector2 size, EditorPalette palette)
    {
        Vector2 origin = ImGui.GetCursorScreenPos();
        ImDrawListPtr dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(origin, origin + size, ImGui.GetColorU32(palette.FrameBg), 2.0f);

        float level = Math.Clamp(bus.Level, 0.0f, 1.0f);
        if (level > 0.001f)
        {
            float filled = size.Y * level;
            // Green while there is headroom, amber as it approaches unity — the usual read for a level bar.
            Vector4 color = level < 0.75f
                ? new Vector4(0.35f, 0.80f, 0.40f, 1.0f)
                : new Vector4(0.95f, 0.70f, 0.25f, 1.0f);
            dl.AddRectFilled(new Vector2(origin.X, origin.Y + size.Y - filled), origin + size,
                ImGui.GetColorU32(color), 2.0f);
        }

        dl.AddRect(origin, origin + size, ImGui.GetColorU32(palette.Border), 2.0f);

        // Reserve the space so the following widgets lay out under the meter rather than on top of it.
        ImGui.Dummy(size);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{bus.ActiveVoices} voice(s) through {bus.Name}");
        }
    }

    private void DrawLevelReadout(AudioBus bus, EditorPalette palette)
    {
        string text = FormatDecibels(bus.Volume);
        float offset = (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) * 0.5f;
        if (offset > 0.0f)
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offset);
        }

        ImGui.TextColored(bus.Mute ? palette.TextDisabled : palette.Text, text);
    }

    private void DrawMuteSolo(AudioBus bus, EditorPalette palette)
    {
        float width = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;

        bool mute = bus.Mute;
        if (DrawLatch("M", mute, width, new Vector4(0.80f, 0.30f, 0.25f, 1.0f), palette,
                mute ? $"{bus.Name} is muted  (M)" : $"Mute {bus.Name}  (M)"))
        {
            bus.Mute = !mute;
            _selected = bus.Name;
            LayoutChanged?.Invoke();
        }

        ImGui.SameLine();

        bool solo = bus.Solo;
        if (DrawLatch("S", solo, width, new Vector4(0.90f, 0.70f, 0.20f, 1.0f), palette,
                "Solo — silences every unrelated bus while on  (S)"))
        {
            bus.Solo = !solo;
            _selected = bus.Name;
        }
    }

    private static void DrawVoiceCount(AudioBus bus)
    {
        ImGui.TextDisabled(bus.ActiveVoices > 0 ? $"{bus.ActiveVoices} playing" : "idle");
    }

    private void DrawContextMenu(AudioBus bus, bool isMaster)
    {
        if (!ImGui.BeginPopupContextWindow("##busmenu"))
        {
            return;
        }

        _selected = bus.Name;

        if (ImGui.MenuItem("Add Child Bus", "Ctrl+N"))
        {
            AddBus(bus.Name);
        }

        if (ImGui.MenuItem("Rename", "F2", false, !isMaster))
        {
            BeginRename(bus);
        }

        if (ImGui.MenuItem("Reset Level", "0"))
        {
            bus.Volume = 1.0f;
            LayoutChanged?.Invoke();
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Remove", "Del", false, !isMaster))
        {
            RemoveBus(bus.Name);
        }

        ImGui.EndPopup();
    }

    // ----- Keyboard ---------------------------------------------------------------------------------

    private void HandleShortcuts()
    {
        // Only while this window has focus, and never while a rename (or any other text field) is being typed
        // into, so "m" and "s" stay ordinary characters there.
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || ImGui.GetIO().WantTextInput)
        {
            return;
        }

        AudioBus? bus = AudioMixer.Find(_selected);
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow))
        {
            MoveSelection(-1);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.RightArrow))
        {
            MoveSelection(1);
        }

        if (bus is null)
        {
            return;
        }

        ImGuiIOPtr io = ImGui.GetIO();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.N))
        {
            AddBusUnderSelection();
            return;
        }

        // Bare letters only: Ctrl+M is the editor's "toggle this window", and Alt/Shift combinations belong to
        // whatever else the host binds them to.
        if (io.KeyCtrl || io.KeyAlt || io.KeySuper)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.M))
        {
            bus.Mute = !bus.Mute;
            LayoutChanged?.Invoke();
        }

        if (ImGui.IsKeyPressed(ImGuiKey.S))
        {
            bus.Solo = !bus.Solo;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.F2))
        {
            BeginRename(bus);
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Delete))
        {
            RemoveBus(bus.Name);
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow) || ImGui.IsKeyPressed(ImGuiKey.DownArrow))
        {
            bus.Volume += ImGui.IsKeyPressed(ImGuiKey.UpArrow) ? VolumeNudge : -VolumeNudge;
            LayoutChanged?.Invoke();
        }

        if (ImGui.IsKeyPressed(ImGuiKey._0) || ImGui.IsKeyPressed(ImGuiKey.Keypad0))
        {
            bus.Volume = 1.0f;
            LayoutChanged?.Invoke();
        }
    }

    private void MoveSelection(int delta)
    {
        IReadOnlyList<AudioBus> buses = AudioMixer.Buses;
        if (buses.Count == 0)
        {
            return;
        }

        int index = 0;
        for (int i = 0; i < buses.Count; i++)
        {
            if (string.Equals(buses[i].Name, _selected, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        _selected = buses[Math.Clamp(index + delta, 0, buses.Count - 1)].Name;
    }

    // ----- Mutations --------------------------------------------------------------------------------

    private void EnsureSelection()
    {
        if (AudioMixer.Find(_selected) is null)
        {
            _selected = AudioMixer.MasterBus;
        }
    }

    private void AddBusUnderSelection() => AddBus(_selected ?? AudioMixer.MasterBus);

    private void AddBus(string parent)
    {
        AudioBus added = AudioMixer.AddBus("New Bus", parent);
        _selected = added.Name;
        BeginRename(added);
        LayoutChanged?.Invoke();
    }

    private void RemoveBus(string name)
    {
        if (!AudioMixer.RemoveBus(name))
        {
            return;
        }

        _renaming = null;
        _selected = AudioMixer.MasterBus;
        LayoutChanged?.Invoke();
    }

    private void BeginRename(AudioBus bus)
    {
        if (ReferenceEquals(bus, AudioMixer.Master))
        {
            return;
        }

        _renaming = bus.Name;
        _renameBuffer = bus.Name;
        _renameFocusPending = true;
    }

    private void CommitRename(AudioBus bus)
    {
        if (AudioMixer.RenameBus(bus.Name, _renameBuffer))
        {
            _selected = bus.Name; // RenameBus has already updated the instance's name.
            LayoutChanged?.Invoke();
        }

        _renaming = null;
    }

    // ----- Helpers ----------------------------------------------------------------------------------

    /// <summary>Formats a linear fader position the way a mixer labels it, with silence as -∞.</summary>
    private static string FormatDecibels(float linear)
        => linear <= 0.0001f ? "-inf dB" : $"{20.0f * MathF.Log10(linear):0.0} dB";

    private static bool IsAncestorOf(AudioBus bus, AudioBus candidate)
    {
        // True when `bus` sits above `candidate`, i.e. routing bus into candidate would close a loop.
        for (AudioBus? node = AudioMixer.ParentOf(candidate); node is not null; node = AudioMixer.ParentOf(node))
        {
            if (ReferenceEquals(node, bus))
            {
                return true;
            }
        }

        return false;
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>A small latch button that stays lit while on (the mixer's M and S buttons). Returns whether it was clicked.</summary>
    private static bool DrawLatch(string label, bool value, float width, Vector4 onColor,
        EditorPalette palette, string tooltip)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, value ? onColor : palette.Button);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, value ? Lighten(onColor) : palette.ButtonHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, value ? onColor : palette.ButtonActive);

        bool clicked = ImGui.Button(label, new Vector2(width, 0.0f));

        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        return clicked;
    }

    private static Vector4 Lighten(Vector4 color)
        => new(MathF.Min(color.X * 1.2f, 1.0f), MathF.Min(color.Y * 1.2f, 1.0f), MathF.Min(color.Z * 1.2f, 1.0f), color.W);
}

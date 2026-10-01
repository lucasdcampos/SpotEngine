using System.Numerics;
using ImGuiNET;
using Spot.DebugUI.UI;
using Spot.DebugUI.Undo;

namespace Spot.Editor.Panels;

/// <summary>
/// Shows the undo history as a list and lets the user jump to any point in it. Where the Edit menu
/// says what one <c>Ctrl</c>+<c>Z</c> would take back, this shows the whole sequence, which is what
/// turns undo from something you hope works into something you can see the state of.
/// </summary>
public sealed class HistoryPanel(UndoHistory history)
{
    // The entry the keyboard is on, as a cursor position (0 = the base state, before any action).
    private int _keyboardTarget = -1;

    /// <summary>Draws the panel. <paramref name="open"/> is cleared by the window's close button.</summary>
    public void OnImGuiRender(ref bool open)
    {
        if (!open)
        {
            return;
        }

        if (!ImGui.Begin($"{EditorIcons.Rotate}  History", ref open))
        {
            ImGui.End();
            return;
        }

        var palette = EditorThemeManager.Current.Palette;
        int count = history.Count;
        int cursor = history.Cursor;

        if (_keyboardTarget < 0 || _keyboardTarget > count)
        {
            _keyboardTarget = cursor;
        }

        HandleShortcuts(count);

        if (ImGui.BeginChild("##entries", new Vector2(0.0f, -ImGui.GetFrameHeightWithSpacing() - 4.0f)))
        {
            // Newest first, so the action just performed is where the eye already is.
            for (int i = count - 1; i >= 0; i--)
            {
                DrawRow(i + 1, history.LabelAt(i) ?? "(unnamed)", cursor, palette);
            }

            // The base state: jumping here undoes everything the history still holds.
            DrawRow(0, "Opened", cursor, palette);
        }

        ImGui.EndChild();

        ImGui.Separator();
        DrawFooter(count, palette);
        ImGui.End();
    }

    /// <summary>
    /// How many scene changes the editor's catch-all had to record because no specific action claimed
    /// them. Surfaced here as the signal that a mutation site still needs migrating; zero in a normal
    /// session means every edit produced a precise, well-named entry.
    /// </summary>
    public int UnattributedChanges { get; set; }

    // One row. `target` is the cursor position this row represents, so clicking it jumps there.
    private void DrawRow(int target, string label, int cursor, EditorPalette palette)
    {
        bool isCurrent = target == cursor;
        bool isFuture = target > cursor;

        // Undone entries stay visible but dimmed: they are the redo branch, and seeing it is half the
        // reason for having this panel.
        if (isFuture)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, palette.TextDisabled);
        }
        else if (isCurrent)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, palette.Text);
        }
        else
        {
            ImGui.PushStyleColor(
                ImGuiCol.Text, new Vector4(palette.Text.X, palette.Text.Y, palette.Text.Z, 0.85f));
        }

        string prefix = isCurrent ? EditorIcons.Play : " ";
        if (ImGui.Selectable($"{prefix} {label}##h{target}", isCurrent || target == _keyboardTarget))
        {
            history.JumpTo(target);
            _keyboardTarget = target;
        }

        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(isCurrent
                ? "Current state"
                : isFuture ? $"Redo up to \"{label}\"" : $"Undo back to just after \"{label}\"");
        }
    }

    private void DrawFooter(int count, EditorPalette palette)
    {
        ImGui.TextDisabled($"{count} action{(count == 1 ? "" : "s")} · {FormatBytes(history.TotalBytes)}");

        if (UnattributedChanges > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(
                palette.LogError,
                $"· {UnattributedChanges} generic");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(
                    "Scene changes recorded as whole-scene \"Scene Change\" snapshots because no\n"
                    + "specific action claimed them. They are undoable, but the entry is coarse and\n"
                    + "unnamed — each one is a mutation site still worth routing through a real action.");
            }
        }

        ImGui.SameLine(ImGui.GetContentRegionMax().X - 90.0f);
        if (ImGui.Button("Clear", new Vector2(80.0f, 0.0f)))
        {
            history.Clear();
            _keyboardTarget = 0;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Discards the history. The scene itself is left exactly as it is now.");
        }
    }

    // Up/Down move a highlight through the list; Enter jumps to it. Only while this window is focused,
    // and never while a text field is being typed into.
    private void HandleShortcuts(int count)
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || ImGui.GetIO().WantTextInput)
        {
            return;
        }

        // Up moves toward newer entries, matching the newest-first list order.
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true))
        {
            _keyboardTarget = Math.Min(count, _keyboardTarget + 1);
        }

        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true))
        {
            _keyboardTarget = Math.Max(0, _keyboardTarget - 1);
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false))
        {
            history.JumpTo(_keyboardTarget);
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };
}

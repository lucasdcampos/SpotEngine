using System;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using Spot.DebugUI.UI;
using Spot.Editor.Utils;
using static Spot.Editor.Launcher.LauncherColors;
using static Spot.Editor.Launcher.LauncherWidgets;

namespace Spot.Editor.Launcher;

// The "New Project" dialog: a name, a parent folder, a preview of the folder it creates, and validation that
// keeps it from scaffolding over an existing folder.
public partial class LauncherScene
{
    private const string NewProjectPopupId = "##newProject";
    private const float DialogWidth = 480.0f;

    private bool _openNewProject;
    private bool _focusProjectName;
    private string _newProjectName = "MyProject";
    private string _newProjectLocation = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    // Validation touches the disk, so it is recomputed only when the inputs change.
    private string? _validatedKey;
    private string? _validationMessage;
    private bool _validationBlocks;

    private void OpenNewProjectDialog()
    {
        _openNewProject = true;
        _newProjectName = SuggestProjectName(_newProjectName, _newProjectLocation);
    }

    // Keeps the proposed name while its folder is free; otherwise numbers it ("MyProject2", "MyProject3", ...).
    private static string SuggestProjectName(string current, string location)
    {
        string name = string.IsNullOrWhiteSpace(current) ? "MyProject" : current.Trim();
        try
        {
            if (!Directory.Exists(Path.Combine(location, name))) return name;
            string stem = name.TrimEnd("0123456789".ToCharArray());
            if (stem.Length == 0) stem = "MyProject";
            for (int n = 2; n < 100; n++)
            {
                string candidate = stem + n;
                if (!Directory.Exists(Path.Combine(location, candidate))) return candidate;
            }
        }
        catch { /* an unreadable location is reported by the dialog's validation */ }
        return name;
    }

    private void DrawNewProjectModal(in LauncherColors c)
    {
        if (_openNewProject)
        {
            ImGui.OpenPopup(NewProjectPopupId);
            _openNewProject = false;
            _focusProjectName = true;
        }

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(DialogWidth, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Space.Xl, Space.Xl));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, LauncherLayout.CardRounding);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, WithAlpha(EditorThemeManager.Current.Palette.PopupBg, 1.0f));
        bool open = true; // no title bar, so no close button; Esc and Cancel close it
        bool visible = ImGui.BeginPopupModal(NewProjectPopupId, ref open,
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
        if (!visible) return;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Space.Md, (LauncherLayout.ControlHeight - ImGui.GetFontSize()) * 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, LauncherLayout.ControlRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.0f);
        ImGui.PushStyleColor(ImGuiCol.Border, c.Hairline);

        float width = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();

        Vector2 p = ImGui.GetCursorScreenPos();
        Text(dl, EditorFonts.Heading, p, c.Text, "New Project");
        Text(dl, EditorFonts.Body, p + new Vector2(0, EditorFonts.Heading.FontSize + Space.Xs), c.TextMuted,
            "Name your project and choose where to keep it.");
        ImGui.Dummy(new Vector2(width, EditorFonts.Heading.FontSize + Space.Xs + EditorFonts.Body.FontSize + Space.Xl));

        FieldLabel(dl, c, "Name");
        if (_focusProjectName)
        {
            ImGui.SetKeyboardFocusHere();
            _focusProjectName = false;
        }
        ImGui.SetNextItemWidth(width);
        ImGui.InputText("##projectName", ref _newProjectName, 128);
        ImGui.Dummy(new Vector2(0, Space.Lg));

        FieldLabel(dl, c, "Location");
        float browse = LauncherLayout.ControlHeight;
        ImGui.SetNextItemWidth(width - browse - Space.Sm);
        ImGui.InputText("##projectLocation", ref _newProjectLocation, 260);
        ImGui.SameLine(0, Space.Sm);
        if (Button("##browseLocation", EditorIcons.FolderOpen, string.Empty, new Vector2(browse, browse), ButtonKind.Secondary, c,
                "Choose a folder"))
        {
            string? folder = FileDialogs.SelectFolder();
            if (folder != null) _newProjectLocation = folder;
        }
        ImGui.Dummy(new Vector2(0, Space.Md));

        // Where the project lands, or why it can't be created there.
        string name = _newProjectName.Trim();
        string location = _newProjectLocation.Trim();
        Validate(name, location);
        ImFontPtr small = EditorFonts.Small;
        Vector2 hintPos = ImGui.GetCursorScreenPos();
        if (_validationMessage != null)
        {
            Text(dl, small, hintPos, _validationBlocks ? c.Warning : c.TextMuted, _validationMessage);
        }
        else if (name.Length > 0 && location.Length > 0)
        {
            Text(dl, small, hintPos, c.TextMuted, EllipsizeStart(small, $"Creates {Path.Combine(location, name)}", width));
        }
        ImGui.Dummy(new Vector2(width, small.FontSize + Space.Xl));

        bool canCreate = name.Length > 0 && location.Length > 0 && !_validationBlocks;
        var buttonSize = new Vector2(104, LauncherLayout.ButtonHeight - 2);
        float buttonsX = ImGui.GetCursorScreenPos().X + width - buttonSize.X * 2 - Space.Sm;
        float buttonsY = ImGui.GetCursorScreenPos().Y;
        ImGui.SetCursorScreenPos(new Vector2(buttonsX, buttonsY));
        bool cancel = Button("##cancel", null, "Cancel", buttonSize, ButtonKind.Secondary, c, "Esc");
        ImGui.SetCursorScreenPos(new Vector2(buttonsX + buttonSize.X + Space.Sm, buttonsY));
        bool create = Button("##create", null, "Create", buttonSize, ButtonKind.Primary, c, "Enter", canCreate);

        create |= canCreate && (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false));
        cancel |= ImGui.IsKeyPressed(ImGuiKey.Escape, false);

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(4);

        if (create)
        {
            ImGui.CloseCurrentPopup();
            StartCreate(name, location);
        }
        else if (cancel)
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private static void FieldLabel(ImDrawListPtr dl, in LauncherColors c, string text)
    {
        ImFontPtr small = EditorFonts.Small;
        Text(dl, small, ImGui.GetCursorScreenPos(), c.TextMuted, text);
        ImGui.Dummy(new Vector2(0, small.FontSize + Space.Sm - 2.0f));
    }

    // Catches what would fail or do damage before the user commits: a name the file system rejects, or a target
    // folder that already has files in it (scaffolding would overwrite its project file and start scene).
    private void Validate(string name, string location)
    {
        string key = name + "\n" + location;
        if (key == _validatedKey) return;
        _validatedKey = key;
        _validationMessage = null;
        _validationBlocks = false;

        if (name.Length == 0 || location.Length == 0) return;

        char[] invalid = Path.GetInvalidFileNameChars();
        char bad = name.FirstOrDefault(ch => invalid.Contains(ch));
        if (bad != default)
        {
            _validationMessage = $"A project name can't contain '{bad}'.";
            _validationBlocks = true;
            return;
        }

        try
        {
            string target = Path.Combine(location, name);
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            {
                _validationMessage = $"A folder named \"{name}\" already exists here.";
                _validationBlocks = true;
            }
        }
        catch
        {
            _validationMessage = "That location can't be read.";
            _validationBlocks = true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Spot.Animation;
using Spot.DebugUI.UI;

namespace Spot.Editor.Panels;

/// <summary>
/// A visual editor for an <see cref="AnimatorController"/> asset: a pannable/zoomable node graph of states and
/// transitions on the left, and a context inspector (parameters, the selected state, or the selected
/// transition) on the right. Drawn as its own dockable window per open ".sptcontroller", it edits the same
/// cached controller instance the runtime loads and saves changes back to disk once edits settle — mirroring
/// how the material and prefab editors persist.
/// </summary>
public sealed class AnimatorControllerPanel
{
    private const float NodeWidth = 150.0f;
    private const float NodeHeight = 46.0f;

    private readonly string _path;
    private readonly AnimatorController _controller;

    private Vector2 _pan;
    private float _zoom = 1.0f;

    // Pseudo-node positions (graph space) for the Entry and Any-State anchors. Not persisted in the asset.
    private Vector2 _entryPos = new(20.0f, 20.0f);
    private Vector2 _anyPos = new(20.0f, 110.0f);

    private int _selectedState = -1;
    private int _selectedTransition = -1;

    // Transition-authoring: when linking, the source of the pending edge (a state index, or the Any-State anchor).
    private int _linkFromState = -1;
    private bool _linkFromAny;

    private bool _dirty;

    // Clip picker state: every animation clip found across the project's model files (name + source file name
    // + source full path), rebuilt when the picker opens, plus its search box.
    private readonly List<(string Clip, string Source, string Path)> _projectClips = new();
    private string _clipSearch = string.Empty;

    /// <summary>Gets the controller file this editor targets.</summary>
    public string Path => _path;

    /// <summary>Creates an editor for the controller at <paramref name="path"/>, loading (or reusing) the asset.</summary>
    /// <param name="path">The ".sptcontroller" file to edit.</param>
    public AnimatorControllerPanel(string path)
    {
        _path = path;
        _controller = AnimatorController.Load(path);
    }

    /// <summary>Draws the editor window; <paramref name="open"/> is cleared when its close button is pressed.</summary>
    /// <param name="open">The window's visibility, driven by the title-bar close button.</param>
    public void OnImGuiRender(ref bool open)
    {
        string title = $"{System.IO.Path.GetFileNameWithoutExtension(_path)}###AnimCtrl_{_path}";
        ImGui.SetNextWindowSize(new Vector2(900, 560), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin(title, ref open, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        const float inspectorWidth = 300.0f;
        float canvasWidth = MathF.Max(120.0f, ImGui.GetContentRegionAvail().X - inspectorWidth);

        DrawCanvas(new Vector2(canvasWidth, 0.0f));
        ImGui.SameLine();
        DrawInspector(new Vector2(0.0f, 0.0f));

        // Persist edits once the user stops interacting, so we don't hit the disk every frame mid-drag/typing.
        if (_dirty && !ImGui.IsAnyItemActive() && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            try
            {
                _controller.Save(_path);
            }
            catch (Exception ex)
            {
                Spot.Core.Log.Error("Failed to save animator controller '{0}': {1}", _path, ex.Message);
            }

            _dirty = false;
        }

        ImGui.End();
    }

    // ----- Graph canvas ----------------------------------------------------------------------------

    private void DrawCanvas(Vector2 size)
    {
        var palette = EditorThemeManager.Current.Palette;
        ImGui.BeginChild("graph", size, ImGuiChildFlags.Border);

        Vector2 origin = ImGui.GetCursorScreenPos();
        Vector2 avail = ImGui.GetContentRegionAvail();
        ImDrawListPtr dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(origin, origin + avail, ImGui.GetColorU32(new Vector4(0.10f, 0.11f, 0.13f, 1.0f)));
        DrawGrid(dl, origin, avail);

        bool canvasHovered = ImGui.IsWindowHovered();

        // Zoom on wheel, pan on middle-drag — the conventional node-editor navigation.
        ImGuiIOPtr io = ImGui.GetIO();
        if (canvasHovered && io.MouseWheel != 0.0f)
        {
            _zoom = Math.Clamp(_zoom + io.MouseWheel * 0.1f, 0.5f, 1.6f);
        }

        if (canvasHovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
        {
            _pan += io.MouseDelta;
        }

        // Transitions first so nodes render over them; collect edges for click selection afterwards.
        DrawTransitions(dl, origin, palette);

        bool anyNodeHovered = false;
        DrawAnchors(dl, origin, palette, ref anyNodeHovered);
        DrawStateNodes(dl, origin, palette, ref anyNodeHovered);

        // While linking, rubber-band a line from the source to the cursor.
        if (_linkFromState >= 0 || _linkFromAny)
        {
            Vector2 from = _linkFromAny ? Center(origin, _anyPos) : Center(origin, StatePos(_controller.States[_linkFromState]));
            dl.AddLine(from, io.MousePos, ImGui.GetColorU32(palette.Accent), 2.0f);
        }

        // A click on empty canvas clears the selection and cancels any pending link.
        if (canvasHovered && !anyNodeHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _selectedState = -1;
            _selectedTransition = -1;
            _linkFromState = -1;
            _linkFromAny = false;
        }

        DrawCanvasContextMenu(origin);

        ImGui.EndChild();
    }

    private void DrawGrid(ImDrawListPtr dl, Vector2 origin, Vector2 size)
    {
        uint line = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.04f));
        float step = 32.0f * _zoom;
        for (float x = _pan.X % step; x < size.X; x += step)
        {
            dl.AddLine(origin + new Vector2(x, 0.0f), origin + new Vector2(x, size.Y), line);
        }

        for (float y = _pan.Y % step; y < size.Y; y += step)
        {
            dl.AddLine(origin + new Vector2(0.0f, y), origin + new Vector2(size.X, y), line);
        }
    }

    private void DrawStateNodes(ImDrawListPtr dl, Vector2 origin, EditorPalette palette, ref bool anyHovered)
    {
        Vector2 nodeSize = new Vector2(NodeWidth, NodeHeight) * _zoom;
        ImGuiIOPtr io = ImGui.GetIO();

        for (int i = 0; i < _controller.States.Count; i++)
        {
            AnimatorState state = _controller.States[i];
            Vector2 pos = StatePos(state, origin);

            ImGui.PushID(i);
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton("node", nodeSize);
            bool hovered = ImGui.IsItemHovered();
            anyHovered |= hovered;

            if (ImGui.IsItemActive() && io.MouseDelta != Vector2.Zero)
            {
                state.EditorX += io.MouseDelta.X / _zoom;
                state.EditorY += io.MouseDelta.Y / _zoom;
                _dirty = true;
            }

            if (ImGui.IsItemActivated())
            {
                if (_linkFromState >= 0 || _linkFromAny)
                {
                    CompleteLink(i);
                }
                else
                {
                    _selectedState = i;
                    _selectedTransition = -1;
                }
            }

            DrawStateContextMenu(i);
            ImGui.PopID();

            bool isDefault = string.Equals(state.Name, _controller.DefaultState, StringComparison.Ordinal);
            uint fill = ImGui.GetColorU32(_selectedState == i ? WithAlpha(palette.Accent, 0.35f)
                : hovered ? palette.FrameBgHovered : WithAlpha(palette.FrameBg, 0.95f));
            uint border = ImGui.GetColorU32(isDefault ? new Vector4(0.40f, 0.85f, 0.45f, 1.0f) : palette.Accent);

            dl.AddRectFilled(pos, pos + nodeSize, fill, 5.0f);
            dl.AddRect(pos, pos + nodeSize, border, 5.0f, ImDrawFlags.None, isDefault ? 2.5f : 1.5f);
            CenteredText(dl, pos, nodeSize, string.IsNullOrEmpty(state.Name) ? "(unnamed)" : state.Name, palette.Text);
        }
    }

    private void DrawAnchors(ImDrawListPtr dl, Vector2 origin, EditorPalette palette, ref bool anyHovered)
    {
        Vector2 anchorSize = new Vector2(NodeWidth, NodeHeight * 0.7f) * _zoom;
        ImGuiIOPtr io = ImGui.GetIO();

        // Entry anchor: a green pill that always points at the default state.
        Vector2 entryScreen = origin + _pan + _entryPos * _zoom;
        ImGui.PushID("entry");
        ImGui.SetCursorScreenPos(entryScreen);
        ImGui.InvisibleButton("entry", anchorSize);
        anyHovered |= ImGui.IsItemHovered();
        if (ImGui.IsItemActive() && io.MouseDelta != Vector2.Zero)
        {
            _entryPos += io.MouseDelta / _zoom;
        }

        ImGui.PopID();
        dl.AddRectFilled(entryScreen, entryScreen + anchorSize, ImGui.GetColorU32(new Vector4(0.18f, 0.42f, 0.22f, 1.0f)), 14.0f);
        CenteredText(dl, entryScreen, anchorSize, "Entry", palette.Text);

        // Any-State anchor: transitions from here can fire regardless of the current state.
        Vector2 anyScreen = origin + _pan + _anyPos * _zoom;
        ImGui.PushID("any");
        ImGui.SetCursorScreenPos(anyScreen);
        ImGui.InvisibleButton("any", anchorSize);
        bool anyHov = ImGui.IsItemHovered();
        anyHovered |= anyHov;
        if (ImGui.IsItemActive() && io.MouseDelta != Vector2.Zero)
        {
            _anyPos += io.MouseDelta / _zoom;
        }

        if (ImGui.IsItemActivated() && !(_linkFromState >= 0 || _linkFromAny))
        {
            _selectedState = -1;
            _selectedTransition = -1;
        }

        if (ImGui.BeginPopupContextItem("anyctx"))
        {
            if (ImGui.MenuItem("Make Transition"))
            {
                _linkFromAny = true;
                _linkFromState = -1;
            }

            ImGui.EndPopup();
        }

        ImGui.PopID();
        dl.AddRectFilled(anyScreen, anyScreen + anchorSize, ImGui.GetColorU32(new Vector4(0.20f, 0.28f, 0.42f, 1.0f)), 14.0f);
        CenteredText(dl, anyScreen, anchorSize, "Any State", palette.Text);

        // Entry -> default state connector.
        AnimatorState? def = _controller.FindState(_controller.DefaultState);
        if (def is not null)
        {
            DrawArrow(dl, entryScreen + anchorSize * 0.5f, Center(origin, StatePos(def)),
                ImGui.GetColorU32(new Vector4(0.40f, 0.85f, 0.45f, 0.9f)));
        }
    }

    private void DrawTransitions(ImDrawListPtr dl, Vector2 origin, EditorPalette palette)
    {
        Vector2 anyCenter = origin + _pan + (_anyPos + new Vector2(NodeWidth, NodeHeight * 0.7f) * 0.5f) * _zoom;

        for (int i = 0; i < _controller.Transitions.Count; i++)
        {
            AnimatorTransition transition = _controller.Transitions[i];
            AnimatorState? to = _controller.FindState(transition.To);
            if (to is null)
            {
                continue;
            }

            Vector2 from = transition.FromAnyState ? anyCenter : StateCenterOrDefault(origin, transition.From);
            Vector2 target = Center(origin, StatePos(to));
            uint color = ImGui.GetColorU32(_selectedTransition == i ? palette.Accent : new Vector4(0.7f, 0.7f, 0.75f, 0.9f));
            DrawArrow(dl, from, target, color);

            // A small clickable knob at the midpoint selects (or, via its menu, deletes) the transition.
            Vector2 mid = (from + target) * 0.5f;
            ImGui.PushID(1000 + i);
            ImGui.SetCursorScreenPos(mid - new Vector2(7.0f, 7.0f));
            ImGui.InvisibleButton("edge", new Vector2(14.0f, 14.0f));
            if (ImGui.IsItemActivated())
            {
                _selectedTransition = i;
                _selectedState = -1;
            }

            if (ImGui.BeginPopupContextItem("edgectx"))
            {
                if (ImGui.MenuItem("Delete Transition"))
                {
                    _controller.Transitions.RemoveAt(i);
                    _selectedTransition = -1;
                    _dirty = true;
                    ImGui.EndPopup();
                    ImGui.PopID();
                    return;
                }

                ImGui.EndPopup();
            }

            ImGui.PopID();
            dl.AddCircleFilled(mid, 5.0f, color);
        }
    }

    private void DrawStateContextMenu(int index)
    {
        if (!ImGui.BeginPopupContextItem("statectx"))
        {
            return;
        }

        AnimatorState state = _controller.States[index];
        if (ImGui.MenuItem("Make Transition"))
        {
            _linkFromState = index;
            _linkFromAny = false;
        }

        if (ImGui.MenuItem("Set as Default"))
        {
            _controller.DefaultState = state.Name;
            _dirty = true;
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Delete State"))
        {
            _controller.Transitions.RemoveAll(t =>
                string.Equals(t.From, state.Name, StringComparison.Ordinal) ||
                string.Equals(t.To, state.Name, StringComparison.Ordinal));
            _controller.States.RemoveAt(index);
            _selectedState = -1;
            _dirty = true;
        }

        ImGui.EndPopup();
    }

    private void DrawCanvasContextMenu(Vector2 origin)
    {
        if (!ImGui.BeginPopupContextWindow("canvasctx", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            return;
        }

        if (ImGui.MenuItem("Create State"))
        {
            // Drop the new node where the menu was opened, in graph space.
            Vector2 graph = (ImGui.GetMousePosOnOpeningCurrentPopup() - origin - _pan) / _zoom;
            var state = new AnimatorState
            {
                Name = UniqueStateName("New State"),
                EditorX = graph.X,
                EditorY = graph.Y,
            };
            _controller.States.Add(state);
            if (string.IsNullOrEmpty(_controller.DefaultState))
            {
                _controller.DefaultState = state.Name;
            }

            _selectedState = _controller.States.Count - 1;
            _dirty = true;
        }

        ImGui.EndPopup();
    }

    // ----- Inspector -------------------------------------------------------------------------------

    private void DrawInspector(Vector2 size)
    {
        ImGui.BeginChild("inspector", size, ImGuiChildFlags.Border);

        if (ImGui.CollapsingHeader("Parameters", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawParameters();
        }

        ImGui.Separator();

        if (_selectedTransition >= 0 && _selectedTransition < _controller.Transitions.Count)
        {
            DrawTransitionInspector(_controller.Transitions[_selectedTransition]);
        }
        else if (_selectedState >= 0 && _selectedState < _controller.States.Count)
        {
            DrawStateInspector(_controller.States[_selectedState]);
        }
        else
        {
            ImGui.TextDisabled("Select a state or transition,");
            ImGui.TextDisabled("or right-click the canvas to add one.");
        }

        ImGui.EndChild();
    }

    private void DrawParameters()
    {
        string[] typeNames = Enum.GetNames(typeof(AnimatorParameterType));
        for (int i = 0; i < _controller.Parameters.Count; i++)
        {
            AnimatorParameter parameter = _controller.Parameters[i];
            ImGui.PushID(i);

            string name = parameter.Name;
            if (EditorGui.InputText("Name", ref name)) { parameter.Name = name; _dirty = true; }

            int type = (int)parameter.Type;
            if (EditorGui.Combo("Type", ref type, typeNames)) { parameter.Type = (AnimatorParameterType)type; _dirty = true; }

            switch (parameter.Type)
            {
                case AnimatorParameterType.Float:
                    float f = parameter.DefaultValue;
                    if (EditorGui.DragFloat("Default", ref f)) { parameter.DefaultValue = f; _dirty = true; }
                    break;
                case AnimatorParameterType.Int:
                    float iv = parameter.DefaultValue;
                    if (EditorGui.DragFloat("Default", ref iv, 1.0f, 0.0f, 0.0f, "%.0f")) { parameter.DefaultValue = MathF.Round(iv); _dirty = true; }
                    break;
                case AnimatorParameterType.Bool:
                    bool b = parameter.DefaultValue != 0.0f;
                    if (EditorGui.Checkbox("Default", ref b)) { parameter.DefaultValue = b ? 1.0f : 0.0f; _dirty = true; }
                    break;
            }

            if (ImGui.SmallButton("Remove")) { _controller.Parameters.RemoveAt(i); _dirty = true; ImGui.PopID(); break; }
            ImGui.Separator();
            ImGui.PopID();
        }

        if (ImGui.Button("Add Parameter", new Vector2(-1.0f, 0.0f)))
        {
            _controller.Parameters.Add(new AnimatorParameter { Name = UniqueParameterName("Param"), Type = AnimatorParameterType.Float });
            _dirty = true;
        }
    }

    private void DrawStateInspector(AnimatorState state)
    {
        ImGui.TextDisabled("State");
        ImGui.Separator();

        string name = state.Name;
        if (EditorGui.InputText("Name", ref name))
        {
            // Keep transitions and the default pointer wired to the renamed state.
            RenameState(state, name);
            _dirty = true;
        }

        DrawClipField(state);

        float speed = state.Speed;
        if (EditorGui.DragFloat("Speed", ref speed, 0.01f, 0.0f, 100.0f)) { state.Speed = speed; _dirty = true; }

        bool loop = state.Loop;
        if (EditorGui.Checkbox("Loop", ref loop)) { state.Loop = loop; _dirty = true; }

        if (!string.Equals(state.Name, _controller.DefaultState, StringComparison.Ordinal))
        {
            if (ImGui.Button("Set as Default", new Vector2(-1.0f, 0.0f))) { _controller.DefaultState = state.Name; _dirty = true; }
        }
        else
        {
            ImGui.TextDisabled("Default state");
        }
    }

    private void DrawTransitionInspector(AnimatorTransition transition)
    {
        ImGui.TextDisabled("Transition");
        ImGui.Separator();

        string source = transition.FromAnyState ? "Any State" : (string.IsNullOrEmpty(transition.From) ? "(none)" : transition.From);
        ImGui.TextUnformatted($"{source}  ->  {(string.IsNullOrEmpty(transition.To) ? "(none)" : transition.To)}");
        ImGui.Spacing();

        bool hasExit = transition.HasExitTime;
        if (EditorGui.Checkbox("Has Exit Time", ref hasExit)) { transition.HasExitTime = hasExit; _dirty = true; }
        if (transition.HasExitTime)
        {
            float exit = transition.ExitTime;
            if (EditorGui.DragFloat("Exit Time", ref exit, 0.01f, 0.0f, 1.0f)) { transition.ExitTime = exit; _dirty = true; }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Conditions");
        ImGui.Separator();

        string[] paramNames = _controller.Parameters.Select(p => p.Name).ToArray();
        string[] modeNames = Enum.GetNames(typeof(AnimatorConditionMode));

        for (int i = 0; i < transition.Conditions.Count; i++)
        {
            AnimatorCondition condition = transition.Conditions[i];
            ImGui.PushID(i);

            int paramIndex = Array.IndexOf(paramNames, condition.Parameter);
            if (paramNames.Length > 0)
            {
                if (paramIndex < 0) paramIndex = 0;
                if (EditorGui.Combo("Param", ref paramIndex, paramNames)) { condition.Parameter = paramNames[paramIndex]; _dirty = true; }
            }
            else
            {
                ImGui.TextDisabled("Add a parameter first.");
            }

            // Triggers pass simply on being set; other types compare against a threshold.
            if (_controller.FindParameter(condition.Parameter)?.Type != AnimatorParameterType.Trigger)
            {
                int mode = (int)condition.Mode;
                if (EditorGui.Combo("Mode", ref mode, modeNames)) { condition.Mode = (AnimatorConditionMode)mode; _dirty = true; }
                float threshold = condition.Threshold;
                if (EditorGui.DragFloat("Value", ref threshold)) { condition.Threshold = threshold; _dirty = true; }
            }

            if (ImGui.SmallButton("Remove")) { transition.Conditions.RemoveAt(i); _dirty = true; ImGui.PopID(); break; }
            ImGui.Separator();
            ImGui.PopID();
        }

        if (ImGui.Button("Add Condition", new Vector2(-1.0f, 0.0f)))
        {
            string first = _controller.Parameters.Count > 0 ? _controller.Parameters[0].Name : string.Empty;
            transition.Conditions.Add(new AnimatorCondition { Parameter = first });
            _dirty = true;
        }

        ImGui.Spacing();
        if (ImGui.Button("Delete Transition", new Vector2(-1.0f, 0.0f)))
        {
            _controller.Transitions.Remove(transition);
            _selectedTransition = -1;
            _dirty = true;
        }
    }

    // ----- Helpers ---------------------------------------------------------------------------------

    private void CompleteLink(int toState)
    {
        string to = _controller.States[toState].Name;
        if (_linkFromAny)
        {
            _controller.Transitions.Add(new AnimatorTransition { FromAnyState = true, To = to });
        }
        else if (_linkFromState >= 0 && _linkFromState != toState)
        {
            _controller.Transitions.Add(new AnimatorTransition { From = _controller.States[_linkFromState].Name, To = to });
        }

        _linkFromState = -1;
        _linkFromAny = false;
        _selectedTransition = _controller.Transitions.Count - 1;
        _selectedState = -1;
        _dirty = true;
    }

    private void RenameState(AnimatorState state, string newName)
    {
        string old = state.Name;
        state.Name = newName;
        if (string.Equals(_controller.DefaultState, old, StringComparison.Ordinal))
        {
            _controller.DefaultState = newName;
        }

        foreach (AnimatorTransition transition in _controller.Transitions)
        {
            if (string.Equals(transition.From, old, StringComparison.Ordinal)) transition.From = newName;
            if (string.Equals(transition.To, old, StringComparison.Ordinal)) transition.To = newName;
        }
    }

    // A clip selector: a full-width button showing the current clip, a project-wide picker on click, and a
    // drop target that accepts an animation/model file (assigning its clip). Clips are strings resolved by
    // name at runtime, so any project model that shares the skeleton is a valid source.
    private void DrawClipField(AnimatorState state)
    {
        var palette = EditorThemeManager.Current.Palette;
        ImGui.PushID("clip");
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Clip");
        ImGui.SameLine(90.0f);

        bool hasValue = !string.IsNullOrEmpty(state.Clip);
        float clearWidth = hasValue ? ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X : 0.0f;
        float width = MathF.Max(40.0f, ImGui.GetContentRegionAvail().X - clearWidth);

        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.0f, 0.5f));
        if (!hasValue) ImGui.PushStyleColor(ImGuiCol.Text, palette.TextDisabled);
        bool clicked = ImGui.Button((hasValue ? state.Clip : "None") + "##clipbtn", new Vector2(width, 0.0f));
        if (!hasValue) ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        // Drag an animation/model file from the Asset Browser onto the field to assign its clip.
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("MODEL_FILE");
                if (payload.NativePtr != null)
                {
                    string? file = Marshal.PtrToStringUTF8(payload.Data);
                    if (!string.IsNullOrEmpty(file)) AssignClipFromFile(state, file);
                }
            }

            ImGui.EndDragDropTarget();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to pick a clip, or drag an animation file here");
        }

        if (hasValue)
        {
            ImGui.SameLine();
            if (ImGui.Button(EditorIcons.Times + "##clipclear", new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight())))
            {
                state.Clip = string.Empty;
                _dirty = true;
            }
        }

        if (clicked)
        {
            _clipSearch = string.Empty;
            RefreshProjectClips();
            ImGui.OpenPopup("SelectClip");
        }

        DrawClipPopup(state);
        ImGui.PopID();
    }

    private void DrawClipPopup(AnimatorState state)
    {
        if (!ImGui.BeginPopup("SelectClip"))
        {
            return;
        }

        ImGui.SetNextItemWidth(300.0f);
        ImGui.InputTextWithHint("##clipsearch", "Search clips...", ref _clipSearch, 128);
        ImGui.Separator();

        ImGui.BeginChild("cliplist", new Vector2(300.0f, 320.0f), ImGuiChildFlags.None);
        if (_projectClips.Count == 0)
        {
            ImGui.TextDisabled("No clips found. Import a rigged model");
            ImGui.TextDisabled("or animation file into the project.");
        }

        foreach ((string clip, string source, string path) in _projectClips)
        {
            if (!string.IsNullOrEmpty(_clipSearch) && !clip.Contains(_clipSearch, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool selected = string.Equals(clip, state.Clip, StringComparison.Ordinal);
            if (ImGui.Selectable($"{clip}##{source}", selected))
            {
                // The state records both the clip name and the file that provides it, so the runtime loads it.
                state.Clip = clip;
                state.ClipSource = ClipRef(path);
                _dirty = true;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(180.0f);
            ImGui.TextDisabled(source);
        }

        ImGui.EndChild();
        ImGui.EndPopup();
    }

    // Loads a dropped model/animation file, assigns its (first) clip to the state along with the file the clip
    // comes from, so the runtime can load its data.
    private void AssignClipFromFile(AnimatorState state, string file)
    {
        try
        {
            Spot.Assets.Model model = Spot.Assets.Model.Load(file);
            string? first = model.Animations.Select(a => a.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n));
            if (first is null)
            {
                Spot.Core.Log.CoreWarn("'{0}' has no animation clips.", System.IO.Path.GetFileName(file));
                return;
            }

            state.Clip = first;
            state.ClipSource = ClipRef(file);
            _dirty = true;
        }
        catch (Exception ex)
        {
            Spot.Core.Log.Error("Animator editor could not read clips from '{0}': {1}", file, ex.Message);
        }
    }

    // A portable reference to a clip's source file: a guid reference when the project has indexed it, else a
    // project-relative path.
    private static string ClipRef(string file) =>
        Spot.Assets.AssetDatabase.ToGuidRef(file) ?? Spot.Assets.AssetPath.MakeRelative(file);

    // Scans every model/animation file under the project's Assets folder and collects the clip names they
    // expose (de-duplicated by name). Model.Load is cached, and this only runs when the picker opens.
    private void RefreshProjectClips()
    {
        _projectClips.Clear();
        string? root = Spot.Core.Project.Active?.GetAssetDirectory();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string[] extensions = { ".fbx", ".gltf", ".glb", ".dae", ".obj" };
        foreach (string file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!extensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant()))
            {
                continue;
            }

            try
            {
                Spot.Assets.Model model = Spot.Assets.Model.Load(file);
                foreach (AnimationClip clip in model.Animations)
                {
                    if (!string.IsNullOrEmpty(clip.Name) && seen.Add(clip.Name))
                    {
                        _projectClips.Add((clip.Name, System.IO.Path.GetFileName(file), file));
                    }
                }
            }
            catch
            {
                // A model that can't load just contributes no clips; the others still populate the list.
            }
        }

        _projectClips.Sort((a, b) => string.Compare(a.Clip, b.Clip, StringComparison.OrdinalIgnoreCase));
    }

    private string UniqueStateName(string baseName)
    {
        string candidate = baseName;
        for (int i = 1; _controller.FindState(candidate) != null; i++)
        {
            candidate = $"{baseName} {i}";
        }

        return candidate;
    }

    private string UniqueParameterName(string baseName)
    {
        string candidate = baseName;
        for (int i = 1; _controller.FindParameter(candidate) != null; i++)
        {
            candidate = $"{baseName} {i}";
        }

        return candidate;
    }

    private Vector2 StatePos(AnimatorState state, Vector2 origin) => origin + _pan + new Vector2(state.EditorX, state.EditorY) * _zoom;

    private Vector2 StatePos(AnimatorState state) => _pan + new Vector2(state.EditorX, state.EditorY) * _zoom;

    private Vector2 Center(Vector2 origin, Vector2 graphPosPlusPan)
    {
        // graphPosPlusPan already includes _pan (from StatePos(state)); add origin and half the node.
        return origin + graphPosPlusPan + new Vector2(NodeWidth, NodeHeight) * 0.5f * _zoom;
    }

    private Vector2 StateCenterOrDefault(Vector2 origin, string stateName)
    {
        AnimatorState? state = _controller.FindState(stateName);
        return state is not null ? Center(origin, StatePos(state)) : origin;
    }

    private static void DrawArrow(ImDrawListPtr dl, Vector2 from, Vector2 to, uint color)
    {
        dl.AddLine(from, to, color, 2.0f);

        Vector2 dir = to - from;
        float len = dir.Length();
        if (len < 1e-3f)
        {
            return;
        }

        dir /= len;
        Vector2 normal = new Vector2(-dir.Y, dir.X);
        Vector2 tip = to - dir * (NodeHeight * 0.5f);
        const float s = 8.0f;
        dl.AddTriangleFilled(tip, tip - dir * s + normal * s * 0.6f, tip - dir * s - normal * s * 0.6f, color);
    }

    private static void CenteredText(ImDrawListPtr dl, Vector2 pos, Vector2 boxSize, string text, Vector4 color)
    {
        Vector2 ts = ImGui.CalcTextSize(text);
        Vector2 at = pos + (boxSize - ts) * 0.5f;
        dl.AddText(at, ImGui.GetColorU32(color), text);
    }

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);
}

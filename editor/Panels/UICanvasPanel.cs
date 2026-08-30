using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Spot.Rendering;
using Spot.UI;

namespace Spot.Editor.Panels;

/// <summary>
/// The UI authoring surface: renders the open <c>.sptui</c> document with the real UI renderer into an
/// offscreen framebuffer and shows it as a screen-space canvas — a distinct view from the 3D scene viewport.
/// Widgets are clicked to select and dragged (with a lightweight move/resize gizmo) to lay out, writing back
/// to each widget's anchored rectangle. The canvas is the same coordinate space the game uses, so it is WYSIWYG.
///
/// Layout aids make assembly easy: an optional pixel grid with snapping, Figma-style smart alignment guides
/// that snap a widget's edges/center to its siblings and container, arrow-key nudging, and a live size readout.
/// Hold <c>Alt</c> while dragging to bypass all snapping.
/// </summary>
public sealed class UICanvasPanel : IDisposable
{
    private readonly EditorContext _context;
    private Framebuffer? _framebuffer;

    // The document this panel edits. Each open .sptui gets its own panel (and its own tab), so multiple can be
    // open at once — the panel operates on this document, not the shared "active" one.
    public UIRoot? Document;
    public string? DocumentPath;

    // The last displayed image origin (screen px), size (px), and the UI-unit → pixel scale, captured each
    // frame so hit-testing and the gizmo map between widget coordinates and the panel.
    private Vector2 _imageOrigin;
    private Vector2 _renderSize = new(16.0f, 16.0f);
    private float _scale = 1.0f;

    private Vector2 _canvasScreenSize = new Vector2(1920, 1080);
    private Vector2 _cameraPos = new Vector2(1920 * 0.5f, 1080 * 0.5f);
    private float _cameraZoom = 1.0f;


    // Layout-aid state, toggled from the toolbar. Grid size is in UI units (the document's design pixels).
    private bool _showGrid = true;
    private bool _gridSnap = true;
    private bool _smartGuides = true;
    private int _gridSize = 8;

    // Alignment guide lines to draw this frame (screen-space X / Y), set while snapping during a drag.
    private float? _guideX;
    private float? _guideY;

    // Reused across frames so gathering snap targets doesn't allocate each drag frame.
    private readonly List<float> _targetsX = new();
    private readonly List<float> _targetsY = new();

    private Handle _activeHandle = Handle.None;

    // The raw, un-snapped rectangle the drag is tracking (UI units). It always follows the mouse; snapping is
    // applied to a copy for display only, so a widget is never trapped on a guide — moving past the threshold
    // releases it (Unity/Figma-style magnetic snap, not a hard lock).
    private Vector2 _dragMin;
    private Vector2 _dragSize;

    private enum Handle { None, Move, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    private const float HandleReach = 7.0f;

    // How close (in screen pixels) an edge/center must be to a grid line or a sibling guide to snap to it.
    private const float SnapPixels = 6.0f;

    public UICanvasPanel(EditorContext context)
    {
        _context = context;
    }

    public void Dispose()
    {
        _framebuffer?.Dispose();
        _framebuffer = null;
    }

    /// <summary>Renders this panel's document into its offscreen framebuffer. Called during the editor's render pass.</summary>
    public void RenderDocument()
    {
        UIRoot? doc = Document;
        if (doc == null || _framebuffer == null) return;

        _framebuffer.Bind();
        Renderer.SetClearColor(0.11f, 0.11f, 0.13f, 1.0f);
        Renderer.Clear();
        Renderer.SetDepthTest(false);
        doc.Render(_framebuffer.Width, _framebuffer.Height);
        _framebuffer.Unbind();
    }

    /// <summary>
    /// Draws this document's canvas into the current window (the editor opens one window/tab per document). Only
    /// the active document (the one shown in the Hierarchy/Inspector) handles selection and gizmo input, so
    /// background tabs just render their picture.
    /// </summary>
    public void OnImGuiRender()
    {
        UIRoot? doc = Document;
        if (doc == null) return;

        bool active = ReferenceEquals(_context.EditingDocument, doc);

        DrawToolbar();

        Vector2 avail = ImGui.GetContentRegionAvail();
        if (avail.X < 4.0f || avail.Y < 4.0f) return;

        uint fbWidth = (uint)_canvasScreenSize.X;
        uint fbHeight = (uint)_canvasScreenSize.Y;

        _framebuffer ??= new Framebuffer(fbWidth, fbHeight);
        if (_framebuffer.Width != fbWidth || _framebuffer.Height != fbHeight)
        {
            _framebuffer.Resize(fbWidth, fbHeight);
        }
        _renderSize = _canvasScreenSize;

        // Lay the tree out at the display size so widget ScreenRects (UI units) match this frame's render.
        doc.LayoutForSize(_canvasScreenSize.X, _canvasScreenSize.Y);
        _scale = doc.Scale * _cameraZoom;

        // Invisible button to capture mouse events over the entire view
        Vector2 cursorScreenPos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##canvas", avail);
        bool hovered = ImGui.IsItemHovered();

        var io = ImGui.GetIO();
        if (hovered && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
        {
            _cameraPos -= io.MouseDelta / _cameraZoom;
        }
        if (hovered && io.MouseWheel != 0.0f)
        {
            Vector2 mouseLocal = io.MousePos - cursorScreenPos;
            Vector2 mouseCanvas = _cameraPos + (mouseLocal - avail * 0.5f) / _cameraZoom;
            
            _cameraZoom *= MathF.Pow(1.1f, io.MouseWheel);
            _cameraZoom = Math.Clamp(_cameraZoom, 0.1f, 10.0f);
            
            Vector2 mouseCanvasAfter = _cameraPos + (mouseLocal - avail * 0.5f) / _cameraZoom;
            _cameraPos -= (mouseCanvasAfter - mouseCanvas);
            _scale = doc.Scale * _cameraZoom; // Update scale after zoom
        }

        _imageOrigin = cursorScreenPos + avail * 0.5f - _cameraPos * _cameraZoom;
        Vector2 p_min = _imageOrigin;
        Vector2 p_max = _imageOrigin + _canvasScreenSize * _cameraZoom;

        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(cursorScreenPos, cursorScreenPos + avail, true);

        // Draw the Canvas background/limits
        drawList.AddImage((IntPtr)_framebuffer.ColorAttachment, p_min, p_max, new Vector2(0, 1), new Vector2(1, 0));
        
        // Canvas border delimitator
        drawList.AddRect(p_min, p_max, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.5f)), 0.0f, ImDrawFlags.None, 2.0f);

        // Outer dimming area
        uint dimColor = ImGui.GetColorU32(new Vector4(0.0f, 0.0f, 0.0f, 0.4f));
        Vector2 cMax = cursorScreenPos + avail;
        drawList.AddRectFilled(cursorScreenPos, new Vector2(cMax.X, p_min.Y), dimColor);
        drawList.AddRectFilled(new Vector2(cursorScreenPos.X, p_max.Y), cMax, dimColor);
        drawList.AddRectFilled(new Vector2(cursorScreenPos.X, MathF.Max(cursorScreenPos.Y, p_min.Y)), new Vector2(p_min.X, MathF.Min(cMax.Y, p_max.Y)), dimColor);
        drawList.AddRectFilled(new Vector2(p_max.X, MathF.Max(cursorScreenPos.Y, p_min.Y)), new Vector2(cMax.X, MathF.Min(cMax.Y, p_max.Y)), dimColor);

        if (_showGrid) DrawGrid(drawList, doc);

        _guideX = null;
        _guideY = null;

        if (active)
        {
            HandleInteraction(doc, hovered);
            HandleNudge();
            DrawGuides(drawList);
            DrawSelection(drawList);

            if (hovered && _activeHandle == Handle.None && ImGui.IsWindowFocused()
                && _context.SelectedWidget != null && ImGui.IsKeyPressed(ImGuiKey.Delete))
            {
                Widget sel = _context.SelectedWidget;
                sel.Parent?.Remove(sel);
                _context.SelectedWidget = null;
            }
        }

        drawList.PopClipRect();
    }

    private void DrawToolbar()
    {
        ImGui.Checkbox("Grid", ref _showGrid);
        ImGui.SameLine();
        ImGui.Checkbox("Snap", ref _gridSnap);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90.0f);
        if (ImGui.DragInt("##gridsize", ref _gridSize, 1.0f, 1, 256, "%d px")) _gridSize = Math.Clamp(_gridSize, 1, 256);
        ImGui.SameLine();
        ImGui.Checkbox("Guides", ref _smartGuides);
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Grid: show the pixel grid.\nSnap: snap moves/resizes to the grid.\nGuides: snap to sibling edges & centers.\nHold Alt while dragging to bypass snapping.\nArrow keys nudge (Shift = grid step).\nMiddle mouse to pan, Scroll to zoom.");
        
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120.0f);
        if (ImGui.DragFloat2("Screen Size", ref _canvasScreenSize, 1.0f, 128.0f, 4096.0f, "%.0f"))
        {
            _canvasScreenSize.X = MathF.Max(128.0f, MathF.Round(_canvasScreenSize.X));
            _canvasScreenSize.Y = MathF.Max(128.0f, MathF.Round(_canvasScreenSize.Y));
        }

        ImGui.Separator();
    }

    private void DrawGrid(ImDrawListPtr drawList, UIRoot doc)
    {
        float step = _gridSize * _scale;
        if (step < 4.0f) return; // too dense to be useful; skip

        uint color = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.05f));
        float width = MathF.Min(doc.Width * _scale, _renderSize.X * _cameraZoom);
        float height = MathF.Min(doc.Height * _scale, _renderSize.Y * _cameraZoom);

        for (float x = step; x < width; x += step)
            drawList.AddLine(new Vector2(_imageOrigin.X + x, _imageOrigin.Y), new Vector2(_imageOrigin.X + x, _imageOrigin.Y + height), color);
        for (float y = step; y < height; y += step)
            drawList.AddLine(new Vector2(_imageOrigin.X, _imageOrigin.Y + y), new Vector2(_imageOrigin.X + width, _imageOrigin.Y + y), color);
    }

    private void HandleInteraction(UIRoot doc, bool hovered)
    {
        Vector2 mouse = ImGui.GetIO().MousePos;

        if (_activeHandle != Handle.None)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _activeHandle = Handle.None;
            }
            else if (_context.SelectedWidget != null)
            {
                DragSelected(doc, ImGui.GetIO().MouseDelta / MathF.Max(_scale, 0.0001f));
            }
            return;
        }

        if (!hovered) return;

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            // Start a gizmo drag when the click lands on the selected widget's handles/body; otherwise pick.
            Widget? sel = _context.SelectedWidget;
            if (sel != null)
            {
                Handle handle = HandleAt(sel, mouse);
                if (handle != Handle.None)
                {
                    _activeHandle = handle;
                    BeginDrag(sel);
                    return;
                }
            }

            _context.SelectedWidget = Pick(doc, ToUI(mouse));
        }
    }

    // Arrow keys nudge the selected widget by one UI unit (Shift = one grid step), for precise placement.
    private void HandleNudge()
    {
        Widget? sel = _context.SelectedWidget;
        if (sel == null || _activeHandle != Handle.None) return;
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || ImGui.IsAnyItemActive()) return;

        float step = ImGui.GetIO().KeyShift ? MathF.Max(_gridSize, 1) : 1.0f;
        Vector2 nudge = Vector2.Zero;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow, true)) nudge.X -= step;
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow, true)) nudge.X += step;
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true)) nudge.Y -= step;
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true)) nudge.Y += step;

        if (nudge != Vector2.Zero)
        {
            UIRect r = sel.Rect;
            r.Position += nudge;
            sel.Rect = r;
        }
    }

    // Records the raw rectangle a drag starts from, so subsequent frames accumulate the true mouse motion
    // rather than re-reading the snapped result (which would trap the widget on a guide).
    private void BeginDrag(Widget w)
    {
        Vector4 sr = w.ScreenRect;
        _dragMin = new Vector2(sr.X, sr.Y);
        _dragSize = new Vector2(sr.Z, sr.W);
    }

    // Advances the raw drag rectangle by the frame's delta (UI units), then applies a snapped copy to the
    // widget. The raw rectangle follows the mouse exactly, so snapping guides the widget without locking it.
    private void DragSelected(UIRoot doc, Vector2 deltaUI)
    {
        Widget w = _context.SelectedWidget!;

        bool left = _activeHandle is Handle.Left or Handle.TopLeft or Handle.BottomLeft;
        bool right = _activeHandle is Handle.Right or Handle.TopRight or Handle.BottomRight;
        bool top = _activeHandle is Handle.Top or Handle.TopLeft or Handle.TopRight;
        bool bottom = _activeHandle is Handle.Bottom or Handle.BottomLeft or Handle.BottomRight;

        if (_activeHandle == Handle.Move)
        {
            _dragMin += deltaUI;
        }
        else
        {
            if (left) { _dragMin.X += deltaUI.X; _dragSize.X -= deltaUI.X; }
            if (right) { _dragSize.X += deltaUI.X; }
            if (top) { _dragMin.Y += deltaUI.Y; _dragSize.Y -= deltaUI.Y; }
            if (bottom) { _dragSize.Y += deltaUI.Y; }
        }

        // Snap a copy of the raw rectangle; the raw values keep tracking the mouse so the widget can leave a guide.
        Vector2 min = _dragMin;
        Vector2 size = _dragSize;

        bool snapping = (_gridSnap || _smartGuides) && !ImGui.GetIO().KeyAlt;
        if (snapping)
        {
            GatherTargets(w, doc);
            float thr = SnapPixels / MathF.Max(_scale, 0.0001f);
            float g = MathF.Max(_gridSize, 1);

            if (_activeHandle == Handle.Move)
            {
                SnapMove(ref min, size, thr, g);
            }
            else
            {
                if (left || right) SnapResizeAxis(ref min.X, ref size.X, left, right, _targetsX, thr, g, horizontal: true);
                if (top || bottom) SnapResizeAxis(ref min.Y, ref size.Y, top, bottom, _targetsY, thr, g, horizontal: false);
            }
        }

        size.X = MathF.Max(size.X, 1.0f);
        size.Y = MathF.Max(size.Y, 1.0f);
        ApplyAbsolute(doc, w, min, size);
    }

    // Snaps the moving widget's left/center/right (and top/middle/bottom) to sibling guides, falling back to
    // the grid. Guides win when within threshold so alignment feels intentional.
    private void SnapMove(ref Vector2 min, Vector2 size, float thr, float g)
    {
        if (TrySnapMoveAxis(min.X, size.X, _targetsX, thr, out float offX, out float targetX))
        {
            min.X += offX;
            _guideX = _imageOrigin.X + targetX * _scale;
        }
        else if (_gridSnap)
        {
            min.X = MathF.Round(min.X / g) * g;
        }

        if (TrySnapMoveAxis(min.Y, size.Y, _targetsY, thr, out float offY, out float targetY))
        {
            min.Y += offY;
            _guideY = _imageOrigin.Y + targetY * _scale;
        }
        else if (_gridSnap)
        {
            min.Y = MathF.Round(min.Y / g) * g;
        }
    }

    // Finds the smallest offset that aligns the rect's near edge, center, or far edge with a target line.
    private bool TrySnapMoveAxis(float minValue, float sizeValue, List<float> targets, float thr, out float offset, out float target)
    {
        offset = 0.0f;
        target = 0.0f;
        if (!_smartGuides) return false;

        Span<float> lines = stackalloc float[] { minValue, minValue + sizeValue * 0.5f, minValue + sizeValue };
        float bestDist = thr;
        bool found = false;
        foreach (float line in lines)
        {
            foreach (float t in targets)
            {
                float d = MathF.Abs(line - t);
                if (d <= bestDist)
                {
                    bestDist = d;
                    offset = t - line;
                    target = t;
                    found = true;
                }
            }
        }

        return found;
    }

    // Snaps a resizing edge to the nearest sibling edge (guides) or the grid, keeping the opposite edge fixed.
    private void SnapResizeAxis(ref float min, ref float size, bool nearEdge, bool farEdge, List<float> targets, float thr, float g, bool horizontal)
    {
        if (nearEdge)
        {
            if (TrySnapValue(min, targets, thr, g, out float snapped, out float? guide))
            {
                size += min - snapped;
                min = snapped;
                SetGuide(guide, horizontal);
            }
        }

        if (farEdge)
        {
            float far = min + size;
            if (TrySnapValue(far, targets, thr, g, out float snapped, out float? guide))
            {
                size = snapped - min;
                SetGuide(guide, horizontal);
            }
        }
    }

    // Snaps a single value to the nearest sibling guide within threshold, else to the grid. Returns whether it
    // moved and, when it snapped to a guide (not the grid), the guide's line so it can be drawn.
    private bool TrySnapValue(float value, List<float> targets, float thr, float g, out float snapped, out float? guide)
    {
        if (_smartGuides)
        {
            float bestDist = thr;
            bool found = false;
            float best = 0.0f;
            foreach (float t in targets)
            {
                float d = MathF.Abs(value - t);
                if (d <= bestDist) { bestDist = d; best = t; found = true; }
            }

            if (found)
            {
                snapped = best;
                guide = best;
                return true;
            }
        }

        if (_gridSnap)
        {
            snapped = MathF.Round(value / g) * g;
            guide = null;
            return true;
        }

        snapped = value;
        guide = null;
        return false;
    }

    private void SetGuide(float? guideUI, bool horizontal)
    {
        if (guideUI == null) return;
        if (horizontal) _guideX = _imageOrigin.X + guideUI.Value * _scale;
        else _guideY = _imageOrigin.Y + guideUI.Value * _scale;
    }

    // Collects candidate snap lines (edges + centers) from the widget's container and its siblings, in absolute
    // UI coordinates, so a drag can align to them.
    private void GatherTargets(Widget w, UIRoot doc)
    {
        _targetsX.Clear();
        _targetsY.Clear();

        Vector4 parent = w.Parent?.ScreenRect ?? new Vector4(0, 0, doc.Width, doc.Height);
        AddTargets(parent);

        IReadOnlyList<Widget> siblings = w.Parent?.Children ?? doc.Children;
        foreach (Widget s in siblings)
        {
            if (s != w) AddTargets(s.ScreenRect);
        }
    }

    private void AddTargets(Vector4 r)
    {
        _targetsX.Add(r.X);
        _targetsX.Add(r.X + r.Z * 0.5f);
        _targetsX.Add(r.X + r.Z);
        _targetsY.Add(r.Y);
        _targetsY.Add(r.Y + r.W * 0.5f);
        _targetsY.Add(r.Y + r.W);
    }

    private void DrawGuides(ImDrawListPtr drawList)
    {
        uint color = ImGui.GetColorU32(new Vector4(1.0f, 0.30f, 0.60f, 0.9f));
        if (_guideX is float gx)
            drawList.AddLine(new Vector2(gx, _imageOrigin.Y), new Vector2(gx, _imageOrigin.Y + _renderSize.Y * _cameraZoom), color);
        if (_guideY is float gy)
            drawList.AddLine(new Vector2(_imageOrigin.X, gy), new Vector2(_imageOrigin.X + _renderSize.X * _cameraZoom), color);
    }

    // Back-solves a widget's Position/Size so it resolves to the given absolute UI rectangle, honoring its
    // current anchor and pivot (so dragging behaves correctly regardless of how the widget is anchored).
    private static void ApplyAbsolute(UIRoot doc, Widget w, Vector2 minUI, Vector2 sizeUI)
    {
        Vector4 p = w.Parent?.ScreenRect ?? new Vector4(0, 0, doc.Width, doc.Height);
        Vector2 anchorPoint = new Vector2(p.X, p.Y) + w.Rect.Anchor * new Vector2(p.Z, p.W);

        UIRect r = w.Rect;
        r.Size = sizeUI;
        r.Position = minUI - anchorPoint + r.Pivot * sizeUI;
        w.Rect = r;
    }

    private void DrawSelection(ImDrawListPtr drawList)
    {
        Widget? sel = _context.SelectedWidget;
        if (sel == null) return;

        (Vector2 min, Vector2 max) = ScreenRectOf(sel);
        uint accent = ImGui.GetColorU32(new Vector4(0.30f, 0.70f, 1.0f, 1.0f));
        drawList.AddRect(min, max, accent, 0.0f, ImDrawFlags.None, 1.5f);

        foreach (Vector2 h in HandlePoints(min, max))
        {
            drawList.AddRectFilled(h - new Vector2(3, 3), h + new Vector2(3, 3), accent);
        }

        // Live size readout while dragging, so precise dimensions are visible without opening the inspector.
        if (_activeHandle != Handle.None)
        {
            Vector4 sr = sel.ScreenRect;
            string label = $"{sr.Z:0} x {sr.W:0}";
            Vector2 ts = ImGui.CalcTextSize(label);
            Vector2 pos = new(min.X, min.Y - ts.Y - 4.0f);
            drawList.AddRectFilled(pos - new Vector2(3, 2), pos + ts + new Vector2(3, 2), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)), 3.0f);
            drawList.AddText(pos, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), label);
        }
    }

    // Which handle (if any) the point is over, for the selected widget. Corners/edges win over the body.
    private Handle HandleAt(Widget w, Vector2 point)
    {
        (Vector2 min, Vector2 max) = ScreenRectOf(w);

        bool left = MathF.Abs(point.X - min.X) <= HandleReach;
        bool right = MathF.Abs(point.X - max.X) <= HandleReach;
        bool top = MathF.Abs(point.Y - min.Y) <= HandleReach;
        bool bottom = MathF.Abs(point.Y - max.Y) <= HandleReach;
        bool withinX = point.X >= min.X - HandleReach && point.X <= max.X + HandleReach;
        bool withinY = point.Y >= min.Y - HandleReach && point.Y <= max.Y + HandleReach;

        if (top && left) return Handle.TopLeft;
        if (top && right) return Handle.TopRight;
        if (bottom && left) return Handle.BottomLeft;
        if (bottom && right) return Handle.BottomRight;
        if (left && withinY) return Handle.Left;
        if (right && withinY) return Handle.Right;
        if (top && withinX) return Handle.Top;
        if (bottom && withinX) return Handle.Bottom;

        if (point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y)
            return Handle.Move;

        return Handle.None;
    }

    private static Vector2[] HandlePoints(Vector2 min, Vector2 max)
    {
        Vector2 mid = (min + max) * 0.5f;
        return new[]
        {
            min, new Vector2(mid.X, min.Y), new Vector2(max.X, min.Y),
            new Vector2(min.X, mid.Y), new Vector2(max.X, mid.Y),
            new Vector2(min.X, max.Y), new Vector2(mid.X, max.Y), max,
        };
    }

    private (Vector2 Min, Vector2 Max) ScreenRectOf(Widget w)
    {
        Vector4 sr = w.ScreenRect;
        Vector2 min = _imageOrigin + new Vector2(sr.X, sr.Y) * _scale;
        Vector2 max = min + new Vector2(sr.Z, sr.W) * _scale;
        return (min, max);
    }

    private Vector2 ToUI(Vector2 screenPoint) => (screenPoint - _imageOrigin) / MathF.Max(_scale, 0.0001f);

    // Topmost widget (last child drawn on top) whose rectangle contains the UI-space point.
    private static Widget? Pick(Widget root, Vector2 pointUI)
    {
        for (int i = root.Children.Count - 1; i >= 0; i--)
        {
            Widget? hit = Pick(root.Children[i], pointUI);
            if (hit != null) return hit;
        }

        if (root is UIRoot) return null; // the document root itself isn't selectable

        Vector4 sr = root.ScreenRect;
        bool inside = pointUI.X >= sr.X && pointUI.X <= sr.X + sr.Z && pointUI.Y >= sr.Y && pointUI.Y <= sr.Y + sr.W;
        return inside ? root : null;
    }
}

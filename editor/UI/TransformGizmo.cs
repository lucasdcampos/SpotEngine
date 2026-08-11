using System;
using System.Numerics;
using ImGuiNET;
using Spot.Rendering;
using Spot.Scenes;

using Spot.DebugUI.UI;
using Spot.Editor.UI;
namespace Spot.Editor.UI;

public enum GizmoMode
{
    Translate,
    Rotate,
    Scale
}

/// <summary>
/// A hand-rolled transform gizmo (translate / rotate / scale) that works with both the 2D
/// orthographic and the 3D perspective editor cameras. Handles are placed in world space and
/// projected through the camera's view-projection so they line up with the rendered scene, then
/// drawn with the ImGui draw list. Editing writes straight back to the selected <see cref="TransformComponent"/>.
/// </summary>
/// <remarks>
/// In 2D only the X and Y axes are shown (rotation is around Z); in 3D all three axes / rings are
/// shown. Handle geometry is sized in screen pixels so the gizmo keeps a constant size regardless
/// of zoom or camera distance. Deltas are applied to the TransformComponent's local fields, matching the
/// engine's existing convention (parent transforms are not compensated for).
/// </remarks>
public sealed class TransformGizmo
{
    public GizmoMode Mode { get; set; } = GizmoMode.Translate;

    /// <summary>True while an axis/ring is being dragged, so the viewport can suppress camera input.</summary>
    public bool IsUsing => _active != Handle.None;

    private enum Handle { None, X, Y, Z, Screen }

    private Handle _active = Handle.None;
    private float _prevAngle;

    // Screen-space sizing (pixels).
    private const float AxisLengthPx = 80f;
    private const float HitThicknessPx = 8f;
    private const float RingRadiusPx = 80f;
    private const float ArrowHeadPx = 13f;
    private const float ScaleBoxPx = 5f;
    private const float CenterBoxPx = 5f;

    // Per-frame context, set at the top of Draw so the helpers below stay parameter-light.
    private Matrix4x4 _vp;
    private Vector2 _vpPos;
    private Vector2 _vpSize;
    private Vector3 _originWorld;
    private Vector2 _originScreen;
    private EditorCamera _cam = null!;
    private ImGuiIOPtr _io;
    private ImDrawListPtr _draw;
    private EditorPalette _pal = null!;

    public void Draw(TransformComponent transform, EditorCamera camera, Vector2 viewportPos, Vector2 viewportSize, bool viewportHovered)
    {
        _cam = camera;
        _vp = camera.ViewProjection;
        _vpPos = viewportPos;
        _vpSize = viewportSize;
        _io = ImGui.GetIO();
        _draw = ImGui.GetWindowDrawList();
        _pal = EditorThemeManager.Current.Palette;

        _originWorld = transform.WorldPosition;
        if (!WorldToScreen(_originWorld, out _originScreen))
        {
            _active = Handle.None; // origin behind the camera; nothing to interact with
            return;
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _active = Handle.None;
        }

        switch (Mode)
        {
            case GizmoMode.Translate: DoTranslate(transform, viewportHovered); break;
            case GizmoMode.Rotate: DoRotate(transform, viewportHovered); break;
            case GizmoMode.Scale: DoScale(transform, viewportHovered); break;
        }
    }

    // ------------------------------------------------------------------ Translate

    private void DoTranslate(TransformComponent t, bool viewportHovered)
    {
        Vector2 mouse = _io.MousePos;

        // Determine the hovered handle (unless a drag is already in progress).
        Handle hot = _active;
        if (!IsUsing)
        {
            float best = HitThicknessPx;
            hot = Handle.None;
            foreach (int axis in ActiveAxes())
            {
                Vector2 dir = ProjectAxis(AxisDir(axis), out float ppu);
                if (ppu < 0.5f) continue;
                Vector2 tip = _originScreen + dir * AxisLengthPx;
                float d = DistToSegment(mouse, _originScreen, tip);
                if (d < best) { best = d; hot = HandleOf(axis); }
            }
            if (Vector2.Distance(mouse, _originScreen) < CenterBoxPx + 3f) hot = Handle.Screen;
        }

        if (!IsUsing && viewportHovered && hot != Handle.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _active = hot;
        }

        // Apply the drag.
        if (IsUsing && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            Vector2 md = _io.MouseDelta;
            int axis = AxisOf(_active);
            if (axis >= 0)
            {
                Vector2 dir = ProjectAxis(AxisDir(axis), out float ppu);
                if (ppu > 0.001f)
                    t.Position += AxisDir(axis) * (Vector2.Dot(md, dir) / ppu);
            }
            else if (_active == Handle.Screen)
            {
                Vector3 world = Vector3.Zero;
                Vector2 rDir = ProjectAxis(_cam.Right, out float rPpu);
                Vector2 uDir = ProjectAxis(_cam.Up, out float uPpu);
                if (rPpu > 0.001f) world += _cam.Right * (Vector2.Dot(md, rDir) / rPpu);
                if (uPpu > 0.001f) world += _cam.Up * (Vector2.Dot(md, uDir) / uPpu);
                t.Position += world;
            }
        }

        // Draw.
        foreach (int axis in ActiveAxes())
        {
            Vector2 dir = ProjectAxis(AxisDir(axis), out float ppu);
            if (ppu < 0.5f) continue;
            Vector2 tip = _originScreen + dir * AxisLengthPx;
            uint col = AxisColor(axis, HandleOf(axis) == hot || HandleOf(axis) == _active);
            _draw.AddLine(_originScreen, tip, col, 3f);
            DrawArrowHead(tip, dir, col);
        }
        uint centerCol = ImGui.GetColorU32(hot == Handle.Screen || _active == Handle.Screen ? _pal.GizmoHover : new Vector4(1, 1, 1, 1));
        DrawBox(_originScreen, CenterBoxPx, centerCol);
    }

    // ------------------------------------------------------------------ Rotate

    private void DoRotate(TransformComponent t, bool viewportHovered)
    {
        Vector2 mouse = _io.MousePos;
        float worldRadius = WorldRadius(RingRadiusPx);

        // Hover detection over the rings.
        Handle hot = _active;
        if (!IsUsing)
        {
            float best = HitThicknessPx;
            hot = Handle.None;
            foreach (int axis in ActiveRotationAxes())
            {
                if (!BuildRing(AxisDir(axis), worldRadius, out Vector2[] pts)) continue;
                float d = DistToPolyline(mouse, pts);
                if (d < best) { best = d; hot = HandleOf(axis); }
            }
        }

        if (!IsUsing && viewportHovered && hot != Handle.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _active = hot;
            _prevAngle = MathF.Atan2(mouse.Y - _originScreen.Y, mouse.X - _originScreen.X);
        }

        if (IsUsing && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            int axis = AxisOf(_active);
            if (axis >= 0)
            {
                float cur = MathF.Atan2(mouse.Y - _originScreen.Y, mouse.X - _originScreen.X);
                float delta = WrapAngle(cur - _prevAngle);
                _prevAngle = cur;
                // Flip so dragging feels consistent whether the ring faces toward or away from us.
                float sign = Vector3.Dot(AxisDir(axis), _cam.Forward) >= 0f ? 1f : -1f;
                t.Rotation = AddComponent(t.Rotation, axis, delta * (180f / MathF.PI) * sign);
            }
        }

        foreach (int axis in ActiveRotationAxes())
        {
            if (!BuildRing(AxisDir(axis), worldRadius, out Vector2[] pts)) continue;
            uint col = AxisColor(axis, HandleOf(axis) == hot || HandleOf(axis) == _active);
            _draw.AddPolyline(ref pts[0], pts.Length, col, ImDrawFlags.Closed, 2.5f);
        }
        DrawBox(_originScreen, CenterBoxPx, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)));
    }

    // ------------------------------------------------------------------ Scale

    private void DoScale(TransformComponent t, bool viewportHovered)
    {
        Vector2 mouse = _io.MousePos;

        Handle hot = _active;
        if (!IsUsing)
        {
            float best = HitThicknessPx;
            hot = Handle.None;
            foreach (int axis in ActiveAxes())
            {
                Vector2 dir = ProjectAxis(AxisDir(axis), out float ppu);
                if (ppu < 0.5f) continue;
                Vector2 tip = _originScreen + dir * AxisLengthPx;
                float d = DistToSegment(mouse, _originScreen, tip);
                if (d < best) { best = d; hot = HandleOf(axis); }
            }
            if (Vector2.Distance(mouse, _originScreen) < CenterBoxPx + 3f) hot = Handle.Screen;
        }

        if (!IsUsing && viewportHovered && hot != Handle.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _active = hot;
        }

        if (IsUsing && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            Vector2 md = _io.MouseDelta;
            int axis = AxisOf(_active);
            if (axis >= 0)
            {
                Vector2 dir = ProjectAxis(AxisDir(axis), out float ppu);
                if (ppu > 0.001f)
                    t.Scale = AddComponent(t.Scale, axis, Vector2.Dot(md, dir) / ppu);
            }
            else if (_active == Handle.Screen)
            {
                float uniform = (md.X - md.Y) * 0.01f;
                t.Scale += new Vector3(uniform, uniform, _cam.Is3D ? uniform : 0f);
            }
        }

        foreach (int axis in ActiveAxes())
        {
            Vector2 dir = ProjectAxis(AxisDir(axis), out float ppu);
            if (ppu < 0.5f) continue;
            Vector2 tip = _originScreen + dir * AxisLengthPx;
            uint col = AxisColor(axis, HandleOf(axis) == hot || HandleOf(axis) == _active);
            _draw.AddLine(_originScreen, tip, col, 3f);
            DrawBox(tip, ScaleBoxPx, col);
        }
        uint centerCol = ImGui.GetColorU32(hot == Handle.Screen || _active == Handle.Screen ? _pal.GizmoHover : new Vector4(1, 1, 1, 1));
        DrawBox(_originScreen, CenterBoxPx + 1f, centerCol);
    }

    // ------------------------------------------------------------------ Geometry helpers

    private bool WorldToScreen(Vector3 world, out Vector2 screen)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), _vp);
        if (clip.W <= 1e-5f) { screen = default; return false; }
        Vector3 ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        screen = new Vector2(
            _vpPos.X + (ndc.X * 0.5f + 0.5f) * _vpSize.X,
            _vpPos.Y + (1f - (ndc.Y * 0.5f + 0.5f)) * _vpSize.Y);
        return true;
    }

    // Screen-space direction of a world axis at the origin, plus pixels-per-world-unit along it.
    private Vector2 ProjectAxis(Vector3 axis, out float pixelsPerUnit)
    {
        if (!WorldToScreen(_originWorld + axis, out Vector2 tip)) { pixelsPerUnit = 0f; return Vector2.Zero; }
        Vector2 d = tip - _originScreen;
        pixelsPerUnit = d.Length();
        return pixelsPerUnit > 1e-4f ? d / pixelsPerUnit : Vector2.Zero;
    }

    // World-space radius that projects to roughly the given pixel size at the origin.
    private float WorldRadius(float pixels)
    {
        ProjectAxis(_cam.Right, out float ppu);
        if (ppu < 1e-4f) ProjectAxis(_cam.Up, out ppu);
        return ppu > 1e-4f ? pixels / ppu : pixels;
    }

    private bool BuildRing(Vector3 axis, float radius, out Vector2[] points)
    {
        const int segments = 64;
        Vector3 t = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 u = Vector3.Normalize(Vector3.Cross(axis, t));
        Vector3 v = Vector3.Cross(axis, u);

        points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * MathF.PI * 2f;
            Vector3 p = _originWorld + radius * (MathF.Cos(a) * u + MathF.Sin(a) * v);
            if (!WorldToScreen(p, out points[i])) return false; // ring crosses the camera plane
        }
        return true;
    }

    private void DrawArrowHead(Vector2 tip, Vector2 dir, uint col)
    {
        Vector2 perp = new(-dir.Y, dir.X);
        Vector2 b = tip - dir * ArrowHeadPx + perp * (ArrowHeadPx * 0.4f);
        Vector2 c = tip - dir * ArrowHeadPx - perp * (ArrowHeadPx * 0.4f);
        _draw.AddTriangleFilled(tip, b, c, col);
    }

    private void DrawBox(Vector2 center, float halfSize, uint col)
    {
        Vector2 h = new(halfSize, halfSize);
        _draw.AddRectFilled(center - h, center + h, col);
    }

    private uint AxisColor(int axis, bool highlighted)
    {
        Vector4 c = highlighted ? _pal.GizmoHover : (axis == 0 ? _pal.AxisX : axis == 1 ? _pal.AxisY : _pal.AxisZ);
        return ImGui.GetColorU32(c);
    }

    private int[] ActiveAxes() => _cam.Is3D ? Axes3D : Axes2D;
    private int[] ActiveRotationAxes() => _cam.Is3D ? Axes3D : RotationAxes2D;

    private static readonly int[] Axes2D = { 0, 1 };
    private static readonly int[] Axes3D = { 0, 1, 2 };
    private static readonly int[] RotationAxes2D = { 2 };

    private static Vector3 AxisDir(int axis) => axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
    private static Vector3 AddComponent(Vector3 v, int axis, float d) =>
        axis == 0 ? new Vector3(v.X + d, v.Y, v.Z) : axis == 1 ? new Vector3(v.X, v.Y + d, v.Z) : new Vector3(v.X, v.Y, v.Z + d);

    private static int AxisOf(Handle h) => h == Handle.X ? 0 : h == Handle.Y ? 1 : h == Handle.Z ? 2 : -1;
    private static Handle HandleOf(int axis) => axis == 0 ? Handle.X : axis == 1 ? Handle.Y : Handle.Z;

    private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / MathF.Max(Vector2.Dot(ab, ab), 1e-6f);
        t = Math.Clamp(t, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    private static float DistToPolyline(Vector2 p, Vector2[] pts)
    {
        float best = float.MaxValue;
        for (int i = 0; i < pts.Length; i++)
        {
            Vector2 a = pts[i];
            Vector2 b = pts[(i + 1) % pts.Length];
            best = MathF.Min(best, DistToSegment(p, a, b));
        }
        return best;
    }

    private static float WrapAngle(float a)
    {
        while (a > MathF.PI) a -= MathF.PI * 2f;
        while (a < -MathF.PI) a += MathF.PI * 2f;
        return a;
    }
}


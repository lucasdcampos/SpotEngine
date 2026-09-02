using System;
using System.Numerics;
using ImGuiNET;
using Spot.Rendering;
using Spot.Scenes;
using Spot.DebugUI.UI;
using Spot.Editor.UI;

namespace Spot.Editor.Panels;

public class ViewportPanel
{
    private readonly EditorContext _context;
    private Framebuffer? _framebuffer;
    private Framebuffer? _cameraPreviewFramebuffer;
    private EditorCamera? _camera;
    private readonly TransformGizmo _gizmo = new();
    private readonly SceneIcons _sceneIcons = new();

    // Latched right-drag "fly the 3D camera" state. Held from press to release so the cursor
    // lock stays stable and a right-click in another panel (e.g. the hierarchy context menu)
    // never starts flying.
    private bool _isFlyingCamera;

    // Whether *this* panel currently owns the hardware cursor lock. Only the panel that grabbed
    // the lock may release it: with several scene viewports open, every non-flying panel used to
    // reset the cursor to Normal each frame, yanking the lock out from under the panel that was
    // actively flying and letting the (hidden) cursor drift out of the viewport.
    private bool _ownsCursorLock;

    // While flying we only *hide* the cursor and confine it ourselves by snapping it back to the
    // viewport centre every frame (see the fly block). GLFW's Disabled/Raw "confine" modes proved
    // unreliable here — they read back as applied but the OS cursor still roams free and escapes —
    // so manual recentring is the backend-independent way to guarantee it never leaves the viewport.
    private const Silk.NET.Input.CursorMode LockMode = Silk.NET.Input.CursorMode.Hidden;

    // Where the cursor was when flying started; restored on release so it reappears where the drag
    // began instead of jumping to the viewport centre.
    private Vector2 _flyAnchor;

    // Counts frames since flying started; the first frame is skipped for look (it centres the cursor
    // from the press point, which isn't a real movement delta).
    private int _flyFrame;

    public ViewportPanel(EditorContext context)
    {
        _context = context;
    }

    public void SetFramebuffer(Framebuffer framebuffer)
    {
        _framebuffer = framebuffer;
    }

    public void SetCameraPreviewFramebuffer(Framebuffer framebuffer)
    {
        _cameraPreviewFramebuffer = framebuffer;
    }

    public void SetCamera(EditorCamera camera)
    {
        _camera = camera;
    }

    public void OnImGuiRender(bool handleInput = true)
    {
        // Safety net: if we grabbed the cursor lock but won't run input this frame (panel lost
        // focus mid-flight, entered Play, etc.), release it here so the cursor can never get
        // stranded in the hidden/locked state.
        if (!handleInput && _ownsCursorLock)
        {
            ReleaseCursorLock();
        }

        var viewportSize = ImGui.GetContentRegionAvail();

        if (_framebuffer != null && viewportSize.X > 0 && viewportSize.Y > 0)
        {
            _framebuffer.Resize((uint)viewportSize.X, (uint)viewportSize.Y);
            if (handleInput && _camera != null)
                _camera.SetViewportSize(viewportSize.X, viewportSize.Y);

            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.Image((IntPtr)_framebuffer.ColorAttachment, viewportSize, new Vector2(0, 1), new Vector2(1, 0));
            bool isHovered = ImGui.IsItemHovered();

            // Dragging a model from the Asset Browser onto the viewport imports it into the scene as a
            // faithful entity hierarchy with its materials applied, then selects the new root.
            if (handleInput && ImGui.BeginDragDropTarget())
            {
                unsafe
                {
                    var modelPayload = ImGui.AcceptDragDropPayload("MODEL_FILE");
                    if (modelPayload.NativePtr != null && _context.ActiveScene != null)
                    {
                        string? path = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(modelPayload.Data);
                        if (path != null)
                        {
                            Entity? root = ModelInstantiator.Instantiate(_context.ActiveScene, path);
                            if (root != null) _context.Selection = root.Value;
                        }
                    }
                }
                ImGui.EndDragDropTarget();
            }

            // Non-interactive HUD (orientation gizmo, FPS, camera readout), drawn on every scene
            // viewport whether focused or not so the overlays stay stable while you work.
            if (_camera != null)
                DrawViewportOverlays(cursorPos, viewportSize);

            if (handleInput && _camera != null)
            {
                // Toolbar overlay: camera mode toggle followed by the gizmo mode buttons.
                ImGui.SetCursorScreenPos(cursorPos + new Vector2(10, 10));
                if (ImGui.Button(_camera.Is3D ? "3D Mode" : "2D Mode"))
                {
                    _camera.ToggleMode();
                }

                ImGui.SameLine();
                ImGui.Dummy(new Vector2(8, 0));
                ImGui.SameLine();
                DrawGizmoModeButton(EditorIcons.Move, "Move (W)", GizmoMode.Translate);
                ImGui.SameLine();
                DrawGizmoModeButton(EditorIcons.Rotate, "Rotate (E)", GizmoMode.Rotate);
                ImGui.SameLine();
                DrawGizmoModeButton(EditorIcons.Scale, "Scale (R)", GizmoMode.Scale);

                ImGui.SameLine();
                ImGui.Dummy(new Vector2(8, 0));
                ImGui.SameLine();

                bool showColliders = Spot.Physics.PhysicsDebug.ShowColliders;
                if (ImGui.Checkbox("Show Colliders", ref showColliders))
                {
                    Spot.Physics.PhysicsDebug.ShowColliders = showColliders;
                }

                ImGui.SameLine();
                bool fullbright = Spot.Rendering.RendererDebug.Fullbright;
                if (ImGui.Checkbox("Fullbright", ref fullbright))
                {
                    Spot.Rendering.RendererDebug.Fullbright = fullbright;
                }

                ImGui.SameLine();
                bool wireframe = Spot.Rendering.RendererDebug.Wireframe;
                if (ImGui.Checkbox("Wireframe", ref wireframe))
                {
                    Spot.Rendering.RendererDebug.Wireframe = wireframe;
                }

                if (_cameraPreviewFramebuffer != null && _context.Selection.HasValue && _context.Selection.Value.HasComponent<Spot.Scenes.CameraComponent>())
                {
                    // Render Camera Preview in bottom right
                    float previewWidth = 320;
                    float previewHeight = 180;

                    var previewPos = cursorPos + viewportSize - new Vector2(previewWidth + 20, previewHeight + 20);

                    // A framed, labeled preview card that matches the editor's surface treatment.
                    var palette = EditorThemeManager.Current.Palette;
                    var drawList = ImGui.GetWindowDrawList();
                    Vector2 bgMin = previewPos - new Vector2(2, 2);
                    Vector2 bgMax = previewPos + new Vector2(previewWidth + 2, previewHeight + 2);
                    drawList.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.55f)), 4.0f);
                    drawList.AddImage((IntPtr)_cameraPreviewFramebuffer.ColorAttachment, previewPos, previewPos + new Vector2(previewWidth, previewHeight), new Vector2(0, 1), new Vector2(1, 0));
                    drawList.AddRect(bgMin, bgMax, ImGui.GetColorU32(palette.Border), 4.0f, ImDrawFlags.None, 1.0f);

                    drawList.AddText(previewPos + new Vector2(6, 4), ImGui.GetColorU32(palette.Text), "Camera Preview");
                }

                var io = ImGui.GetIO();

                // Right-drag flies the 3D camera. Latch the state on press (only when the viewport
                // is actually hovered) and hold it until the button is released. Recomputing it from
                // a live hover/drag check each frame is fragile: the check flickers, which unlocks the
                // cursor mid-look (letting it escape the viewport) and can wrongly start flying from a
                // right-click made in another panel, e.g. the hierarchy context menu, since
                // IsMouseDragging(Right) is global and not scoped to this window.
                if (_isFlyingCamera)
                {
                    if (!ImGui.IsMouseDown(ImGuiMouseButton.Right))
                        _isFlyingCamera = false;
                }
                else if (_camera.Is3D && isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    _isFlyingCamera = true;
                    _flyFrame = 0;
                    _flyAnchor = io.MousePos;
                }
                bool isFlyingCamera = _isFlyingCamera;

                // --- EDITOR ICONS (billboards for invisible entities: cameras, lights, sky) ---
                // Drawn before the gizmo so gizmo handles render on top; the hovered icon (if any) is
                // preferred over mesh picking when the user clicks.
                Entity? hoveredIcon = null;
                if (_context.ActiveScene != null)
                {
                    hoveredIcon = _sceneIcons.Draw(_context.ActiveScene, _camera, cursorPos, viewportSize, isHovered);
                }

                // --- TRANSFORM GIZMO (2D & 3D: translate / rotate / scale) ---
                if (_context.Selection.HasValue && _context.Selection.Value.HasComponent<TransformComponent>())
                {
                    var transform = _context.Selection.Value.GetComponent<TransformComponent>();
                    _gizmo.Draw(transform, _camera, cursorPos, viewportSize, isHovered && !isFlyingCamera);

                    // Unity-style mode switch, plus F to frame the selection. Guarded so it does not fire
                    // while flying the 3D camera with the right mouse button (which uses W/A/S/D for movement).
                    if (isHovered && !ImGui.IsMouseDown(ImGuiMouseButton.Right))
                    {
                        if (ImGui.IsKeyPressed(ImGuiKey.W)) _gizmo.Mode = GizmoMode.Translate;
                        if (ImGui.IsKeyPressed(ImGuiKey.E)) _gizmo.Mode = GizmoMode.Rotate;
                        if (ImGui.IsKeyPressed(ImGuiKey.R)) _gizmo.Mode = GizmoMode.Scale;
                        if (ImGui.IsKeyPressed(ImGuiKey.F)) _camera.Focus(transform.WorldPosition);
                    }
                }

                // --- CLICK TO SELECT ---
                // A left click that is not consumed by a gizmo handle picks the entity under the
                // cursor (or clears the selection when nothing is hit). The camera uses the middle/
                // right buttons, so the left button is free for selection.
                if (isHovered && !_gizmo.IsUsing && !isFlyingCamera && _context.ActiveScene != null
                    && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    // An editor icon under the cursor takes priority over mesh picking, so invisible
                    // entities (cameras, lights, sky) can be selected by clicking their billboard.
                    _context.Selection = hoveredIcon ?? ScenePicker.Pick(
                        _context.ActiveScene, _camera.ViewProjection, io.MousePos, cursorPos, viewportSize);
                }

                // --- CAMERA CONTROLS ---
                if (isHovered && !_gizmo.IsUsing && !isFlyingCamera)
                {
                    if (io.MouseWheel != 0.0f)
                    {
                        _camera.OnMouseScroll(io.MouseWheel);
                    }
                }

                if (isFlyingCamera)
                {
                    // Hide the cursor and confine it ourselves: every frame we read how far it drifted
                    // from the viewport centre, feed that as the look delta, then warp it straight back
                    // to the centre. Because it can never reach an edge, it can never leave the
                    // viewport — regardless of whether GLFW's own confine modes work on this machine.
                    var mice = Spot.Core.Application.Instance.Window.Input.Mice;
                    var mouse = mice.Count > 0 ? mice[0] : null;
                    if (mouse != null && mouse.Cursor.CursorMode != LockMode)
                    {
                        mouse.Cursor.CursorMode = LockMode;
                    }
                    io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;
                    _ownsCursorLock = true;

                    Vector2 center = cursorPos + viewportSize * 0.5f;
                    Vector2 lookDelta = Vector2.Zero;
                    if (_flyFrame > 0)
                    {
                        // Offset from centre == this frame's movement, since we recentre every frame.
                        lookDelta = io.MousePos - center;
                    }
                    if (mouse != null)
                    {
                        mouse.Position = center;   // snap back so the next frame's offset is pure movement
                    }
                    _flyFrame++;

                    // 3D Mouselook
                    _camera.MouseLook(lookDelta);

                    // 3D Movement
                    Vector3 moveDir = Vector3.Zero;
                    if (ImGui.IsKeyDown(ImGuiKey.W)) moveDir.Z += 1;
                    if (ImGui.IsKeyDown(ImGuiKey.S)) moveDir.Z -= 1;
                    if (ImGui.IsKeyDown(ImGuiKey.A)) moveDir.X -= 1;
                    if (ImGui.IsKeyDown(ImGuiKey.D)) moveDir.X += 1;
                    if (ImGui.IsKeyDown(ImGuiKey.E)) moveDir.Y += 1;
                    if (ImGui.IsKeyDown(ImGuiKey.Q)) moveDir.Y -= 1;

                    if (moveDir != Vector3.Zero)
                    {
                        float speed = 5.0f; // units per second
                        if (ImGui.IsKeyDown(ImGuiKey.LeftShift)) speed = 20.0f;
                        _camera.Move(moveDir, speed * io.DeltaTime);
                    }
                }
                else
                {
                    // Release the cursor only if this panel is the one holding the lock. Other
                    // viewports must not touch it, or they'd unlock a panel that is still flying.
                    if (_ownsCursorLock)
                    {
                        ReleaseCursorLock();
                    }

                    if (!_gizmo.IsUsing)
                    {
                        if ((isHovered || ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right)) &&
                            (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || (!_camera.Is3D && ImGui.IsMouseDragging(ImGuiMouseButton.Right))))
                        {
                            // 2D/3D Pan
                            _camera.OnMouseDrag(io.MouseDelta);
                        }
                    }
                }
            }
        }
        else
        {
            ImGui.Text("Viewport Placeholder");
        }
    }

    // Restores the hardware cursor to its normal (visible, free) state and drops this panel's
    // ownership of the lock. Safe to call whether or not the cursor is currently captured.
    private void ReleaseCursorLock()
    {
        var mice = Spot.Core.Application.Instance.Window.Input.Mice;
        if (mice.Count > 0)
        {
            var mouse = mice[0];
            // Put the cursor back where the drag began (kept inside the viewport) so it doesn't
            // reappear parked at the viewport centre.
            mouse.Position = _flyAnchor;
            if (mouse.Cursor.CursorMode != Silk.NET.Input.CursorMode.Normal)
            {
                mouse.Cursor.CursorMode = Silk.NET.Input.CursorMode.Normal;
            }
        }
        ImGui.GetIO().ConfigFlags &= ~ImGuiConfigFlags.NoMouseCursorChange;
        _ownsCursorLock = false;
    }

    // Subtle top-right HUD: a camera-orientation gizmo with an FPS and camera readout beneath it. All
    // of it is painted on the window draw list (no interactive widgets) so it never steals input from
    // the viewport, and it mirrors the axis colors used by the transform gizmo and property fields.
    private void DrawViewportOverlays(Vector2 cursorPos, Vector2 viewportSize)
    {
        if (_camera == null || viewportSize.X < 160.0f || viewportSize.Y < 120.0f)
            return;

        var palette = EditorThemeManager.Current.Palette;
        var drawList = ImGui.GetWindowDrawList();

        // --- Orientation gizmo ---
        float axisLen = 20.0f;
        Vector2 center = new(cursorPos.X + viewportSize.X - axisLen - 18.0f, cursorPos.Y + axisLen + 16.0f);
        drawList.AddCircleFilled(center, axisLen + 8.0f, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.22f)), 24);

        DrawOrientationAxis(drawList, center, Vector3.UnitX, "X", palette.AxisX, axisLen);
        DrawOrientationAxis(drawList, center, Vector3.UnitY, "Y", palette.AxisY, axisLen);
        DrawOrientationAxis(drawList, center, Vector3.UnitZ, "Z", palette.AxisZ, axisLen);

        // --- FPS + camera readout, right-aligned under the gizmo ---
        float rightEdge = cursorPos.X + viewportSize.X - 12.0f;
        float y = center.Y + axisLen + 12.0f;

        float fps = Spot.Core.FrameStats.Fps;
        float ms = Spot.Core.FrameStats.FrameTimeMs;
        DrawRightText(drawList, rightEdge, ref y, $"{fps:0} FPS  ({ms:0.0} ms)", palette.Text);

        DrawRightText(drawList, rightEdge, ref y, _camera.Is3D ? "Perspective" : "Orthographic", palette.TextDisabled);

        Vector3 p = _camera.Position;
        string pos = _camera.Is3D
            ? $"{p.X:0.0}, {p.Y:0.0}, {p.Z:0.0}"
            : $"{p.X:0.0}, {p.Y:0.0}";
        DrawRightText(drawList, rightEdge, ref y, pos, palette.TextDisabled);
    }

    private void DrawOrientationAxis(ImDrawListPtr drawList, Vector2 center, Vector3 worldDir, string label, Vector4 color, float length)
    {
        // Project the world axis onto the screen using the camera basis (translation-independent), so
        // the gizmo spins to match the view. Axes pointing toward the camera render brighter.
        Vector2 screenDir = new(Vector3.Dot(_camera!.Right, worldDir), -Vector3.Dot(_camera.Up, worldDir));
        Vector2 tip = center + screenDir * length;
        bool towards = Vector3.Dot(_camera.Forward, worldDir) <= 0.0f;

        uint col = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, towards ? 1.0f : 0.45f));
        drawList.AddLine(center, tip, col, 2.0f);
        drawList.AddCircleFilled(tip, towards ? 4.0f : 3.0f, col, 12);
        if (towards)
        {
            Vector2 ts = ImGui.CalcTextSize(label);
            drawList.AddText(tip - ts * 0.5f, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.95f)), label);
        }
    }

    private static void DrawRightText(ImDrawListPtr drawList, float rightEdge, ref float y, string text, Vector4 color)
    {
        Vector2 size = ImGui.CalcTextSize(text);
        drawList.AddText(new Vector2(rightEdge - size.X, y), ImGui.GetColorU32(color), text);
        y += size.Y + 2.0f;
    }

    // A gizmo-mode toolbar button (icon glyph + tooltip) that stays highlighted while its mode is active.
    private void DrawGizmoModeButton(string glyph, string tooltip, GizmoMode mode)
    {
        bool active = _gizmo.Mode == mode;
        if (active)
        {
            var accent = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
            ImGui.PushStyleColor(ImGuiCol.Button, accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, accent);
        }
        if (ImGui.Button(glyph))
        {
            _gizmo.Mode = mode;
        }
        if (active)
        {
            ImGui.PopStyleColor(2);
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }
}

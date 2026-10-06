using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Spot.Engine;
using Spot.Engine.Scenes;
using Spot.Engine.Graphics;
using Spot.DebugUI.UI;
using Spot.DebugUI.Undo;
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

    // Cursor position the current look delta is measured from — last frame's position, or the viewport
    // centre on the frames where the edge guard warped the cursor back.
    private Vector2 _lastLookPos;

    // Set for the frame right after a warp: the recentring may not be visible to the next poll yet, so
    // that frame's reading is ignored instead of being fed in as a jump.
    private bool _skipLookFrame = true;

    // The scene this viewport shows. Assets dropped here go into it even when another scene tab is the
    // active one; falls back to the active scene when not given.
    private readonly Func<Scene?>? _scene;

    public ViewportPanel(EditorContext context, Func<Scene?>? scene = null)
    {
        _context = context;
        _scene = scene;
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

    /// <summary>
    /// Draws the viewport image, then the editor HUD and interaction (toolbar, gizmos, picking, camera
    /// navigation) on top of it.
    /// </summary>
    /// <param name="handleInput">Whether the editor camera and gizmos respond to the mouse and keyboard.</param>
    /// <param name="gameView">The image is the game camera's view: only the picture is drawn, since the HUD,
    /// gizmos and picking all work in the editor camera's space and input belongs to the game.</param>
    public void OnImGuiRender(bool handleInput = true, bool gameView = false)
    {
        if (gameView) handleInput = false;

        // Safety net: if we grabbed the cursor lock but won't run input this frame (panel lost
        // focus mid-flight, switched to the game camera, etc.), release it here so the cursor can
        // never get stranded in the hidden/locked state.
        if (!handleInput && _ownsCursorLock)
        {
            ReleaseCursorLock();
        }

        var viewportSize = ImGui.GetContentRegionAvail();

        if (_framebuffer != null && viewportSize.X > 0 && viewportSize.Y > 0)
        {
            _framebuffer.Resize((uint)viewportSize.X, (uint)viewportSize.Y);
            // Kept in step even while the viewport shows the game camera, so switching back never shows
            // a frame stretched to an old size.
            _camera?.SetViewportSize(viewportSize.X, viewportSize.Y);

            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.Image((IntPtr)_framebuffer.ColorAttachment, viewportSize, new Vector2(0, 1), new Vector2(1, 0));
            if (gameView) return;
            bool isHovered = ImGui.IsItemHovered();

            // Dragging assets from the Asset Browser onto the viewport builds the entities they stand for (a
            // prefab, a model with its materials, a sprite from an image, ...) where the cursor points, and selects
            // them. Not gated on handleInput: during a drag the asset tile holds ImGui's active item, so this window
            // only counts as hovered on the release frame, and the gate would hide the landing preview.
            // A material dropped on a mesh is applied to it instead; the mesh under the cursor is outlined while
            // the material hovers, and nothing is highlighted where there is no mesh to take it.
            if (_camera != null && ImGui.BeginDragDropTarget())
            {
                Scene? scene = _scene?.Invoke() ?? _context.ActiveScene;
                Vector2 mouse = ImGui.GetIO().MousePos;

                if (AssetSpawner.AcceptDrop(out IReadOnlyList<string> paths, out bool delivered, preview: true))
                {
                    Vector3 point = ScenePicker.DropPoint(
                        scene, _camera.ViewProjection, _camera.Is3D, mouse, cursorPos, viewportSize);

                    if (delivered) DropAssets(scene, paths, point);
                    else DrawDropPreview(paths, point, cursorPos, viewportSize);
                }
                else if (scene != null && MaterialDrop.IsDragging())
                {
                    Entity? target = ScenePicker.PickMesh(scene, _camera.ViewProjection, mouse, cursorPos, viewportSize);
                    if (MaterialDrop.Accept(out string material, out bool dropped, preview: true, drawRect: false))
                    {
                        if (!dropped) DrawMaterialPreview(target, material, mouse, cursorPos, viewportSize);
                        else if (target != null) MaterialDrop.Apply(new[] { target.Value }, material);
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
                DrawToolbar(cursorPos);

                // The toolbar's buttons sit over the image, which has no item id of its own, so the image
                // still counts as hovered beneath them. Clicks, wheel and fly-look aimed at the toolbar must
                // not reach the scene as well (a click on a tool would also pick or deselect).
                if (ImGui.IsAnyItemHovered())
                {
                    isHovered = false;
                }

                if (ImGui.BeginPopup("ViewportCameraSettings"))
                {
                    ImGui.TextUnformatted("Scene Camera");
                    ImGui.Separator();

                    float sensitivity = Utils.EditorSettings.CameraLookSensitivity;
                    ImGui.SetNextItemWidth(160.0f);
                    if (ImGui.SliderFloat("Look sensitivity", ref sensitivity, 0.1f, 5.0f, "%.2fx"))
                    {
                        Utils.EditorSettings.CameraLookSensitivity = sensitivity;
                    }

                    float moveSpeed = Utils.EditorSettings.CameraMoveSpeed;
                    ImGui.SetNextItemWidth(160.0f);
                    if (ImGui.SliderFloat("Fly speed", ref moveSpeed, 0.5f, 100.0f, "%.1f u/s"))
                    {
                        Utils.EditorSettings.CameraMoveSpeed = moveSpeed;
                    }

                    ImGui.Separator();
                    if (ImGui.Button("Reset to defaults"))
                    {
                        Utils.EditorSettings.CameraLookSensitivity = Utils.EditorSettings.DefaultCameraLookSensitivity;
                        Utils.EditorSettings.CameraMoveSpeed = Utils.EditorSettings.DefaultCameraMoveSpeed;
                    }
                    ImGui.TextDisabled("Right-drag to look, WASD/QE to fly, Shift for 4x");
                    ImGui.EndPopup();
                }

                if (_cameraPreviewFramebuffer != null && _context.Selection.HasValue && _context.Selection.Value.HasComponent<Spot.Engine.Camera>())
                {
                    // Camera preview in the bottom-right corner: a floating card with a caption strip above
                    // the picture, in the same chrome as the toolbar.
                    const float previewWidth = 320;
                    const float previewHeight = 180;
                    const float frame = 4.0f;
                    float caption = ImGui.GetTextLineHeight() + 8.0f;

                    var previewPos = cursorPos + viewportSize - new Vector2(previewWidth + 12 + frame, previewHeight + 12 + frame);
                    var palette = EditorThemeManager.Current.Palette;
                    var drawList = ImGui.GetWindowDrawList();
                    Vector2 cardMin = previewPos - new Vector2(frame, frame + caption);
                    Vector2 cardMax = previewPos + new Vector2(previewWidth + frame, previewHeight + frame);
                    EditorGui.OverlayPanel(drawList, cardMin, cardMax);
                    drawList.AddText(cardMin + new Vector2(frame + 4.0f, (caption + frame - ImGui.GetTextLineHeight()) * 0.5f),
                        ImGui.GetColorU32(palette.Text), $"{EditorIcons.Video}  {_context.Selection.Value.Name}");
                    drawList.AddImageRounded((IntPtr)_cameraPreviewFramebuffer.ColorAttachment, previewPos,
                        previewPos + new Vector2(previewWidth, previewHeight), new Vector2(0, 1), new Vector2(1, 0),
                        0xFFFFFFFF, 3.0f);
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
                    _lastLookPos = io.MousePos;
                    _skipLookFrame = true;
                }
                bool isFlyingCamera = _isFlyingCamera;

                // --- EDITOR ICONS (billboards for invisible entities: cameras, lights, sky) ---
                // Drawn before the gizmo so gizmo handles render on top; the hovered icon (if any) is
                // preferred over mesh picking when the user clicks. The hidden cursor belongs to
                // camera navigation while flying, so it must not highlight icons or show tooltips.
                Entity? hoveredIcon = null;
                if (_context.ActiveScene != null)
                {
                    hoveredIcon = _sceneIcons.Draw(_context.ActiveScene, _camera, cursorPos, viewportSize, isHovered && !isFlyingCamera);
                }

                // --- TRANSFORM GIZMO (2D & 3D: translate / rotate / scale) ---
                if (_context.Selection.HasValue && _context.Selection.Value.HasComponent<Transform>())
                {
                    var transform = _context.Selection.Value.GetComponent<Transform>();
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
                    // Hide the cursor and confine it to the viewport ourselves. The look delta is the
                    // plain frame-to-frame cursor movement; the cursor is only warped back to the centre
                    // when it nears a viewport border (see the edge guard below).
                    //
                    // Do NOT warp every frame and read the offset from centre as the delta: the warp
                    // happens during the ImGui pass, which runs a whole frame of update+render after the
                    // position was polled, so every motion the mouse made in between is thrown away by
                    // the snap. That loses a variable slice of each frame's movement — the camera feels
                    // both sluggish and jittery because the amount lost changes with frame time.
                    var mice = Spot.Engine.Application.Instance.Window.Input.Mice;
                    var mouse = mice.Count > 0 ? mice[0] : null;
                    if (mouse != null && mouse.Cursor.CursorMode != LockMode)
                    {
                        mouse.Cursor.CursorMode = LockMode;
                    }
                    io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;
                    _ownsCursorLock = true;

                    Vector2 mousePos = io.MousePos;
                    Vector2 lookDelta = _skipLookFrame ? Vector2.Zero : mousePos - _lastLookPos;
                    _skipLookFrame = false;
                    _lastLookPos = mousePos;
                    _flyFrame++;

                    // Edge guard: recentre only once the cursor gets close to a border, so it can never
                    // walk out of the viewport while staying free to accumulate real motion in between.
                    // Round to the nearest integer pixel: the OS stores an integer cursor position, so a
                    // fractional centre (.5f from an odd viewport dimension) would read back shifted and
                    // inject a constant sub-pixel delta that drifts the camera on its own.
                    Vector2 rawCenter = cursorPos + viewportSize * 0.5f;
                    Vector2 center = new Vector2(MathF.Round(rawCenter.X), MathF.Round(rawCenter.Y));
                    float margin = Math.Clamp(MathF.Min(viewportSize.X, viewportSize.Y) * 0.2f, 32.0f, 160.0f);
                    bool nearEdge = mousePos.X < cursorPos.X + margin
                        || mousePos.X > cursorPos.X + viewportSize.X - margin
                        || mousePos.Y < cursorPos.Y + margin
                        || mousePos.Y > cursorPos.Y + viewportSize.Y - margin;

                    if (mouse != null && (_flyFrame == 1 || nearEdge))
                    {
                        mouse.Position = center;
                        _lastLookPos = center;
                        // The warp's own position event may not land before the next poll, so the next
                        // frame could still report the pre-warp position. Skip one frame of look rather
                        // than turn that stale reading into a large bogus jump.
                        _skipLookFrame = true;
                    }

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
                        float speed = Utils.EditorSettings.CameraMoveSpeed; // units per second
                        if (ImGui.IsKeyDown(ImGuiKey.LeftShift)) speed *= 4.0f;
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

    // Spawns the dropped assets at the drop point and selects them. In 2D only the plane position comes from
    // the cursor: each asset keeps its own depth, which is its draw order there.
    private void DropAssets(Scene? scene, IReadOnlyList<string> paths, Vector3 point)
    {
        if (scene == null || _camera == null) return;

        List<Entity> spawned = AssetSpawner.SpawnAll(scene, paths);
        if (spawned.Count == 0) return;

        foreach (Entity root in spawned)
        {
            var transform = root.GetComponent<Transform>();
            transform.Position = _camera.Is3D ? point : new Vector3(point.X, point.Y, transform.Position.Z);
        }

        _context.SetSelectedEntities(spawned);
        EditorHistory.RecordSceneEdit(scene, AssetSpawner.AddLabel(paths));

        // Take focus: the scene becomes the active one and W/E/R/F act on the new selection straight away.
        ImGui.SetWindowFocus();
    }

    // While assets hover the viewport: a ring where they will land, one unit across and lying in the ground
    // plane (the z = 0 plane in 2D) so its perspective conveys the depth, labeled with what the drop creates.
    private void DrawDropPreview(IReadOnlyList<string> paths, Vector3 point, Vector2 cursorPos, Vector2 viewportSize)
    {
        if (_camera == null) return;

        Matrix4x4 vp = _camera.ViewProjection;
        if (!ScenePicker.TryProject(point, vp, cursorPos, viewportSize, out Vector2 center)) return;

        var palette = EditorThemeManager.Current.Palette;
        var drawList = ImGui.GetWindowDrawList();
        uint accent = ImGui.GetColorU32(palette.Accent);

        const int Segments = 32;
        const float Radius = 0.5f;
        Vector3 axisA = Vector3.UnitX;
        Vector3 axisB = _camera.Is3D ? Vector3.UnitZ : Vector3.UnitY;
        Span<Vector2> ring = stackalloc Vector2[Segments];
        bool ringVisible = true;
        for (int i = 0; i < Segments && ringVisible; i++)
        {
            float angle = i * MathF.Tau / Segments;
            Vector3 world = point + (axisA * MathF.Cos(angle) + axisB * MathF.Sin(angle)) * Radius;
            ringVisible = ScenePicker.TryProject(world, vp, cursorPos, viewportSize, out ring[i]);
        }
        if (ringVisible)
        {
            for (int i = 0; i < Segments; i++)
            {
                drawList.AddLine(ring[i], ring[(i + 1) % Segments], accent, 2.0f);
            }
        }
        drawList.AddCircleFilled(center, 3.0f, accent, 12);

        string label = paths.Count == 1
            ? $"{AssetSpawner.Describe(AssetSpawner.KindOf(paths[0]))}: {AssetSpawner.NameFor(paths[0])}"
            : $"{paths.Count} assets";

        DrawDropLabel(drawList, center, label, palette.Text);
    }

    // While a material hovers the viewport: the bounds of the mesh it would paint, outlined, and a label naming
    // the material and the mesh — or saying there is no mesh under the cursor to take it.
    private void DrawMaterialPreview(Entity? target, string material, Vector2 mouse, Vector2 cursorPos, Vector2 viewportSize)
    {
        if (_camera == null) return;

        var palette = EditorThemeManager.Current.Palette;
        var drawList = ImGui.GetWindowDrawList();
        string name = AssetSpawner.NameFor(material);

        if (target is not Entity mesh)
        {
            DrawDropLabel(drawList, mouse, $"No mesh here for '{name}'", palette.TextDisabled);
            return;
        }

        if (ScenePicker.TryGetMeshBounds(mesh, out var bounds))
        {
            Matrix4x4 model = mesh.GetComponent<Transform>().Matrix;
            Matrix4x4 vp = _camera.ViewProjection;
            Span<Vector2> corners = stackalloc Vector2[8];
            bool visible = true;
            for (int i = 0; i < 8 && visible; i++)
            {
                var local = new Vector3(
                    (i & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                    (i & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                    (i & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
                visible = ScenePicker.TryProject(Vector3.Transform(local, model), vp, cursorPos, viewportSize, out corners[i]);
            }

            if (visible)
            {
                // The twelve edges join corners whose indices differ in exactly one bit (one axis).
                uint accent = ImGui.GetColorU32(palette.Accent);
                for (int i = 0; i < 8; i++)
                {
                    for (int bit = 1; bit < 8; bit <<= 1)
                    {
                        if ((i & bit) == 0) drawList.AddLine(corners[i], corners[i | bit], accent, 2.0f);
                    }
                }
            }
        }

        DrawDropLabel(drawList, mouse, $"Apply '{name}' to {mesh.Name}", palette.Text);
    }

    // A small floating tag centred above a screen point; above, because the drag source's own tooltip sits
    // below-right of the cursor.
    private static void DrawDropLabel(ImDrawListPtr drawList, Vector2 anchor, string label, Vector4 color)
    {
        Vector2 size = ImGui.CalcTextSize(label);
        Vector2 textPos = anchor - new Vector2(size.X * 0.5f, size.Y + 18.0f);
        EditorGui.OverlayPanel(drawList, textPos - new Vector2(7, 4), textPos + size + new Vector2(7, 4), 4.0f);
        drawList.AddText(textPos, ImGui.GetColorU32(color), label);
    }

    // Restores the hardware cursor to its normal (visible, free) state and drops this panel's
    // ownership of the lock. Safe to call whether or not the cursor is currently captured.
    private void ReleaseCursorLock()
    {
        var mice = Spot.Engine.Application.Instance.Window.Input.Mice;
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
        drawList.AddCircleFilled(center, axisLen + 8.0f, ImGui.GetColorU32(WithAlpha(palette.HeaderBg, 0.45f)), 32);
        drawList.AddCircle(center, axisLen + 8.0f, ImGui.GetColorU32(WithAlpha(palette.Text, 0.06f)), 32, 1.0f);

        DrawOrientationAxis(drawList, center, Vector3.UnitX, "X", palette.AxisX, axisLen);
        DrawOrientationAxis(drawList, center, Vector3.UnitY, "Y", palette.AxisY, axisLen);
        DrawOrientationAxis(drawList, center, Vector3.UnitZ, "Z", palette.AxisZ, axisLen);

        // --- FPS + camera readout, right-aligned under the gizmo ---
        float rightEdge = cursorPos.X + viewportSize.X - 12.0f;
        float y = center.Y + axisLen + 12.0f;

        float fps = Spot.Engine.FrameStats.Fps;
        float ms = Spot.Engine.FrameStats.FrameTimeMs;
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

    // Right-aligned readout text with a soft shadow, so it stays legible over bright and dark scenes alike.
    private static void DrawRightText(ImDrawListPtr drawList, float rightEdge, ref float y, string text, Vector4 color)
    {
        Vector2 size = ImGui.CalcTextSize(text);
        EditorGui.OverlayText(drawList, new Vector2(rightEdge - size.X, y), color, text);
        y += size.Y + 2.0f;
    }

    private static Vector4 WithAlpha(Vector4 c, float a) => new(c.X, c.Y, c.Z, a);

    // The floating toolbar over the viewport's top-left corner, as two light translucent clusters so the scene
    // stays visible around them: the transform tools, then the view (2D toggle, collider/fullbright/wireframe
    // overlays, camera settings). Every button is a compact icon whose state reads at a glance — accent while
    // the tool is selected or the toggle on — and names its shortcut in a tooltip.
    private void DrawToolbar(Vector2 viewportMin)
    {
        if (_camera == null) return;

        float h = ImGui.GetFrameHeight();
        var square = new Vector2(h, h);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2.0f, 0.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4.0f, ImGui.GetStyle().FramePadding.Y));

        EditorGui.BeginToolbarCluster(viewportMin + new Vector2(8.0f, 8.0f));
        ToolButton(EditorIcons.Move, "Move (W)", GizmoMode.Translate, square);
        ImGui.SameLine();
        ToolButton(EditorIcons.Rotate, "Rotate (E)", GizmoMode.Rotate, square);
        ImGui.SameLine();
        ToolButton(EditorIcons.Scale, "Scale (R)", GizmoMode.Scale, square);
        Vector2 toolsMax = EditorGui.EndToolbarCluster();

        EditorGui.BeginToolbarCluster(new Vector2(toolsMax.X + 6.0f, viewportMin.Y + 8.0f));
        if (EditorGui.ToolbarButton("2D##mode", !_camera.Is3D,
                _camera.Is3D ? "2D view (orthographic, XY plane)" : "Back to the 3D view", new Vector2(h + 8.0f, h)))
        {
            _camera.ToggleMode();
        }
        EditorGui.ToolbarDivider();

        bool showColliders = Spot.Engine.Physics.PhysicsDebug.ShowColliders;
        if (EditorGui.ToolbarButton(EditorIcons.VectorSquare + "##colliders", showColliders, "Show colliders", square))
        {
            Spot.Engine.Physics.PhysicsDebug.ShowColliders = !showColliders;
        }
        ImGui.SameLine();
        bool fullbright = Spot.Engine.Graphics.RendererDebug.Fullbright;
        if (EditorGui.ToolbarButton(EditorIcons.Lightbulb + "##fullbright", fullbright, "Fullbright (ignore lighting)", square))
        {
            Spot.Engine.Graphics.RendererDebug.Fullbright = !fullbright;
        }
        ImGui.SameLine();
        bool wireframe = Spot.Engine.Graphics.RendererDebug.Wireframe;
        if (EditorGui.ToolbarButton(EditorIcons.DrawPolygon + "##wireframe", wireframe, "Wireframe", square))
        {
            Spot.Engine.Graphics.RendererDebug.Wireframe = !wireframe;
        }
        EditorGui.ToolbarDivider();

        // Camera feel (look sensitivity / fly speed). Tucked behind a gear so the toolbar stays uncluttered;
        // the values are global and persist with the window layout. Lit while its popup is open.
        bool settingsOpen = ImGui.IsPopupOpen("ViewportCameraSettings");
        if (EditorGui.ToolbarButton(EditorIcons.Gear + "##camera", settingsOpen, "Scene camera: look sensitivity and fly speed", square))
        {
            ImGui.OpenPopup("ViewportCameraSettings");
        }
        EditorGui.EndToolbarCluster();

        ImGui.PopStyleVar(2);
    }

    // A transform-tool button that stays highlighted while its gizmo mode is active.
    private void ToolButton(string glyph, string tooltip, GizmoMode mode, Vector2 size)
    {
        if (EditorGui.ToolbarButton(glyph, _gizmo.Mode == mode, tooltip, size))
        {
            _gizmo.Mode = mode;
        }
    }
}

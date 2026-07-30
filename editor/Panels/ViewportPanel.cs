using System;
using System.Numerics;
using ImGuiNET;
using Spot.Rendering;
using Spot.Editor.UI;

namespace Spot.Editor.Panels;

public class ViewportPanel
{
    private readonly EditorContext _context;
    private Framebuffer? _framebuffer;
    private Framebuffer? _cameraPreviewFramebuffer;
    private EditorCamera? _camera;
    private readonly TransformGizmo _gizmo = new();

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
        var viewportSize = ImGui.GetContentRegionAvail();

        if (_framebuffer != null && viewportSize.X > 0 && viewportSize.Y > 0)
        {
            _framebuffer.Resize((uint)viewportSize.X, (uint)viewportSize.Y);
            if (handleInput && _camera != null)
                _camera.SetViewportSize(viewportSize.X, viewportSize.Y);
            
            var cursorPos = ImGui.GetCursorScreenPos();
            ImGui.Image((IntPtr)_framebuffer.ColorAttachment, viewportSize, new Vector2(0, 1), new Vector2(1, 0));
            bool isHovered = ImGui.IsItemHovered();

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
                DrawGizmoModeButton("Move", GizmoMode.Translate);
                ImGui.SameLine();
                DrawGizmoModeButton("Rotate", GizmoMode.Rotate);
                ImGui.SameLine();
                DrawGizmoModeButton("Scale", GizmoMode.Scale);

                if (_cameraPreviewFramebuffer != null && _context.Selection.HasValue && _context.Selection.Value.HasComponent<Spot.Scenes.CameraComponent>())
                {
                    // Render Camera Preview in bottom right
                    float previewWidth = 320;
                    float previewHeight = 180;
                    
                    var previewPos = cursorPos + viewportSize - new Vector2(previewWidth + 20, previewHeight + 20);
                    
                    // Draw a small background/border for it
                    var drawList = ImGui.GetWindowDrawList();
                    drawList.AddRectFilled(previewPos - new Vector2(2, 2), previewPos + new Vector2(previewWidth + 2, previewHeight + 2), ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)));
                    drawList.AddImage((IntPtr)_cameraPreviewFramebuffer.ColorAttachment, previewPos, previewPos + new Vector2(previewWidth, previewHeight), new Vector2(0, 1), new Vector2(1, 0));
                    
                    drawList.AddText(previewPos + new Vector2(5, 5), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), "Camera Preview");
                }

                var io = ImGui.GetIO();
                
                // --- TRANSFORM GIZMO (2D & 3D: translate / rotate / scale) ---
                if (_context.Selection.HasValue && _context.Selection.Value.HasComponent<Transform>())
                {
                    var transform = _context.Selection.Value.GetComponent<Transform>();
                    _gizmo.Draw(transform, _camera, cursorPos, viewportSize, isHovered);

                    // Unity-style mode switch. Guarded so it does not fire while flying the 3D
                    // camera with the right mouse button (which uses W/A/S/D for movement).
                    if (isHovered && !ImGui.IsMouseDown(ImGuiMouseButton.Right))
                    {
                        if (ImGui.IsKeyPressed(ImGuiKey.W)) _gizmo.Mode = GizmoMode.Translate;
                        if (ImGui.IsKeyPressed(ImGuiKey.E)) _gizmo.Mode = GizmoMode.Rotate;
                        if (ImGui.IsKeyPressed(ImGuiKey.R)) _gizmo.Mode = GizmoMode.Scale;
                    }
                }
                
                // --- CAMERA CONTROLS ---
                if (isHovered && !_gizmo.IsUsing)
                {
                    if (io.MouseWheel != 0.0f)
                    {
                        _camera.OnMouseScroll(io.MouseWheel);
                    }
                }

                if (!_gizmo.IsUsing)
                {
                    if (_camera.Is3D && ImGui.IsMouseDown(ImGuiMouseButton.Right) && (isHovered || ImGui.IsMouseDragging(ImGuiMouseButton.Right, 0)))
                    {
                        // Lock cursor
                        var mice = Spot.Core.Application.Instance.Window.Input.Mice;
                        if (mice.Count > 0) mice[0].Cursor.CursorMode = Silk.NET.Input.CursorMode.Raw;

                        // 3D Mouselook
                        _camera.MouseLook(io.MouseDelta);
                        
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
                        // Unlock cursor
                        var mice = Spot.Core.Application.Instance.Window.Input.Mice;
                        if (mice.Count > 0 && mice[0].Cursor.CursorMode == Silk.NET.Input.CursorMode.Raw)
                        {
                            mice[0].Cursor.CursorMode = Silk.NET.Input.CursorMode.Normal;
                        }

                        if ((isHovered || ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right)) && 
                            (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || (! _camera.Is3D && ImGui.IsMouseDragging(ImGuiMouseButton.Right))))
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

    // A gizmo-mode toolbar button that stays highlighted while its mode is the active one.
    private void DrawGizmoModeButton(string label, GizmoMode mode)
    {
        bool active = _gizmo.Mode == mode;
        if (active)
        {
            var accent = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive];
            ImGui.PushStyleColor(ImGuiCol.Button, accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, accent);
        }
        if (ImGui.Button(label))
        {
            _gizmo.Mode = mode;
        }
        if (active)
        {
            ImGui.PopStyleColor(2);
        }
    }
}

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
    private EditorCamera? _camera;
    private bool _isDraggingX = false;
    private bool _isDraggingY = false;

    public ViewportPanel(EditorContext context)
    {
        _context = context;
    }

    public void SetFramebuffer(Framebuffer framebuffer)
    {
        _framebuffer = framebuffer;
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
                // Toggle Button overlay
                ImGui.SetCursorScreenPos(cursorPos + new Vector2(10, 10));
                if (ImGui.Button(_camera.Is3D ? "3D Mode" : "2D Mode"))
                {
                    _camera.ToggleMode();
                }

                var io = ImGui.GetIO();
                
                // --- CUSTOM GIZMO (2D ONLY) ---
                if (!_camera.Is3D && _context.Selection.HasValue && _context.Selection.Value.HasComponent<Transform>())
                {
                    var entity = _context.Selection.Value;
                    var transform = entity.GetComponent<Transform>();
                    var drawList = ImGui.GetWindowDrawList();

                    float unitsPerPixel = _camera.GetUnitsPerPixel();
                    var cameraPos = _camera.Position;
                    
                    Vector2 screenCenter = cursorPos + viewportSize * 0.5f;
                    Vector2 screenEntityPos = new Vector2(
                        screenCenter.X + (transform.WorldPosition.X - cameraPos.X) / unitsPerPixel,
                        screenCenter.Y - (transform.WorldPosition.Y - cameraPos.Y) / unitsPerPixel
                    );

                    float arrowLength = 60.0f;
                    float hitRadius = 10.0f;
                    
                    Vector2 arrowXEnd = screenEntityPos + new Vector2(arrowLength, 0);
                    Vector2 arrowYEnd = screenEntityPos + new Vector2(0, -arrowLength);
                    
                    Vector2 mousePos = io.MousePos;

                    // Hover logic
                    bool hoverX = Math.Abs(mousePos.Y - screenEntityPos.Y) < hitRadius && mousePos.X > screenEntityPos.X && mousePos.X < arrowXEnd.X;
                    bool hoverY = Math.Abs(mousePos.X - screenEntityPos.X) < hitRadius && mousePos.Y < screenEntityPos.Y && mousePos.Y > arrowYEnd.Y;

                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        if (hoverX) _isDraggingX = true;
                        else if (hoverY) _isDraggingY = true;
                    }
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    {
                        _isDraggingX = false;
                        _isDraggingY = false;
                    }

                    if (_isDraggingX)
                    {
                        var position = transform.Position;
                        position.X += io.MouseDelta.X * unitsPerPixel;
                        transform.Position = position;
                    }
                    if (_isDraggingY)
                    {
                        var position = transform.Position;
                        position.Y -= io.MouseDelta.Y * unitsPerPixel;
                        transform.Position = position;
                    }

                    // Drawing
                    var palette = EditorThemeManager.Current.Palette;
                    uint colorX = ImGui.GetColorU32(hoverX || _isDraggingX ? palette.GizmoHover : palette.AxisX);
                    uint colorY = ImGui.GetColorU32(hoverY || _isDraggingY ? palette.GizmoHover : palette.AxisY);

                    drawList.AddLine(screenEntityPos, arrowXEnd, colorX, 3.0f);
                    drawList.AddLine(screenEntityPos, arrowYEnd, colorY, 3.0f);

                    // Center square
                    drawList.AddRectFilled(screenEntityPos - new Vector2(4, 4), screenEntityPos + new Vector2(4, 4), 0xFFFFFFFF);
                }
                
                // --- CAMERA CONTROLS ---
                if (isHovered && !_isDraggingX && !_isDraggingY)
                {
                    if (io.MouseWheel != 0.0f)
                    {
                        _camera.OnMouseScroll(io.MouseWheel);
                    }
                }
                
                if (!_isDraggingX && !_isDraggingY)
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
}

using System;
using System.Numerics;
using ImGuiNET;
using Spot.Rendering;

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

            if (handleInput && _camera != null)
            {
                var io = ImGui.GetIO();
                bool isHovered = ImGui.IsItemHovered();
                
                // --- CUSTOM 2D GIZMO ---
                if (_context.Selection.HasValue && _context.Selection.Value.HasComponent<Transform>())
                {
                    var entity = _context.Selection.Value;
                    var transform = entity.GetComponent<Transform>();
                    var drawList = ImGui.GetWindowDrawList();

                    float unitsPerPixel = _camera.GetUnitsPerPixel();
                    var cameraPos = _camera.Camera.Position;
                    
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
                    uint colorX = hoverX || _isDraggingX ? 0xFF00FFFF : 0xFF0000FF; // Yellow when hovered/active, Red otherwise
                    uint colorY = hoverY || _isDraggingY ? 0xFF00FFFF : 0xFF00FF00; // Yellow when hovered/active, Green otherwise

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

                    if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right))
                    {
                        _camera.OnMouseDrag(io.MouseDelta);
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

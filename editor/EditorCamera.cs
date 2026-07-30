using System;
using System.Numerics;
using Spot.Rendering;

namespace Spot.Editor;

public class EditorCamera
{
    private float _aspectRatio = 1.0f;
    private float _zoomLevel = 2.0f;
    public float ZoomLevel => _zoomLevel;
    private float _viewportHeight = 720.0f;
    
    // Camera state
    public Vector3 Position { get; set; } = new Vector3(0, 0, 0);
    public float Pitch { get; set; } = 0.0f;
    public float Yaw { get; set; } = 0.0f;
    public bool Is3D { get; set; } = false;
    
    private Matrix4x4 _projection;
    private Matrix4x4 _view;
    
    public Matrix4x4 ViewProjection => _view * _projection;
    
    public EditorCamera()
    {
        UpdateProjection();
        UpdateView();
    }
    
    public void SetViewportSize(float width, float height)
    {
        _aspectRatio = width / height;
        _viewportHeight = height;
        UpdateProjection();
    }
    
    public void OnMouseScroll(float delta)
    {
        if (Is3D)
        {
            Vector3 forward = GetForwardDirection();
            Position += forward * delta * 0.5f;
            UpdateView();
        }
        else
        {
            _zoomLevel -= delta * 0.25f;
            _zoomLevel = Math.Max(_zoomLevel, 0.25f);
            UpdateProjection();
        }
    }
    
    public void OnMouseDrag(Vector2 delta)
    {
        if (Is3D)
        {
            Vector3 right = GetRightDirection();
            Vector3 up = GetUpDirection();
            
            Position -= right * delta.X * 0.01f;
            Position += up * delta.Y * 0.01f;
            UpdateView();
        }
        else
        {
            float unitsPerPixel = GetUnitsPerPixel();
            Position -= new Vector3(delta.X * unitsPerPixel, -delta.Y * unitsPerPixel, 0);
            UpdateView();
        }
    }
    
    public void MouseLook(Vector2 delta)
    {
        if (!Is3D) return;
        
        Yaw += delta.X * 0.005f;
        Pitch -= delta.Y * 0.005f;
        
        // Clamp pitch to avoid gimbal lock
        Pitch = Math.Clamp(Pitch, -MathF.PI / 2.0f + 0.01f, MathF.PI / 2.0f - 0.01f);
        UpdateView();
    }
    
    public void Move(Vector3 direction, float speed)
    {
        if (!Is3D) return;
        
        Vector3 forward = GetForwardDirection();
        Vector3 right = GetRightDirection();
        Vector3 up = Vector3.UnitY; // Move relative to world up to avoid flying when looking down
        
        Position += forward * direction.Z * speed;
        Position += right * direction.X * speed;
        Position += up * direction.Y * speed;
        
        UpdateView();
    }
    
    public void UpdateProjection()
    {
        if (Is3D)
        {
            _projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.0f, _aspectRatio, 0.1f, 1000.0f);
        }
        else
        {
            _projection = Matrix4x4.CreateOrthographicOffCenter(-_aspectRatio * _zoomLevel, _aspectRatio * _zoomLevel, -_zoomLevel, _zoomLevel, -1.0f, 1.0f);
        }
    }
    
    public void UpdateView()
    {
        if (Is3D)
        {
            Vector3 forward = GetForwardDirection();
            _view = Matrix4x4.CreateLookAt(Position, Position + forward, Vector3.UnitY);
        }
        else
        {
            Matrix4x4 transform = Matrix4x4.CreateTranslation(Position);
            Matrix4x4.Invert(transform, out _view);
        }
    }
    
    private Vector3 GetForwardDirection()
    {
        return new Vector3(
            MathF.Cos(Pitch) * MathF.Sin(Yaw),
            MathF.Sin(Pitch),
            -MathF.Cos(Pitch) * MathF.Cos(Yaw)
        );
    }
    
    private Vector3 GetRightDirection()
    {
        return Vector3.Normalize(Vector3.Cross(GetForwardDirection(), Vector3.UnitY));
    }
    
    private Vector3 GetUpDirection()
    {
        return Vector3.Normalize(Vector3.Cross(GetRightDirection(), GetForwardDirection()));
    }

    public float GetUnitsPerPixel()
    {
        if (Is3D) return 0;
        return 2.0f * _zoomLevel / _viewportHeight;
    }
    
    public void ToggleMode()
    {
        Is3D = !Is3D;
        if (Is3D)
        {
            Position = new Vector3(Position.X, Position.Y, 10);
            Pitch = 0;
            Yaw = 0;
        }
        else
        {
            Position = new Vector3(Position.X, Position.Y, 0);
        }
        UpdateProjection();
        UpdateView();
    }
    
    public void Focus(Vector3 targetPosition)
    {
        if (Is3D)
        {
            Vector3 backward = -GetForwardDirection();
            Position = targetPosition + backward * 10.0f;
        }
        else
        {
            Position = new Vector3(targetPosition.X, targetPosition.Y, 0);
        }
        UpdateView();
    }
}

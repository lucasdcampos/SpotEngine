using System.Numerics;
using Spot.Editor;
using Spot.Editor.Utils;

namespace Spot.Engine.Tests;

public class EditorCameraTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void RestoreState_AppliesSavedViewBeforeAnyInput(bool initialIs3D, bool savedIs3D)
    {
        bool previousMode = EditorSettings.GlobalIs3DMode;
        try
        {
            EditorSettings.GlobalIs3DMode = initialIs3D;
            var camera = new EditorCamera();
            camera.SetViewportSize(1280, 720);
            var state = new SceneCameraState
            {
                Is3D = savedIs3D,
                PosX = 12,
                PosY = 7,
                PosZ = savedIs3D ? -3 : 0,
                Pitch = -0.3f,
                Yaw = 0.8f,
                Zoom = 5,
            };

            camera.RestoreState(state);

            Vector3 position = new(state.PosX, state.PosY, state.PosZ);
            Matrix4x4 view = savedIs3D
                ? Matrix4x4.CreateLookAt(position, position + camera.Forward, Vector3.UnitY)
                : Matrix4x4.CreateTranslation(-position);
            const float aspect = 1280f / 720f;
            Matrix4x4 projection = savedIs3D
                ? Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, aspect, 0.1f, 1000)
                : Matrix4x4.CreateOrthographicOffCenter(-aspect * state.Zoom, aspect * state.Zoom,
                    -state.Zoom, state.Zoom, -1, 1);

            Assert.Equal(savedIs3D, camera.Is3D);
            Assert.Equal(position, camera.Position);
            Assert.Equal(state.Pitch, camera.Pitch);
            Assert.Equal(state.Yaw, camera.Yaw);
            Assert.Equal(state.Zoom, camera.ZoomLevel);
            Assert.Equal(view * projection, camera.ViewProjection);

            Matrix4x4 restored = camera.ViewProjection;
            if (savedIs3D) camera.Move(Vector3.Zero, 1);
            else camera.OnMouseDrag(Vector2.Zero);
            Assert.Equal(restored, camera.ViewProjection);
        }
        finally
        {
            EditorSettings.GlobalIs3DMode = previousMode;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NewCamera_LooksAtOriginBeforeAnyInput(bool is3D)
    {
        bool previousMode = EditorSettings.GlobalIs3DMode;
        try
        {
            EditorSettings.GlobalIs3DMode = is3D;
            var camera = new EditorCamera();

            if (is3D)
            {
                Assert.True(camera.Position.X > 0 && camera.Position.Y > 0 && camera.Position.Z > 0);
                Assert.True(Vector3.Dot(camera.Forward, Vector3.Normalize(-camera.Position)) > 0.9999f);
            }
            else
            {
                Assert.Equal(Vector3.Zero, camera.Position);
            }

            Vector4 origin = Vector4.Transform(new Vector4(0, 0, 0, 1), camera.ViewProjection);
            Assert.True(origin.W > 0);
            Assert.InRange(MathF.Abs(origin.X / origin.W), 0, 0.0001f);
            Assert.InRange(MathF.Abs(origin.Y / origin.W), 0, 0.0001f);
            Assert.InRange(origin.Z / origin.W, 0, 1);
        }
        finally
        {
            EditorSettings.GlobalIs3DMode = previousMode;
        }
    }
}

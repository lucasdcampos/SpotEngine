using System;
using System.Numerics;
using Silk.NET.OpenGL;
using Spot.Assets;
using Spot.Physics;
using Spot.Rendering;

namespace Spot.DebugUI.UI;

/// <summary>
/// Renders a live 3D preview of a <see cref="Model"/> into an offscreen framebuffer for display in the
/// editor (for example as an asset-browser thumbnail). The model is drawn with a neutral material and a
/// three-quarter camera framed on its local bounds, so the shape reads clearly regardless of its scale.
/// </summary>
public static class ModelPreviewHelper
{
    // A neutral light-gray material so previews focus on the model's silhouette, not its authored colors.
    private static readonly Material s_previewMaterial = new() { Color = new Vector4(0.78f, 0.78f, 0.80f, 1.0f) };

    public static void RenderToFramebuffer(Model model, Spot.Rendering.Framebuffer framebuffer)
    {
        Renderer.Api.GetInteger(GLEnum.FramebufferBinding, out int prevFb);
        int[] prevViewport = new int[4];
        unsafe
        {
            fixed (int* vp = prevViewport)
            {
                Renderer.Api.GetInteger(GLEnum.Viewport, vp);
            }
        }

        framebuffer.Bind();
        Renderer.SetClearColor(0.15f, 0.15f, 0.15f, 1.0f);
        Renderer.Clear();
        Renderer.SetDepthTest(true);
        Renderer.SetFaceCulling(true);

        // Frame the model by its bounding sphere: distance so the sphere just fits the vertical FOV, plus padding.
        Aabb3d bounds = model.LocalBounds;
        Vector3 center = bounds.Center;
        float radius = bounds.HalfExtents.Length();
        if (radius < 1e-4f)
        {
            radius = 1.0f;
        }

        const float fov = MathF.PI / 4f;
        float dist = radius / MathF.Sin(fov * 0.5f) * 1.25f;
        Vector3 dir = Vector3.Normalize(new Vector3(1.0f, 0.7f, 1.0f));
        Vector3 cameraPos = center + dir * dist;

        Matrix4x4 view = Matrix4x4.CreateLookAt(cameraPos, center, Vector3.UnitY);
        float near = MathF.Max(0.01f, dist - radius * 2.0f);
        float far = dist + radius * 2.0f;
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, (float)framebuffer.Width / framebuffer.Height, near, far);

        Span<Renderer3D.PointLightData> pointLights = stackalloc Renderer3D.PointLightData[1];
        pointLights[0] = new Renderer3D.PointLightData
        {
            Position = cameraPos,
            Color = Vector3.One,
            Intensity = 0.6f,
            Range = dist * 4.0f
        };

        Renderer3D.BeginScene(
            view * proj,
            true,
            Vector3.Normalize(new Vector3(-0.5f, -1.0f, -0.5f)),
            Vector3.One,
            0.3f,
            Matrix4x4.Identity,
            false,
            pointLights,
            cameraPos);

        foreach (Mesh mesh in model.Meshes)
        {
            Renderer3D.DrawMesh(Matrix4x4.Identity, mesh, s_previewMaterial.Color, null, (int)s_previewMaterial.ShaderType, s_previewMaterial);
        }

        Renderer3D.EndScene();

        Renderer.Api.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)prevFb);
        Renderer.Api.Viewport(prevViewport[0], prevViewport[1], (uint)prevViewport[2], (uint)prevViewport[3]);
    }
}

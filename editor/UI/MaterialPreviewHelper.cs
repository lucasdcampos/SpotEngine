using System;
using System.Numerics;
using Silk.NET.OpenGL;
using Spot.Assets;
using Spot.Rendering;

namespace Spot.Editor.UI;

public static class MaterialPreviewHelper
{
    private static Model? s_sphereModel;

    public static void RenderToFramebuffer(Material material, Spot.Rendering.Framebuffer framebuffer)
    {
        if (s_sphereModel == null)
        {
            s_sphereModel = PrimitiveModelFactory.Create("sphere");
        }

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

        Vector3 cameraPos = new Vector3(0, 0, 1.2f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(cameraPos, Vector3.Zero, Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, (float)framebuffer.Width / framebuffer.Height, 0.1f, 10f);
        
        // Optionally add a point light for better material visualization
        Span<Renderer3D.PointLightData> pointLights = stackalloc Renderer3D.PointLightData[1];
        pointLights[0] = new Renderer3D.PointLightData 
        {
            Position = new Vector3(1.0f, 1.0f, 1.0f),
            Color = Vector3.One,
            Intensity = 0.8f,
            Range = 5.0f
        };
        
        Renderer3D.BeginScene(view * proj, true, Vector3.Normalize(new Vector3(-0.5f, -1.0f, -0.5f)), Vector3.One, 0.3f, Matrix4x4.Identity, false, pointLights);

        Matrix4x4 model = Matrix4x4.Identity;
        Renderer3D.DrawMesh(model, s_sphereModel.Meshes[0], material.Color, material.Texture, (int)material.ShaderType, material);
        Renderer3D.EndScene();

        Renderer.Api.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)prevFb);
        Renderer.Api.Viewport(prevViewport[0], prevViewport[1], (uint)prevViewport[2], (uint)prevViewport[3]);
    }
}

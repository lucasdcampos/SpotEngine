using Spot.DebugUI.UI;
using Spot.Engine.Assets;
using Spot.Engine.Graphics;
using Spot.Tests.Fakes;

namespace Spot.Engine.Tests;

/// <summary>
/// Guards the editor's offscreen previews (asset thumbnails, the material inspector) against leaving their
/// framebuffer bound. Creating a framebuffer re-binds the renderer's tracked target, so a preview that restored
/// the target behind the renderer's back made the next preview's creation re-bind the previous one — and the rest
/// of the frame, the editor UI included, drew into that thumbnail.
/// </summary>
public class PreviewRenderTargetTests
{
    private static (RecordingGraphicsDevice Device, Framebuffer EditorTarget) Install()
    {
        var device = new RecordingGraphicsDevice();
        Renderer.Init(device);
        Renderer3D.Init();
        EngineAssets.Install();
        var editorTarget = new Framebuffer(640, 360);
        editorTarget.Bind();
        return (device, editorTarget);
    }

    [Fact]
    public void SeveralPreviewsInOneFrame_EachRestoreTheEditorsTarget()
    {
        (RecordingGraphicsDevice device, Framebuffer editorTarget) = Install();
        using Framebuffer target = editorTarget;

        using var first = new Framebuffer(128, 128);
        ModelPreviewHelper.RenderToFramebuffer(BuiltinAssets.LoadModel("builtin:Mesh/Cube"), first);
        AssertEditorTargetBound(device, target);

        // Creating the next preview re-binds the tracked target, which must be the editor's, not the first preview.
        using var second = new Framebuffer(128, 128);
        Assert.Equal(target.Handle.Id, device.BoundFramebuffer);

        MaterialPreviewHelper.RenderToFramebuffer(BuiltinAssets.LoadMaterial("builtin:Material/Checker"), second);
        AssertEditorTargetBound(device, target);

        using var third = new Framebuffer(128, 128);
        Assert.Equal(target.Handle.Id, device.BoundFramebuffer);
    }

    [Fact]
    public void Previews_DrawIntoTheirOwnFramebuffer()
    {
        (RecordingGraphicsDevice device, Framebuffer editorTarget) = Install();
        using Framebuffer target = editorTarget;
        using var preview = new Framebuffer(128, 128);
        int drawsBefore = device.Draws.Count;

        ModelPreviewHelper.RenderToFramebuffer(BuiltinAssets.LoadModel("builtin:Mesh/Capsule"), preview);

        Assert.True(device.Draws.Count > drawsBefore);
        Assert.Equal(target.Handle.Id, device.BoundFramebuffer);
    }

    [Fact]
    public void TransparentPreviews_ClearToZeroAlpha_OthersKeepAnOpaqueBackdrop()
    {
        (RecordingGraphicsDevice device, Framebuffer editorTarget) = Install();
        using Framebuffer target = editorTarget;
        using var preview = new Framebuffer(128, 128);

        ModelPreviewHelper.RenderToFramebuffer(BuiltinAssets.LoadModel("builtin:Mesh/Cube"), preview, transparentBackground: true);
        Assert.Equal(0.0f, device.ClearColor.W);

        MaterialPreviewHelper.RenderToFramebuffer(BuiltinAssets.LoadMaterial("builtin:Material/Checker"), preview, transparentBackground: true);
        Assert.Equal(0.0f, device.ClearColor.W);

        MaterialPreviewHelper.RenderToFramebuffer(BuiltinAssets.LoadMaterial("builtin:Material/Checker"), preview);
        Assert.Equal(1.0f, device.ClearColor.W);
    }

    [Fact]
    public void AFailedPreview_StillRestoresTheTarget()
    {
        (RecordingGraphicsDevice device, Framebuffer editorTarget) = Install();
        using Framebuffer target = editorTarget;
        using var preview = new Framebuffer(128, 128);

        Assert.ThrowsAny<Exception>(() => ModelPreviewHelper.RenderToFramebuffer(null!, preview));

        AssertEditorTargetBound(device, target);
    }

    private static void AssertEditorTargetBound(RecordingGraphicsDevice device, Framebuffer target)
    {
        Assert.Equal(target.Handle, Renderer.CurrentRenderTarget);
        Assert.Equal(target.Handle.Id, device.BoundFramebuffer);
        Assert.Equal((0, 0, 640u, 360u), device.Viewport);
        Assert.False(device.Capabilities.GetValueOrDefault(GraphicsCapability.CullFace));
    }
}

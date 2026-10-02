namespace Spot.Rendering;

/// <summary>
/// The color format of a <see cref="Framebuffer"/>.
/// </summary>
public enum FramebufferFormat
{
    /// <summary>8-bit RGBA: ordinary color.</summary>
    RGBA8,

    /// <summary>16-bit float RGBA: HDR color, for lighting that exceeds 1.0 before tone mapping.</summary>
    RGBA16F,
}

/// <summary>
/// An offscreen render target: a color texture plus a depth-stencil texture you can draw into instead of the
/// screen, then sample (post-processing, minimaps, editor viewports). Issued entirely through
/// <see cref="Renderer.Device"/>, so it works on every backend.
/// </summary>
public sealed class Framebuffer : IDisposable
{
    private readonly IGraphicsDevice _device;
    private FramebufferHandle _handle;
    private TextureHandle _color;
    private TextureHandle _depth;

    /// <summary>
    /// Creates the target and its attachments.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="format">The color format.</param>
    /// <exception cref="InvalidOperationException">The backend reports the target incomplete.</exception>
    public Framebuffer(uint width, uint height, FramebufferFormat format = FramebufferFormat.RGBA8)
    {
        _device = Renderer.Device;
        Width = width;
        Height = height;
        Format = format;
        Invalidate();
    }

    /// <summary>Gets the width in pixels.</summary>
    public uint Width { get; private set; }

    /// <summary>Gets the height in pixels.</summary>
    public uint Height { get; private set; }

    /// <summary>Gets the color format.</summary>
    public FramebufferFormat Format { get; }

    /// <summary>Gets the color texture, for sampling what was rendered.</summary>
    public TextureHandle ColorTexture => _color;

    /// <summary>Gets the depth-stencil texture.</summary>
    public TextureHandle DepthTexture => _depth;

    /// <summary>
    /// Gets the color texture's native name — what UI layers such as ImGui take to display it
    /// (<c>ImGui.Image((IntPtr)framebuffer.ColorAttachment, ...)</c>).
    /// </summary>
    public uint ColorAttachment => _color.Id;

    /// <summary>
    /// Gets the target as a device handle, for <see cref="Renderer.BindRenderTarget"/> or raw device calls.
    /// </summary>
    public FramebufferHandle Handle => _handle;

    /// <summary>
    /// Makes this the draw target, with a viewport covering all of it.
    /// </summary>
    public void Bind() => Renderer.BindRenderTarget(_handle, 0, 0, Width, Height);

    /// <summary>
    /// Makes the screen the draw target again. The viewport is left as it is.
    /// </summary>
    public void Unbind() => Renderer.BindRenderTarget(
        FramebufferHandle.Default, Renderer.ViewportX, Renderer.ViewportY, Renderer.ViewportWidth, Renderer.ViewportHeight);

    /// <summary>
    /// Copies this target's depth buffer into another target. Use it after compositing an offscreen render (e.g.
    /// post-processing) back into a target, so geometry drawn into the target afterwards is correctly occluded
    /// by what was rendered here. Leaves the destination bound.
    /// </summary>
    /// <param name="targetFramebuffer">The destination framebuffer's native name (0 for the screen).</param>
    /// <param name="x">Destination region x origin.</param>
    /// <param name="y">Destination region y origin.</param>
    /// <param name="width">Destination region width.</param>
    /// <param name="height">Destination region height.</param>
    public void BlitDepthTo(uint targetFramebuffer, int x, int y, uint width, uint height) =>
        _device.BlitDepth(_handle, new FramebufferHandle(targetFramebuffer), Width, Height, x, y, width, height);

    /// <summary>
    /// Recreates the attachments at a new size. A zero size or an unchanged one is ignored.
    /// </summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    public void Resize(uint width, uint height)
    {
        if (width == 0 || height == 0 || (Width == width && Height == height))
        {
            return;
        }

        Width = width;
        Height = height;
        Invalidate();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_handle.Id == 0)
        {
            return;
        }

        _device.DeleteFramebuffer(_handle);
        _device.DeleteTexture(_color);
        _device.DeleteTexture(_depth);
        _handle = default;
        _color = default;
        _depth = default;
    }

    private void Invalidate()
    {
        Dispose();

        _handle = _device.CreateFramebuffer();
        _device.BindFramebuffer(_handle);

        _color = _device.CreateTexture();
        _device.BindTexture(0, _color);
        _device.TextureImage2D(
            Format == FramebufferFormat.RGBA16F ? TextureInternalFormat.Rgba16F : TextureInternalFormat.Rgba8,
            Width, Height, ReadOnlySpan<byte>.Empty);
        _device.SetTextureFilter(TextureFilter.Linear, TextureFilter.Linear);
        _device.FramebufferTexture2D(RenderTargetAttachment.Color0, _color);

        _depth = _device.CreateTexture();
        _device.BindTexture(0, _depth);
        _device.TextureImage2D(TextureInternalFormat.Depth24Stencil8, Width, Height, ReadOnlySpan<byte>.Empty);
        _device.FramebufferTexture2D(RenderTargetAttachment.DepthStencil, _depth);

        bool complete = _device.CheckFramebufferComplete();

        // Leave whatever target was bound before (tracked by the renderer) bound again.
        _device.BindFramebuffer(Renderer.CurrentRenderTarget);

        if (!complete)
        {
            Dispose();
            throw new InvalidOperationException("Framebuffer is incomplete.");
        }
    }
}

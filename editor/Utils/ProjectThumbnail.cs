using System;
using System.Buffers;
using System.IO;
using Silk.NET.OpenGL;
using Spot.Engine;
using Spot.Framework;
using Spot.Framework.Graphics;
using Framebuffer = Spot.Framework.Graphics.Framebuffer;

namespace Spot.Editor.Utils;

/// <summary>
/// The preview picture the launcher shows on a project's card: a small 16:9 snapshot of the editor viewport,
/// written to the project's <c>Library</c> folder (editor-only, out of version control) whenever the editor
/// saves the active scene or closes. Capturing is best-effort: a failure is logged and never interrupts the
/// save it piggybacks on.
/// </summary>
public static class ProjectThumbnail
{
    public const string FileName = "thumbnail.png";

    /// <summary>Stored size. Cards draw it at up to ~260 px wide, so this stays sharp on high-DPI screens.</summary>
    public const int Width = 480;
    public const int Height = 270;

    /// <summary>Where a project's thumbnail lives (it may not exist yet).</summary>
    public static string PathFor(string projectDirectory) =>
        Path.Combine(projectDirectory, ProjectStructure.LibraryFolder, FileName);

    /// <summary>
    /// Reads back <paramref name="source"/> (the viewport's render target), crops it to 16:9, scales it down
    /// and saves it as the project's thumbnail. A blank frame (a viewport that has not drawn yet, or a game
    /// view with no camera) is skipped so it never replaces a meaningful picture. Must run on the render thread.
    /// </summary>
    public static void Capture(Framebuffer source, string projectDirectory)
    {
        if (string.IsNullOrEmpty(projectDirectory) || source.Width < 16 || source.Height < 16) return;

        // A full-screen 4K viewport is ~33 MB of pixels; rent the buffer rather than churn the large-object heap
        // on every save.
        byte[] pixels = ArrayPool<byte>.Shared.Rent((int)(source.Width * source.Height * 4));
        try
        {
            ReadPixels(source, pixels);
            Image? thumbnail = Downscale(pixels, (int)source.Width, (int)source.Height);
            if (thumbnail != null)
            {
                thumbnail.SavePng(PathFor(projectDirectory));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not save the project thumbnail: {0}", ex.Message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }
    }

    // The editor is desktop-only, so the readback goes through the raw OpenGL escape hatch. The target is bound
    // and restored through the renderer so its tracked state never goes stale.
    private static void ReadPixels(Framebuffer source, byte[] pixels)
    {
        FramebufferHandle previousTarget = Renderer.CurrentRenderTarget;
        int x = Renderer.ViewportX, y = Renderer.ViewportY;
        uint w = Renderer.ViewportWidth, h = Renderer.ViewportHeight;
        try
        {
            source.Bind();
            Renderer.Api.ReadPixels(0, 0, source.Width, source.Height, PixelFormat.Rgba, PixelType.UnsignedByte,
                pixels.AsSpan(0, (int)(source.Width * source.Height * 4)));
        }
        finally
        {
            Renderer.BindRenderTarget(previousTarget, x, y, w, h);
        }
    }

    // Center-crops the bottom-up RGBA frame to 16:9 and box-filters it down to Width x Height. Returns null when
    // the frame is effectively one flat color.
    private static Image? Downscale(byte[] src, int srcW, int srcH)
    {
        float targetAspect = (float)Width / Height;
        int cropW = srcW, cropH = srcH;
        if ((float)srcW / srcH > targetAspect) cropW = (int)(srcH * targetAspect);
        else cropH = (int)(srcW / targetAspect);
        int offX = (srcW - cropW) / 2, offY = (srcH - cropH) / 2;

        int outW = Math.Min(Width, cropW), outH = Math.Min(Height, cropH);
        var dst = new byte[outW * outH * 4];
        double lumaSum = 0, lumaSqSum = 0;

        for (int oy = 0; oy < outH; oy++)
        {
            int sy0 = offY + oy * cropH / outH, sy1 = Math.Max(sy0 + 1, offY + (oy + 1) * cropH / outH);
            for (int ox = 0; ox < outW; ox++)
            {
                int sx0 = offX + ox * cropW / outW, sx1 = Math.Max(sx0 + 1, offX + (ox + 1) * cropW / outW);
                int r = 0, g = 0, b = 0, n = 0;
                for (int sy = sy0; sy < sy1; sy++)
                {
                    int row = sy * srcW * 4;
                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        int i = row + sx * 4;
                        r += src[i]; g += src[i + 1]; b += src[i + 2];
                        n++;
                    }
                }

                int o = (oy * outW + ox) * 4;
                dst[o] = (byte)(r / n);
                dst[o + 1] = (byte)(g / n);
                dst[o + 2] = (byte)(b / n);
                dst[o + 3] = 255; // the viewport's alpha is whatever blending left behind; the picture is opaque

                double luma = 0.2126 * dst[o] + 0.7152 * dst[o + 1] + 0.0722 * dst[o + 2];
                lumaSum += luma;
                lumaSqSum += luma * luma;
            }
        }

        int count = outW * outH;
        double mean = lumaSum / count;
        double variance = lumaSqSum / count - mean * mean;
        if (variance < 4.0) return null; // stddev under 2/255: nothing worth showing

        return new Image(outW, outH, dst); // rows stay bottom-up, as Image expects
    }
}

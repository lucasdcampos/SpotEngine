using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.OpenGL.Extensions.ImGui;

namespace Spot.Core.Services;

/// <summary>
/// Manages the ImGui context and rendering loop.
/// </summary>
public class ImGuiService : IEngineService
{
    private readonly ApplicationSpec _spec;
    private readonly List<ImFontPtr> _fonts = new();
    private ImGuiController? _controller;

    // Keeps the icon-font glyph-range array pinned for the atlas's lifetime: ImGui stores the pointer
    // and only reads it when the atlas is (re)built, so the array must outlive font configuration.
    private GCHandle _iconRangeHandle;

    // Same contract for any standalone additional fonts that carry their own tight glyph range (e.g. a
    // large icon atlas): each array stays pinned until Shutdown.
    private readonly List<GCHandle> _extraRangeHandles = new();

    public ImGuiService(ApplicationSpec spec)
    {
        _spec = spec;
    }

    /// <summary>
    /// The fonts baked into the atlas, in order: index 0 is the primary UI font, then each
    /// <see cref="ApplicationSpec.AdditionalFonts"/> entry. Empty until <see cref="Init"/> runs.
    /// </summary>
    public IReadOnlyList<ImFontPtr> Fonts => _fonts;

    public void Init(Application app)
    {
        // The primary font is still loaded via the controller's font config — the proven path that
        // makes it the default (Fonts[0]) font. The extra fonts are appended in the IO-configure hook,
        // which the controller runs right after the primary and before the atlas texture is built, so a
        // single atlas holds them all. Handles are captured (not atlas indices) so they stay valid
        // regardless of any default font the controller adds afterwards.
        bool havePrimary = !string.IsNullOrEmpty(_spec.FontPath) && File.Exists(_spec.FontPath);
        ImGuiFontConfig? fontConfig = null;
        if (havePrimary)
        {
            fontConfig = new ImGuiFontConfig(_spec.FontPath!, _spec.FontSize);
        }
        else if (!string.IsNullOrEmpty(_spec.FontPath))
        {
            Log.CoreWarn("UI font not found at '{0}', using the default font.", _spec.FontPath);
        }

        _controller = new ImGuiController(
            Spot.Rendering.Renderer.Api, app.Window.NativeWindow, app.Window.Input,
            fontConfig, () => ConfigureExtraFonts(havePrimary));

        ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        ImGui.StyleColorsDark();
    }

    // Runs inside the controller's construction, after the primary font has been added. Records the
    // primary handle at index 0, then bakes each additional font (substituting the primary for any
    // missing file so index alignment — relied on by callers of Fonts — is preserved).
    private void ConfigureExtraFonts(bool havePrimary)
    {
        var io = ImGui.GetIO();
        _fonts.Clear();

        ImFontPtr primary = havePrimary && io.Fonts.Fonts.Size > 0
            ? io.Fonts.Fonts[0]
            : io.Fonts.AddFontDefault();
        _fonts.Add(primary);

        MergeIconFont(io);

        foreach (FontSpec font in _spec.AdditionalFonts)
        {
            if (!string.IsNullOrEmpty(font.Path) && File.Exists(font.Path))
            {
                if (font.GlyphRanges != null)
                {
                    // A standalone face with a tight range (e.g. a large icon atlas). Pin the range for
                    // the atlas's lifetime and let ImGui build a default config around it.
                    var handle = GCHandle.Alloc(font.GlyphRanges, GCHandleType.Pinned);
                    _extraRangeHandles.Add(handle);
                    _fonts.Add(io.Fonts.AddFontFromFileTTF(font.Path, font.Size, default, handle.AddrOfPinnedObject()));
                }
                else
                {
                    _fonts.Add(io.Fonts.AddFontFromFileTTF(font.Path, font.Size));
                }
            }
            else
            {
                Log.CoreWarn("Additional font not found at '{0}', substituting the primary font.", font.Path);
                _fonts.Add(primary); // keep index alignment; callers get the body font in this slot
            }
        }
    }

    // Merges the icon font into the font that was just added (the body font), so icon glyphs render
    // inline with text. Called immediately after the primary so MergeMode targets it.
    private void MergeIconFont(ImGuiIOPtr io)
    {
        IconFontSpec? icon = _spec.IconFont;
        if (icon == null)
        {
            return;
        }
        if (!File.Exists(icon.Path))
        {
            Log.CoreWarn("Icon font not found at '{0}'; icons will be missing.", icon.Path);
            return;
        }

        // Pin the range for the atlas's lifetime (freed in Shutdown).
        _iconRangeHandle = GCHandle.Alloc(icon.GlyphRanges, GCHandleType.Pinned);

        unsafe
        {
            ImFontConfigPtr config = ImGuiNative.ImFontConfig_ImFontConfig();
            config.MergeMode = true;                 // fold these glyphs into the previous (body) font
            config.PixelSnapH = true;
            config.GlyphMinAdvanceX = icon.Size;     // keep icons a uniform width so rows stay aligned
            io.Fonts.AddFontFromFileTTF(icon.Path, icon.Size, config, _iconRangeHandle.AddrOfPinnedObject());
            config.Destroy();
        }
    }

    public void Update(float deltaTime)
    {
        _controller?.Update(deltaTime);
    }

    public void ImGuiRender()
    {
        // This is called inside the try-catch block by Application.
    }

    /// <summary>
    /// Called by Application.Run in a finally block to ensure the frame is balanced.
    /// </summary>
    public void RenderFrame()
    {
        _controller?.Render();
    }

    public void Shutdown()
    {
        _controller?.Dispose();
        _controller = null;

        if (_iconRangeHandle.IsAllocated)
        {
            _iconRangeHandle.Free();
        }

        foreach (var handle in _extraRangeHandles)
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
        _extraRangeHandles.Clear();
    }
}

using System;
using System.Collections.Generic;
using ImGuiNET;
using Spot.Assets;
using Spot.Core;
using Spot.Rendering;

namespace Spot.DebugUI.UI;

/// <summary>
/// A process-wide cache of rendered material previews keyed by material path, so the asset picker can
/// show a live sphere thumbnail for each <c>.sptmat</c> instead of a flat glyph. Each entry owns a small
/// offscreen framebuffer that is re-rendered only when the material's preview-relevant properties change.
///
/// Rendering a preview is a full offscreen draw, so a per-frame render budget streams new thumbnails in
/// over a few frames rather than stalling the frame the picker opens. Entries are capped and load
/// failures are remembered so a broken material logs once and falls back to its glyph. Never throws.
/// </summary>
internal static class MaterialThumbnails
{
    private const uint Size = 96;
    private const int MaxCached = 128;
    // How many previews may be (re)rendered per frame. Keeps opening a picker full of materials smooth.
    private const int RenderBudgetPerFrame = 3;

    private sealed class Entry
    {
        public required Framebuffer Framebuffer;
        public int Signature;
    }

    private static readonly Dictionary<string, Entry> _cache = new();
    private static readonly HashSet<string> _failed = new();
    private static int _lastFrame = -1;
    private static int _rendersThisFrame;

    /// <summary>
    /// Returns the GL texture handle of the rendered preview for <paramref name="materialPath"/>, or 0 when
    /// there is nothing to show yet (still queued behind the frame budget, load failed, or cache is full).
    /// A 0 result lets the caller fall back to a glyph and try again next frame.
    /// </summary>
    public static nint Get(string materialPath)
    {
        if (string.IsNullOrEmpty(materialPath) || _failed.Contains(materialPath))
            return 0;

        int frame = ImGui.GetFrameCount();
        if (frame != _lastFrame)
        {
            _lastFrame = frame;
            _rendersThisFrame = 0;
        }

        Material material;
        try { material = Material.Load(materialPath); }
        catch { _failed.Add(materialPath); return 0; }

        int sig = MaterialPreviewHelper.Signature(material);

        if (_cache.TryGetValue(materialPath, out Entry? entry))
        {
            // Re-render on edit, but only if we still have budget this frame; otherwise show the stale
            // (but valid) preview until a later frame catches up.
            if (entry.Signature != sig && _rendersThisFrame < RenderBudgetPerFrame &&
                TryRender(materialPath, material, entry.Framebuffer))
            {
                entry.Signature = sig;
            }
            return (nint)entry.Framebuffer.ColorAttachment;
        }

        if (_cache.Count >= MaxCached || _rendersThisFrame >= RenderBudgetPerFrame)
            return 0;

        Framebuffer fb;
        try { fb = new Framebuffer(Size, Size); }
        catch { _failed.Add(materialPath); return 0; }

        if (!TryRender(materialPath, material, fb))
        {
            fb.Dispose();
            _failed.Add(materialPath);
            return 0;
        }

        _cache[materialPath] = new Entry { Framebuffer = fb, Signature = sig };
        return (nint)fb.ColorAttachment;
    }

    // Renders one preview, counting it against the frame budget. A faulty material/shader must not throw
    // out of the picker: on failure we log once (via the failed set the caller maintains) and return false.
    private static bool TryRender(string materialPath, Material material, Framebuffer fb)
    {
        try
        {
            MaterialPreviewHelper.RenderToFramebuffer(material, fb);
            _rendersThisFrame++;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Failed to render material thumbnail for '{0}': {1}", materialPath, ex.Message);
            return false;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ImGuiNET;
using Spot.Editor.Utils;
using Spot.Engine.Graphics;

namespace Spot.Editor.Launcher;

/// <summary>
/// Loads the launcher's project thumbnails (<see cref="ProjectThumbnail"/>) without stalling the first frames:
/// PNGs decode on the thread pool and upload to the GPU on the render thread in <see cref="Update"/>. A project
/// with no thumbnail, or one that fails to decode, just reports none so its card shows the placeholder.
/// </summary>
internal sealed class ProjectThumbnailCache : IDisposable
{
    /// <summary>How long a freshly loaded picture takes to fade in over its placeholder, in seconds.</summary>
    private const float FadeSeconds = 0.18f;

    private sealed class Entry
    {
        public Texture2D? Texture;
        public double ReadyTime;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string Key, Image? Image)> _decoded = new();

    /// <summary>Uploads pictures decoded since the last call. Call once per frame on the render thread.</summary>
    public void Update()
    {
        while (_decoded.TryDequeue(out var item))
        {
            if (item.Image == null || !_entries.TryGetValue(item.Key, out Entry? entry)) continue;
            try
            {
                entry.Texture = item.Image.ToTexture();
                entry.ReadyTime = ImGui.GetTime();
            }
            catch
            {
                // A texture that cannot be created just leaves the placeholder in place.
            }
        }
    }

    /// <summary>
    /// Returns the project's thumbnail once it is loaded, starting the load on first request.
    /// <paramref name="opacity"/> ramps from 0 to 1 right after it arrives, for a short fade-in.
    /// </summary>
    public bool TryGet(string projectDirectory, out Texture2D texture, out float opacity)
    {
        if (!_entries.TryGetValue(projectDirectory, out Entry? entry))
        {
            _entries[projectDirectory] = new Entry();
            BeginDecode(projectDirectory);
        }
        else if (entry.Texture != null)
        {
            texture = entry.Texture;
            opacity = Math.Clamp((float)((ImGui.GetTime() - entry.ReadyTime) / FadeSeconds), 0.0f, 1.0f);
            return true;
        }

        texture = null!;
        opacity = 0.0f;
        return false;
    }

    private void BeginDecode(string projectDirectory)
    {
        string path = ProjectThumbnail.PathFor(projectDirectory);
        Task.Run(() =>
        {
            Image? image = null;
            try
            {
                if (File.Exists(path)) image = Image.FromBytes(File.ReadAllBytes(path));
            }
            catch
            {
                // Unreadable or corrupt: the card keeps its placeholder.
            }
            _decoded.Enqueue((projectDirectory, image));
        });
    }

    public void Dispose()
    {
        foreach (Entry entry in _entries.Values) entry.Texture?.Dispose();
        _entries.Clear();
    }
}

using System.IO;
using System.Text.Json;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Spot.Editor.Utils;

public class EditorWindowSettings
{
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int PosX { get; set; } = -1;
    public int PosY { get; set; } = -1;
    public bool Maximized { get; set; } = false;
    public bool Is3DMode { get; set; } = true;
}

public static class EditorSettings
{
    private static readonly string SettingsFile = "editor_window.json";
    
    public static bool GlobalIs3DMode { get; set; } = true;

    // A comfortable default editor size, larger than the compact launcher, used the first time the
    // editor is opened (before any window layout has been saved).
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 720;

    /// <summary>
    /// Applies the editor's window size/position/state (from the saved layout, or a sensible default
    /// when none exists yet). The launcher calls this <b>before</b> switching to the editor so the
    /// window is already at its final size while the loading screen is on-screen — the editor then
    /// re-applies the same values in <c>OnEnter</c>, which is a no-op resize.
    /// </summary>
    public static void PrepareEditorWindow(IWindow window)
    {
        if (File.Exists(SettingsFile))
        {
            LoadAndApply(window);
            return;
        }

        try
        {
            window.WindowState = WindowState.Normal;
            window.Size = new Vector2D<int>(DefaultWidth, DefaultHeight);
            window.Center();
        }
        catch { }
    }

    public static void LoadAndApply(IWindow window)
    {
        if (File.Exists(SettingsFile))
        {
            try
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<EditorWindowSettings>(json);
                if (settings != null)
                {
                    // When the target is maximized, only set the state: assigning an explicit size to
                    // an already-maximized window can bounce it out of and back into the maximized
                    // state, which would flash a mis-sized frame. This keeps re-applying idempotent.
                    if (settings.Maximized)
                    {
                        window.WindowState = WindowState.Maximized;
                    }
                    else
                    {
                        window.WindowState = WindowState.Normal;
                        if (settings.Width > 0 && settings.Height > 0)
                        {
                            window.Size = new Vector2D<int>(settings.Width, settings.Height);
                        }
                        if (settings.PosX >= 0 && settings.PosY >= 0)
                        {
                            window.Position = new Vector2D<int>(settings.PosX, settings.PosY);
                        }
                    }
                    GlobalIs3DMode = settings.Is3DMode;
                }
            }
            catch { }
        }
    }

    public static void Save(IWindow window)
    {
        try
        {
            var settings = new EditorWindowSettings
            {
                Width = window.Size.X,
                Height = window.Size.Y,
                PosX = window.Position.X,
                PosY = window.Position.Y,
                Maximized = window.WindowState == WindowState.Maximized,
                Is3DMode = GlobalIs3DMode
            };
            
            // If the window is minimized or hidden when closing, we don't want to save that as normal size/pos
            if (window.WindowState != WindowState.Normal && window.WindowState != WindowState.Maximized)
            {
                return;
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch { }
    }
}

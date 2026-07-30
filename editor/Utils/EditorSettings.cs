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
                    if (settings.Width > 0 && settings.Height > 0)
                    {
                        window.Size = new Vector2D<int>(settings.Width, settings.Height);
                    }
                    if (settings.PosX >= 0 && settings.PosY >= 0)
                    {
                        window.Position = new Vector2D<int>(settings.PosX, settings.PosY);
                    }
                    window.WindowState = settings.Maximized ? WindowState.Maximized : WindowState.Normal;
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

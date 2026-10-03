using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spot.Editor.Launcher;

internal enum ProjectView
{
    Grid,
    List,
}

internal enum ProjectSort
{
    LastOpened,
    Name,
}

/// <summary>
/// How the launcher lays out the project list (view and sort order), remembered per user next to the
/// recent-projects list. Best-effort like the rest of the launcher's persistence: a missing or corrupt file
/// just yields the defaults.
/// </summary>
internal sealed class LauncherPreferences
{
    public ProjectView View { get; set; } = ProjectView.Grid;
    public ProjectSort Sort { get; set; } = ProjectSort.LastOpened;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string StoragePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpotEngine",
            "launcher.json");

    public static LauncherPreferences Load()
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                return JsonSerializer.Deserialize<LauncherPreferences>(File.ReadAllText(StoragePath), JsonOptions)
                    ?? new LauncherPreferences();
            }
        }
        catch { /* ignore corrupt/unreadable preferences */ }
        return new LauncherPreferences();
    }

    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(StoragePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* best-effort persistence */ }
    }
}

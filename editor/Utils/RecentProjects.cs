using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Spot.Editor.Utils;

/// <summary>
/// Persists the list of recently opened projects (paths to .sptproj files) under the user's
/// application-data folder so the launcher can offer quick access across sessions.
/// </summary>
public static class RecentProjects
{
    private const int MaxEntries = 12;

    private static string StoragePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpotEngine",
            "recent_projects.json");

    /// <summary>Returns the recent project paths that still exist on disk, most recent first.</summary>
    public static List<string> Load()
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                var stored = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StoragePath));
                if (stored != null)
                {
                    return stored.Where(File.Exists).Distinct().ToList();
                }
            }
        }
        catch { /* ignore corrupt/unreadable list */ }
        return new List<string>();
    }

    /// <summary>Moves <paramref name="sptprojPath"/> to the front of the recent list.</summary>
    public static void Add(string sptprojPath)
    {
        if (string.IsNullOrWhiteSpace(sptprojPath)) return;

        string full = Path.GetFullPath(sptprojPath);
        var list = Load();
        list.RemoveAll(p => PathsEqual(p, full));
        list.Insert(0, full);
        if (list.Count > MaxEntries)
        {
            list = list.Take(MaxEntries).ToList();
        }
        Save(list);
    }

    /// <summary>Removes an entry from the recent list.</summary>
    public static void Remove(string sptprojPath)
    {
        var list = Load();
        list.RemoveAll(p => PathsEqual(p, sptprojPath));
        Save(list);
    }

    private static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    private static void Save(List<string> list)
    {
        try
        {
            string? dir = Path.GetDirectoryName(StoragePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort persistence */ }
    }
}

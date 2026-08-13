using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Spot.Editor.Utils;

/// <summary>
/// One entry in the recent-projects list: the path to a <c>.sptproj</c> file plus the metadata the
/// launcher shows next to it (when it was last opened and which engine version opened it).
/// </summary>
public sealed class RecentProject
{
    public string Path { get; set; } = string.Empty;

    /// <summary>When this project was last opened, in UTC. <c>null</c> for entries migrated from an
    /// older list format that did not record a timestamp.</summary>
    public DateTime? LastOpenedUtc { get; set; }

    /// <summary>The engine version that last opened this project (e.g. <c>"0.1.0"</c>), or <c>null</c>
    /// when unknown.</summary>
    public string? EngineVersion { get; set; }
}

/// <summary>
/// Persists the list of recently opened projects under the user's application-data folder so the
/// launcher can offer quick access across sessions. Each entry carries a little metadata (last-opened
/// time, engine version) that the launcher surfaces on its project cards.
/// </summary>
public static class RecentProjects
{
    private const int MaxEntries = 12;

    private static string StoragePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpotEngine",
            "recent_projects.json");

    /// <summary>Returns the recent projects that still exist on disk, most recent first.</summary>
    public static List<RecentProject> Load()
    {
        try
        {
            if (File.Exists(StoragePath))
            {
                string json = File.ReadAllText(StoragePath);
                return Deduplicate(Parse(json)).Where(p => File.Exists(p.Path)).ToList();
            }
        }
        catch { /* ignore corrupt/unreadable list */ }
        return new List<RecentProject>();
    }

    /// <summary>
    /// Records a project as just opened: moves it to the front of the list and stamps it with the
    /// current time and the engine version that opened it.
    /// </summary>
    public static void Add(string sptprojPath, string? engineVersion = null)
    {
        if (string.IsNullOrWhiteSpace(sptprojPath)) return;

        string full = Path.GetFullPath(sptprojPath);
        var list = Load();
        list.RemoveAll(p => PathsEqual(p.Path, full));
        list.Insert(0, new RecentProject
        {
            Path = full,
            LastOpenedUtc = DateTime.UtcNow,
            EngineVersion = engineVersion,
        });
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
        list.RemoveAll(p => PathsEqual(p.Path, sptprojPath));
        Save(list);
    }

    // Parses either the current object format or the legacy `["path", ...]` string array, so an
    // existing list keeps working after the upgrade rather than being silently dropped.
    private static List<RecentProject> Parse(string json)
    {
        try
        {
            var typed = JsonSerializer.Deserialize<List<RecentProject>>(json);
            if (typed != null && typed.All(p => !string.IsNullOrEmpty(p.Path)))
            {
                return typed;
            }
        }
        catch { /* fall through to the legacy string-array format */ }

        try
        {
            var legacy = JsonSerializer.Deserialize<List<string>>(json);
            if (legacy != null)
            {
                return legacy.Select(p => new RecentProject { Path = p }).ToList();
            }
        }
        catch { /* not the legacy format either */ }

        return new List<RecentProject>();
    }

    private static List<RecentProject> Deduplicate(List<RecentProject> list)
    {
        var seen = new List<RecentProject>();
        foreach (var entry in list)
        {
            if (string.IsNullOrWhiteSpace(entry.Path)) continue;
            if (!seen.Any(p => PathsEqual(p.Path, entry.Path))) seen.Add(entry);
        }
        return seen;
    }

    private static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    private static void Save(List<RecentProject> list)
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

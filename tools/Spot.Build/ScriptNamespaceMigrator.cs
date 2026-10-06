using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Spot.Build;

/// <summary>
/// Rewrites game scripts written against the pre-0.4 namespaces (<c>Spot.Core</c>, <c>Spot.Scenes</c>,
/// <c>Spot.Rendering</c>, ...) to the level namespaces (<c>Spot.Engine.*</c>, <c>Spot.Engine.*</c>). Each old
/// <c>using</c> is replaced by just the new namespaces the script actually uses, and fully qualified names are
/// re-pointed at the namespace their type lives in now. Scripts already on the new namespaces are left untouched,
/// so running it twice is harmless.
/// </summary>
public static class ScriptNamespaceMigrator
{
    /// <summary>
    /// Each pre-0.4 namespace and the namespaces its types moved to.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> MovedNamespaces = new Dictionary<string, string[]>
    {
        ["Spot"] = new[] { "Spot.Engine" },
        ["Spot.Core"] = new[] { "Spot.Engine.Mathematics", "Spot.Engine" },
        ["Spot.Core.Services"] = new[] { "Spot.Engine.Services" },
        ["Spot.Events"] = new[] { "Spot.Engine.Events" },
        ["Spot.Rendering"] = new[] { "Spot.Engine.Graphics", "Spot.Engine.Mathematics", "Spot.Engine.Rendering" },
        ["Spot.Audio"] = new[] { "Spot.Engine.Audio" },
        ["Spot.Assets"] = new[] { "Spot.Engine.Graphics", "Spot.Engine.Audio", "Spot.Engine.Assimp", "Spot.Engine.Assets" },
        ["Spot.Animation"] = new[] { "Spot.Engine.Animation", "Spot.Engine.Animation" },
        ["Spot.Physics"] = new[] { "Spot.Engine.Mathematics", "Spot.Engine.Physics" },
        ["Spot.Physics.Bepu"] = new[] { "Spot.Engine.Physics.Bepu" },
        ["Spot.Physics.Aether"] = new[] { "Spot.Engine.Physics.Aether" },
        ["Spot.Scenes"] = new[] { "Spot.Engine.Scenes", "Spot.Engine" },
        ["Spot.UI"] = new[] { "Spot.Engine.UI" },
        ["Spot.UI.Serialization"] = new[] { "Spot.Engine.UI" },
        ["Spot.Console"] = new[] { "Spot.Engine.Console" },
        ["Spot.Browser"] = new[] { "Spot.Engine.Browser", "Spot.Engine.IO", "Spot.Engine.Browser" },
        ["Spot.IO"] = new[] { "Spot.Engine.IO" },
    };

    // Extension members that add to a type from another namespace: a script calling them needs the using even
    // though it names no type from that namespace (Texture2D.Load lives in Spot.Engine.Assets).
    private static readonly Dictionary<string, Regex> CrossNamespaceExtensions = new()
    {
        ["Spot.Engine.Assets"] = new Regex(@"\.Load\(|\bFromSpTex\b|\bFromSpAudio\b", RegexOptions.Compiled),
        ["Spot.Engine.Physics"] = new Regex(@"\bFromTransform\b", RegexOptions.Compiled),
    };

    private static readonly Regex UsingDirective =
        new(@"^([ \t]*)using (Spot(?:\.[\w.]+)?);[ \t]*\r?\n", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex QualifiedName = new(
        @"(?<![\w])(global::)?(" + string.Join("|", MovedNamespaces.Keys
            .Where(k => k != "Spot").OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")\.(\w+)",
        RegexOptions.Compiled);

    /// <summary>
    /// The public types of the loaded Spot framework and engine assemblies, by namespace.
    /// </summary>
    /// <returns>A map from namespace to the simple names of its public types.</returns>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> LoadedSpotTypes()
    {
        // Touch one type per level so the assemblies are loaded even if nothing else used them yet.
        Assembly[] assemblies =
        {
            typeof(Spot.Engine.Window).Assembly,
            typeof(Spot.Engine.IO.FileSystem).Assembly,
            typeof(Spot.Engine.Scenes.Scene).Assembly,
        };

        var byNamespace = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        IEnumerable<Assembly> all = assemblies.Concat(AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name is "Spot.Engine.Assimp"));
        foreach (Assembly assembly in all.Distinct())
        {
            foreach (Type type in assembly.GetExportedTypes())
            {
                if (type.Namespace is null || type.IsNested)
                {
                    continue;
                }

                string name = type.Name;
                int tick = name.IndexOf('`');
                if (tick >= 0)
                {
                    name = name[..tick];
                }

                if (!byNamespace.TryGetValue(type.Namespace, out HashSet<string>? names))
                {
                    names = new HashSet<string>(StringComparer.Ordinal);
                    byNamespace[type.Namespace] = names;
                }

                names.Add(name);
            }
        }

        // The Assimp module is only referenced on desktop; keep its namespace known even if it isn't loaded.
        byNamespace.TryAdd("Spot.Engine.Assimp", new HashSet<string> { "AssimpModelImporter", "ImportedMaterial" });

        return byNamespace.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value);
    }

    /// <summary>
    /// Rewrites one script's source to the level namespaces.
    /// </summary>
    /// <param name="source">The script source.</param>
    /// <param name="typesByNamespace">The public types of each new namespace (see <see cref="LoadedSpotTypes"/>).</param>
    /// <returns>The migrated source (the same string when nothing needed changing).</returns>
    public static string Migrate(string source, IReadOnlyDictionary<string, IReadOnlySet<string>> typesByNamespace)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(typesByNamespace);

        // 1. Fully qualified names point at the namespace that holds the type now.
        string result = QualifiedName.Replace(source, m =>
        {
            string oldNamespace = m.Groups[2].Value;
            string typeName = m.Groups[3].Value;
            string? target = MovedNamespaces[oldNamespace]
                .FirstOrDefault(ns => typesByNamespace.TryGetValue(ns, out IReadOnlySet<string>? types) && types.Contains(typeName));
            return target is null ? m.Value : $"{m.Groups[1].Value}{target}.{typeName}";
        });

        // 2. Old usings become the new namespaces the script actually uses.
        List<Match> oldUsings = UsingDirective.Matches(result).Where(m => MovedNamespaces.ContainsKey(m.Groups[2].Value)).ToList();
        if (oldUsings.Count == 0)
        {
            return result;
        }

        string code = CodeForReferenceChecks(result);
        HashSet<string> existing = UsingDirective.Matches(result).Select(m => m.Groups[2].Value).ToHashSet(StringComparer.Ordinal);
        string[] keep = oldUsings
            .SelectMany(m => MovedNamespaces[m.Groups[2].Value])
            .Distinct(StringComparer.Ordinal)
            .Where(ns => !existing.Contains(ns) && Uses(code, ns, typesByNamespace))
            .OrderBy(ns => ns, StringComparer.Ordinal)
            .ToArray();

        string indent = oldUsings[0].Groups[1].Value;
        string newline = oldUsings[0].Value.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var sb = new StringBuilder(result.Length);
        int last = 0;
        for (int i = 0; i < oldUsings.Count; i++)
        {
            Match m = oldUsings[i];
            sb.Append(result, last, m.Index - last);
            if (i == 0)
            {
                foreach (string ns in keep)
                {
                    sb.Append(indent).Append("using ").Append(ns).Append(';').Append(newline);
                }
            }

            last = m.Index + m.Length;
        }

        sb.Append(result, last, result.Length - last);
        return sb.ToString();
    }

    /// <summary>
    /// Migrates every <c>.cs</c> file under a directory (typically a project's <c>Assets/</c>).
    /// </summary>
    /// <param name="directory">The directory to scan recursively.</param>
    /// <param name="dryRun">Report what would change without writing.</param>
    /// <returns>The number of files that were (or would be) changed.</returns>
    public static int MigrateDirectory(string directory, bool dryRun = false) =>
        Directory.Exists(directory)
            ? MigrateFiles(Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories), dryRun)
            : 0;

    /// <summary>
    /// Migrates a project's scripts: everything under its assets directory plus the <c>.cs</c> files at its root
    /// (the generated <c>Program.cs</c>), leaving build output alone.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="assetDirectory">The project's assets directory.</param>
    /// <param name="dryRun">Report what would change without writing.</param>
    /// <returns>The number of files that were (or would be) changed.</returns>
    public static int MigrateProject(string projectDirectory, string assetDirectory, bool dryRun = false)
    {
        IEnumerable<string> root = Directory.Exists(projectDirectory)
            ? Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            : Enumerable.Empty<string>();
        IEnumerable<string> assets = Directory.Exists(assetDirectory)
            ? Directory.EnumerateFiles(assetDirectory, "*.cs", SearchOption.AllDirectories)
            : Enumerable.Empty<string>();
        return MigrateFiles(root.Concat(assets).Distinct(StringComparer.OrdinalIgnoreCase), dryRun);
    }

    private static int MigrateFiles(IEnumerable<string> files, bool dryRun)
    {
        IReadOnlyDictionary<string, IReadOnlySet<string>> types = LoadedSpotTypes();
        int changed = 0;
        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            string migrated = Migrate(source, types);
            if (migrated == source)
            {
                continue;
            }

            changed++;
            if (!dryRun)
            {
                File.WriteAllText(file, migrated);
            }
        }

        return changed;
    }

    // Whether the code names a type from the namespace, or calls one of its cross-namespace extension members.
    private static bool Uses(string code, string ns, IReadOnlyDictionary<string, IReadOnlySet<string>> typesByNamespace)
    {
        if (CrossNamespaceExtensions.TryGetValue(ns, out Regex? extension) && extension.IsMatch(code))
        {
            return true;
        }

        if (!typesByNamespace.TryGetValue(ns, out IReadOnlySet<string>? types))
        {
            return false;
        }

        foreach (string type in types)
        {
            if (Regex.IsMatch(code, @"(?<![\w.])" + Regex.Escape(type) + @"\b"))
            {
                return true;
            }

            if (type.EndsWith("Attribute", StringComparison.Ordinal) && type.Length > 9
                && Regex.IsMatch(code, @"\[\s*" + Regex.Escape(type[..^9]) + @"\b"))
            {
                return true;
            }
        }

        return false;
    }

    // The source without comments and using directives, so a type named only in a comment or an old using
    // doesn't count as a reference.
    private static string CodeForReferenceChecks(string source)
    {
        string code = Regex.Replace(source, @"//[^\n]*", string.Empty);
        code = Regex.Replace(code, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(code, @"^\s*using (?:static )?[\w.]+(?:\s*=\s*[\w.<>]+)?;[^\n]*\n", string.Empty, RegexOptions.Multiline);
    }
}


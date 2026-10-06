using System;
using System.IO;
using System.Linq;
using System.Text;
using Spot.Engine.Assets;
using Spot.Engine;

namespace Spot.DebugUI.UI;

/// <summary>
/// Creates the C# file behind a new user component: <c>Assets/Scripts/&lt;Name&gt;.cs</c> holding a class that
/// derives from <c>Component</c>, plus its <c>.meta</c> sidecar so scenes reference it by a stable guid from
/// the start. Shared by the inspector's "New Component" flow and the asset browser's "New Script".
/// </summary>
public static class ComponentScripts
{
    /// <summary>
    /// Returns whether <paramref name="name"/> can be a component's class name: a C# identifier that starts
    /// with a letter (so it is also a sensible file name).
    /// </summary>
    public static bool IsValidClassName(string name) =>
        !string.IsNullOrEmpty(name)
        && char.IsLetter(name[0])
        && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// Turns free text such as <c>"player movement"</c> into a class name (<c>"PlayerMovement"</c>): words are
    /// capitalized and joined, and characters a C# identifier cannot hold are dropped.
    /// </summary>
    public static string ToClassName(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool upperNext = true;
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
                upperNext = false;
            }
            else
            {
                upperNext = true;
            }
        }

        // An identifier cannot start with a digit or underscore.
        while (sb.Length > 0 && !char.IsLetter(sb[0]))
            sb.Remove(0, 1);
        return sb.ToString();
    }

    /// <summary>
    /// Forgets everything the debug UI cached about the game's component types. Call before unloading the
    /// game's scripts, so the old types are neither drawn from stale metadata nor kept alive by it.
    /// </summary>
    public static void ForgetLoadedTypes()
    {
        ComponentInspector.ClearTypeCaches();
        LoadedTypesForgotten?.Invoke();
    }

    /// <summary>Raised by <see cref="ForgetLoadedTypes"/>, so panels holding the game's types or components can drop them.</summary>
    internal static event Action? LoadedTypesForgotten;

    /// <summary>The source of a new component class named <paramref name="className"/>.</summary>
    public static string Template(string className, string rootNamespace) => $$"""
        using Spot.Engine.Scenes;

        namespace {{rootNamespace}};

        public class {{className}} : Component
        {
            public override void OnStart()
            {
            }

            public override void OnUpdate(float deltaTime)
            {
            }
        }

        """;

    /// <summary>
    /// Supplies the open project's name, used as the namespace of new scripts. Set by the host (the editor);
    /// DebugUI knows nothing about projects. Null or an empty name falls back to <c>Game</c>.
    /// </summary>
    public static Func<string?>? ProjectNameSource { get; set; }

    /// <summary>
    /// The namespace new scripts are written in: the active project's name as an identifier, or <c>Game</c>.
    /// </summary>
    public static string ProjectNamespace()
    {
        string? projectName = null;
        try
        {
            projectName = ProjectNameSource?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to read the project name: {0}", ex.Message);
        }

        string name = ToClassName(projectName ?? string.Empty);
        return name.Length > 0 ? name : "Game";
    }

    /// <summary>
    /// Writes a new component script into <paramref name="directory"/> (by default the project's
    /// <c>Assets/Scripts</c> folder, or <c>Assets</c> when that does not exist) and its <c>.meta</c> sidecar.
    /// Never throws: a failure is logged and reported through the return value.
    /// </summary>
    /// <param name="className">The class name, which is also the file name.</param>
    /// <param name="path">The written file's path.</param>
    /// <param name="guid">The script's stable guid, from its new sidecar (empty if it could not be written).</param>
    /// <param name="directory">The folder to write into, or <see langword="null"/> for the default.</param>
    /// <returns><see langword="true"/> when the file was created.</returns>
    public static bool Create(string className, out string path, out string guid, string? directory = null)
    {
        path = string.Empty;
        guid = string.Empty;

        if (!IsValidClassName(className))
        {
            Log.Error("'{0}' is not a valid component name: use letters, digits and underscores, starting with a letter.", className);
            return false;
        }

        if (directory is null)
        {
            string assets = AssetPath.Root;
            if (string.IsNullOrEmpty(assets))
            {
                Log.Error("Open a project before creating a component.");
                return false;
            }

            string scripts = Path.Combine(assets, "Scripts");
            directory = Directory.Exists(scripts) ? scripts : assets;
        }

        path = Path.Combine(directory, className + ".cs");
        if (File.Exists(path))
        {
            Log.Error("Cannot create component '{0}': {1} already exists.", className, path);
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, Template(className, ProjectNamespace()));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to create component '{0}': {1}", className, ex.Message);
            return false;
        }

        try
        {
            AssetMeta meta = AssetMeta.ReadOrCreate(path, "script");
            meta.Save(path);
            guid = meta.Guid;
        }
        catch (Exception ex)
        {
            // The script still works without a sidecar (it resolves by class name); just say so.
            Log.Warn("Created '{0}' but could not write its .meta sidecar: {1}", path, ex.Message);
        }

        return true;
    }
}

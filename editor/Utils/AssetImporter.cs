using System;
using System.IO;
using Spot.Core;
using Spot.Build;
namespace Spot.Editor.Utils;

/// <summary>
/// Helpers for bringing external asset files into the active project's asset directory.
/// </summary>
public static class AssetImporter
{
    /// <summary>
    /// Copies a model file into the active project's <c>Assets/Models</c> folder and returns the
    /// destination path. If no project is active, or the file is already inside that folder, the
    /// original path is returned unchanged.
    /// </summary>
    /// <param name="sourcePath">The path to the model file to import.</param>
    /// <returns>The path the model should be referenced by.</returns>
    public static string ImportModel(string sourcePath)
    {
        var project = Project.Active;
        if (project == null)
        {
            return sourcePath;
        }

        string modelsDir = Path.Combine(project.GetAssetDirectory(), "Models");
        Directory.CreateDirectory(modelsDir);

        string destPath = Path.Combine(modelsDir, Path.GetFileName(sourcePath));
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
        {
            return destPath;
        }

        File.Copy(sourcePath, destPath, overwrite: true);
        return destPath;
    }
}

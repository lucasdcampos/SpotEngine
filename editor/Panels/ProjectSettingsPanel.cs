using ImGuiNET;
using Spot.Core;
using Spot.Engine.Debug.UI;
using Spot.Editor.UI;

namespace Spot.Editor.Panels;

public class ProjectSettingsPanel
{
    public ProjectSettingsPanel()
    {
    }

    public void OnImGuiRender(ref bool isOpen)
    {
        if (!isOpen) return;

        if (ImGui.Begin("Project Settings", ref isOpen))
        {
            if (Project.Active != null)
            {
                var config = Project.Active.Config;
                bool changed = false;

                string name = config.Name;
                if (EditorGui.InputText("Project Name", ref name))
                {
                    config.Name = name;
                    changed = true;
                }

                // Start-scene slot: drag a .sptscene in or click to pick from a searchable list. The picker
                // and drag both yield an absolute path, which we store relative to the project's asset dir so
                // the .sptproj stays portable.
                string[] scenePatterns = { "*.sptscene" };
                if (EditorGui.AssetSlot("Start Scene", "SCENE_FILE", scenePatterns, config.StartScene, out string? scenePath))
                {
                    config.StartScene = RelativeToAssets(scenePath);
                    changed = true;
                }
                
                string assetDir = config.AssetDirectory;
                if (EditorGui.InputText("Asset Directory", ref assetDir))
                {
                    config.AssetDirectory = assetDir;
                    changed = true;
                }

                if (changed)
                {
                    // Save changes immediately
                    string sptprojPath = System.IO.Path.Combine(Project.Active.ProjectDirectory, $"{config.Name}.sptproj");
                    Project.SaveActive(sptprojPath);
                }
            }
            else
            {
                ImGui.Text("No active project.");
            }
        }
        ImGui.End();
    }

    // Normalizes an absolute asset path to a forward-slashed path relative to the active project's asset
    // directory, so the start scene reference committed to the .sptproj resolves on any machine.
    private static string RelativeToAssets(string? absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return string.Empty;
        string? assetDir = Project.Active?.GetAssetDirectory();
        if (!string.IsNullOrEmpty(assetDir) && absolutePath.StartsWith(assetDir, System.StringComparison.OrdinalIgnoreCase))
        {
            absolutePath = absolutePath.Substring(assetDir.Length).TrimStart('\\', '/');
        }
        return absolutePath.Replace("\\", "/");
    }
}

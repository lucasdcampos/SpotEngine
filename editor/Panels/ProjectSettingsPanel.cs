using ImGuiNET;
using Spot.Core;
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

                ImGui.PushID("StartScene");
                ImGui.Columns(2);
                ImGui.SetColumnWidth(0, 100.0f);
                ImGui.Text("Start Scene");
                ImGui.NextColumn();
                
                string startScene = config.StartScene;
                ImGui.Button(string.IsNullOrEmpty(startScene) ? "Drop Scene Here" : startScene, new System.Numerics.Vector2(-1, 0));
                
                if (ImGui.BeginDragDropTarget())
                {
                    unsafe
                    {
                        var payload = ImGui.AcceptDragDropPayload("SCENE_FILE");
                        if (payload.NativePtr != null)
                        {
                            string? filepath = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(payload.Data);
                            if (filepath != null)
                            {
                                string projAssetDir = Project.Active.GetAssetDirectory();
                                if (filepath.StartsWith(projAssetDir))
                                {
                                    filepath = filepath.Substring(projAssetDir.Length).TrimStart('\\', '/');
                                }
                                config.StartScene = filepath.Replace("\\", "/");
                                changed = true;
                            }
                        }
                    }
                    ImGui.EndDragDropTarget();
                }
                ImGui.Columns(1);
                ImGui.PopID();
                
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
}

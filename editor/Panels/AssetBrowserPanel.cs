using System;
using System.IO;
using System.Numerics;
using ImGuiNET;

namespace Spot.Editor.Panels;

public class AssetBrowserPanel
{
    private readonly EditorContext _context;
    private string _currentDirectory;
    private string _baseDirectory;

    public AssetBrowserPanel(EditorContext context)
    {
        _context = context;
        _baseDirectory = Spot.Core.Project.Active?.GetAssetDirectory() ?? Environment.CurrentDirectory;
        _currentDirectory = _baseDirectory;
    }

    public void OnImGuiRender(bool asWindow = false)
    {
        // Update base directory if project changes
        var currentProjectAssetDir = Spot.Core.Project.Active?.GetAssetDirectory() ?? Environment.CurrentDirectory;
        if (_baseDirectory != currentProjectAssetDir)
        {
            _baseDirectory = currentProjectAssetDir;
            _currentDirectory = _baseDirectory;
        }

        if (asWindow)
        {
            ImGui.Begin("Asset Browser");
        }

        if (_currentDirectory != _baseDirectory)
        {
            if (ImGui.Button("<- Back"))
            {
                var parentInfo = Directory.GetParent(_currentDirectory);
                if (parentInfo != null)
                {
                    _currentDirectory = parentInfo.FullName;
                }
            }
            ImGui.SameLine();
        }
        
        ImGui.Text(_currentDirectory);
        ImGui.Separator();

        ImGui.BeginChild("AssetList");

        float cellSize = 90.0f;
        float panelWidth = ImGui.GetContentRegionAvail().X;
        int columnCount = Math.Max(1, (int)(panelWidth / cellSize));
        
        ImGui.Columns(columnCount, "AssetBrowserColumns", false);

        try
        {
            var dirInfo = new DirectoryInfo(_currentDirectory);
            if (dirInfo.Exists)
            {
                foreach (var directory in dirInfo.GetDirectories())
                {
                    if (ImGui.Button(directory.Name + "\n(Dir)", new Vector2(cellSize - 10, cellSize - 10)))
                    {
                        _currentDirectory = directory.FullName;
                    }
                    ImGui.NextColumn();
                }

                foreach (var file in dirInfo.GetFiles())
                {
                    ImGui.Button(file.Name, new Vector2(cellSize - 10, cellSize - 10));
                    
                    if (ImGui.BeginDragDropSource())
                    {
                        ImGui.Text(file.Name);
                        // Aqui pode-se implementar o payload (ex: caminho do arquivo) no futuro
                        ImGui.EndDragDropSource();
                    }
                    ImGui.NextColumn();
                }
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error reading directory: {ex.Message}");
        }

        ImGui.Columns(1);
        ImGui.EndChild();

        if (asWindow)
        {
            ImGui.End();
        }
    }
}

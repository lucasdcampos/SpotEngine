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

    private bool _isCreatingScript = false;
    private string _newScriptName = "";

    public AssetBrowserPanel(EditorContext context)
    {
        _context = context;
        _baseDirectory = Spot.Core.Project.Active?.GetAssetDirectory() ?? Environment.CurrentDirectory;
        if (!Directory.Exists(_baseDirectory))
        {
            try { Directory.CreateDirectory(_baseDirectory); } catch { }
        }
        _currentDirectory = _baseDirectory;
    }

    public void OnImGuiRender(bool asWindow = false)
    {
        // Update base directory if project changes
        var currentProjectAssetDir = Spot.Core.Project.Active?.GetAssetDirectory() ?? Environment.CurrentDirectory;
        if (_baseDirectory != currentProjectAssetDir)
        {
            _baseDirectory = currentProjectAssetDir;
            if (!Directory.Exists(_baseDirectory))
            {
                try { Directory.CreateDirectory(_baseDirectory); } catch { }
            }
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
                    
                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = file.FullName,
                            UseShellExecute = true
                        });
                    }
                    
                    if (ImGui.BeginDragDropSource())
                    {
                        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(file.Name + "\0");
                        unsafe
                        {
                            fixed (byte* p = payloadBytes)
                            {
                                ImGui.SetDragDropPayload("SCRIPT_FILE", (IntPtr)p, (uint)payloadBytes.Length);
                            }
                        }
                        ImGui.Text(file.Name);
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

        if (ImGui.BeginPopupContextWindow("AssetBrowserContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            if (ImGui.MenuItem("New Script"))
            {
                _isCreatingScript = true;
                _newScriptName = "NewScript.cs";
            }
            ImGui.EndPopup();
        }

        if (_isCreatingScript)
        {
            ImGui.OpenPopup("Create New Script");
        }

        bool modalOpen = true;
        if (ImGui.BeginPopupModal("Create New Script", ref modalOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.InputText("Name", ref _newScriptName, 256);
            if (ImGui.Button("Create", new Vector2(120, 0)))
            {
                CreateScript(_newScriptName);
                _isCreatingScript = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                _isCreatingScript = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        else if (!modalOpen)
        {
            _isCreatingScript = false;
        }

        ImGui.Columns(1);
        ImGui.EndChild();

        if (asWindow)
        {
            ImGui.End();
        }
    }

    private void CreateScript(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!name.EndsWith(".cs")) name += ".cs";

        if (!Directory.Exists(_currentDirectory))
        {
            try { Directory.CreateDirectory(_currentDirectory); } catch { }
        }

        string filepath = Path.Combine(_currentDirectory, name);
        if (!File.Exists(filepath))
        {
            string className = Path.GetFileNameWithoutExtension(name);
            // Remova espaços ou caracteres inválidos simples
            className = className.Replace(" ", "");
            
            string template = $@"using System;
using Spot.Core;
using Spot.Scenes;

namespace Spot.Game;

public class {className} : EntityBehaviour
{{
    public override void OnCreate()
    {{
        
    }}

    public override void OnUpdate(float deltaTime)
    {{
        
    }}
}}
";
            File.WriteAllText(filepath, template);
        }
    }
}

using ImGuiNET;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Game.Scenes;

/// <summary>
/// The default scene: a menu to switch to the other demo scenes.
/// </summary>
internal sealed class MenuScene : Scene
{
    public override void OnEnter() => Renderer.SetClearColor(0.08f, 0.08f, 0.12f, 1.0f);

    public override void OnImGuiRender()
    {
        ImGui.Begin("Spot Engine - Menu");
        ImGui.TextUnformatted("Choose a scene:");
        ImGui.Separator();

        if (ImGui.Button("Hello Triangle (low-level API)"))
        {
            SceneManager.Load(new HelloTriangleScene());
        }

        if (ImGui.Button("Sprites (Sprite2D + Renderer2D)"))
        {
            SceneManager.Load(new SpriteScene());
        }

        if (ImGui.Button("Pong (entities + input)"))
        {
            SceneManager.Load(new PongScene());
        }

        ImGui.Separator();
        
        if (ImGui.Button("Load User Scene (scene.spotscene)"))
        {
            SceneManager.Load(new LoadedScene("scene.spotscene"));
        }

        ImGui.Separator();
        if (ImGui.Button("Quit"))
        {
            Application.Instance.Quit();
        }

        ImGui.End();
    }
}

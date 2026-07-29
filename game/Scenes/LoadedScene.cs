using ImGuiNET;
using System.Numerics;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Game.Scenes;

public class LoadedScene : Scene
{
    private readonly OrthographicCamera _camera = new(-1.0f, 1.0f, -1.0f, 1.0f);

    public LoadedScene(string filepath)
    {
        var serializer = new SceneSerializer(this);
        serializer.Deserialize(filepath);

        // Setup a 16:9 camera view
        float aspectRatio = 1280.0f / 720.0f;
        _camera = new OrthographicCamera(-aspectRatio, aspectRatio, -1.0f, 1.0f);
        _camera.Position = new Vector3(0, 0, 1.0f);
    }

    public override void OnEnter()
    {
        Renderer.SetClearColor(0.1f, 0.1f, 0.1f, 1.0f);
    }

    public override void OnRender()
    {
        Renderer.Clear();
        RenderSystem.Render(this, _camera);
    }

    public override void OnImGuiRender()
    {
        ImGui.Begin("Game overlay");
        if (ImGui.Button("Back to Menu"))
        {
            SceneManager.Load(new MenuScene());
        }
        ImGui.End();
    }
}

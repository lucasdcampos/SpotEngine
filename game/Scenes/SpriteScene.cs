using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Game.Scenes;

/// <summary>
/// A scene that tests the high-level 2D path: entities carrying <see cref="Sprite2D"/> components
/// are drawn automatically by <see cref="RenderSystem"/> (which batches through Renderer2D).
/// </summary>
internal sealed class SpriteScene : Scene
{
    private OrthographicCamera? _camera;
    private Texture2D? _texture;

    public override void OnEnter()
    {
        Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);

        _texture = new Texture2D(Path.Combine(AppContext.BaseDirectory, "assets", "spot.png"));
        _camera = new OrthographicCamera(-Aspect, Aspect, -1.0f, 1.0f);

        // A row of solid-color sprites...
        const int count = 5;
        for (int i = 0; i < count; i++)
        {
            Entity entity = CreateEntity($"Quad {i}");

            Transform transform = entity.GetComponent<Transform>();
            transform.Position = new Vector3(-1.1f + (i * 0.34f), -0.5f, 0.0f);
            transform.Scale = new Vector3(0.25f, 0.25f, 1.0f);

            entity.AddComponent(new Sprite2D
            {
                Color = new Vector4(i / (float)count, 0.5f, 1.0f - (i / (float)count), 1.0f),
            });
        }

        // ...and one textured sprite in the middle.
        Entity logo = CreateEntity("Logo");
        Transform logoTransform = logo.GetComponent<Transform>();
        logoTransform.Position = new Vector3(0.0f, 0.35f, 0.0f);
        logoTransform.Scale = new Vector3(0.9f, 0.9f, 1.0f);
        logo.AddComponent(new Sprite2D { Texture = _texture });
    }

    public override void OnUpdate(float deltaTime)
    {
        _camera?.SetProjection(-Aspect, Aspect, -1.0f, 1.0f);

        // Spin every entity in place by mutating its Transform component.
        foreach (Entity entity in View<Transform>())
        {
            entity.GetComponent<Transform>().Rotation += new Vector3(0.0f, 0.0f, 45.0f * deltaTime);
        }
    }

    public override void OnRender()
    {
        if (_camera is not null)
        {
            RenderSystem.Render(this, _camera);
        }
    }

    public override void OnImGuiRender()
    {
        ImGui.Begin("Sprites");
        ImGui.TextUnformatted("High-level: entities with Sprite2D, drawn by RenderSystem.");
        ImGui.Separator();
        if (ImGui.Button("Back to menu"))
        {
            SceneManager.Load(new MenuScene());
        }

        ImGui.End();
    }

    public override void OnExit() => _texture?.Dispose();

    private static float Aspect => (float)Application.Instance.Window.Width / Application.Instance.Window.Height;
}

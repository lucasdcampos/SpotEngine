using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Events;
using Spot.Game.Scripts;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Game.Scenes;

/// <summary>
/// A scene that tests the high-level 2D path: entities carrying <see cref="Sprite2D"/> components
/// are drawn automatically by <see cref="RenderSystem"/> (which batches through Renderer2D).
/// </summary>
internal sealed class SpriteScene : Scene
{
    private readonly Random _random = new();
    private OrthographicCamera? _camera;
    private Texture2D? _texture;
    private float _zoom = 1.0f;

    public override void OnEnter()
    {
        Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);

        _texture = new Texture2D(Path.Combine(AppContext.BaseDirectory, "assets", "spot.png"));
        _camera = new OrthographicCamera(-Aspect, Aspect, -1.0f, 1.0f);

        // A row of solid-color sprites...
        const int count = 5;
        for (int i = 0; i < count; i++)
        {
            Entity entity = Instantiate($"Quad {i}");

            Transform transform = entity.GetComponent<Transform>();
            transform.Position = new Vector3(-1.1f + (i * 0.34f), -0.5f, 0.0f);
            transform.Scale = new Vector3(0.25f, 0.25f, 1.0f);

            entity.AddComponent(new Sprite2D
            {
                Color = new Vector4(i / (float)count, 0.5f, 1.0f - (i / (float)count), 1.0f),
            });
        }

        // ...and one textured sprite in the middle.
        Entity logo = Instantiate("Logo");
        Transform logoTransform = logo.GetComponent<Transform>();
        logoTransform.Position = new Vector3(0.0f, 0.35f, 0.0f);
        logoTransform.Scale = new Vector3(0.9f, 0.9f, 1.0f);
        logo.AddComponent(new Sprite2D { Texture = _texture });
    }

    public override void OnUpdate(float deltaTime)
    {
        if (_camera is not null)
        {
            // Polling input: WASD / arrow keys pan the camera, mouse wheel zooms.
            const float panSpeed = 1.5f;
            Vector3 position = _camera.Position;
            if (Input.GetKey(Key.A) || Input.GetKey(Key.Left))
            {
                position.X -= panSpeed * deltaTime;
            }

            if (Input.GetKey(Key.D) || Input.GetKey(Key.Right))
            {
                position.X += panSpeed * deltaTime;
            }

            if (Input.GetKey(Key.S) || Input.GetKey(Key.Down))
            {
                position.Y -= panSpeed * deltaTime;
            }

            if (Input.GetKey(Key.W) || Input.GetKey(Key.Up))
            {
                position.Y += panSpeed * deltaTime;
            }

            _camera.Position = position;

            _zoom = Math.Clamp(_zoom - (Input.MouseScrollDelta.Y * 0.1f), 0.25f, 3.0f);
            _camera.SetProjection(-Aspect * _zoom, Aspect * _zoom, -_zoom, _zoom);
        }

        // Space spawns a short-lived sprite that destroys itself via a Lifetime script.
        if (Input.GetKeyDown(Key.Space))
        {
            SpawnConfetti();
        }

        // Spin every entity in place by mutating its Transform component.
        foreach (Entity entity in View<Transform>())
        {
            entity.GetComponent<Transform>().Rotation += new Vector3(0.0f, 0.0f, 45.0f * deltaTime);
        }
    }

    public override void OnEvent(Event e)
    {
        // Per-scene, event-driven input: Escape returns to the menu.
        if (e is KeyPressedEvent { Key: Key.Escape })
        {
            SceneManager.Load(new MenuScene());
            e.Handled = true;
        }
    }

    public override void OnRender()
    {
        if (_camera is not null)
        {
            RenderSystem.Render(this, _camera.ViewProjection);
        }
    }

    public override void OnImGuiRender()
    {
        ImGui.Begin("Sprites");
        ImGui.TextUnformatted("High-level: entities with Sprite2D, drawn by RenderSystem.");
        ImGui.TextUnformatted("WASD/arrows: pan   Wheel: zoom   Space: spawn   Esc: back");
        ImGui.Separator();
        if (ImGui.Button("Back to menu"))
        {
            SceneManager.Load(new MenuScene());
        }

        ImGui.End();
    }

    public override void OnExit() => _texture?.Dispose();

    private void SpawnConfetti()
    {
        Entity entity = Instantiate("Confetti");
        Transform transform = entity.GetComponent<Transform>();
        transform.Position = new Vector3(
            (float)((_random.NextDouble() * 2.0) - 1.0) * Aspect,
            (float)((_random.NextDouble() * 2.0) - 1.0),
            0.0f);
        transform.Scale = new Vector3(0.15f, 0.15f, 1.0f);

        entity.AddComponent(new Sprite2D
        {
            Color = new Vector4((float)_random.NextDouble(), (float)_random.NextDouble(), (float)_random.NextDouble(), 1.0f),
        });

        // The sprite removes itself after a couple of seconds.
        entity.AddScript(new Lifetime(2.0f));
    }

    private static float Aspect => (float)Application.Instance.Window.Width / Application.Instance.Window.Height;
}

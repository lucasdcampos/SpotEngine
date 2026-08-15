using System;
using System.Numerics;
using Spot.Core;
using Spot.Physics;
using Spot.Rendering;
using Spot.Scenes;
using Spot.UI;

namespace Spot.Game;

/// <summary>
/// Builds and drives the Physics 2D playground, a showcase for the Aether 2D backend: a static floor, walls
/// and a ramp; a stack of dynamic boxes; a few bouncy balls; and a WASD/jump character. Left-click spawns a
/// box and right-click spawns a ball at the cursor, R resets, and Escape returns to the menu. The whole scene
/// is assembled in <see cref="OnCreate"/> so the <c>.sptscene</c> only needs a camera and this bootstrap.
/// </summary>
public sealed class Physics2DPlayground : EntityBehaviour
{
    private static readonly Vector4 GroundColor = new(0.30f, 0.32f, 0.38f, 1f);
    private static readonly Vector4 WallColor = new(0.24f, 0.40f, 0.55f, 1f);
    private static readonly Vector4 PlayerColor = new(0.30f, 0.85f, 1.0f, 1f);
    private static readonly Vector4 BallColor = new(1.0f, 0.55f, 0.20f, 1f);

    private static readonly Vector4[] BoxColors =
    {
        new(0.90f, 0.45f, 0.35f, 1f),
        new(0.45f, 0.70f, 0.95f, 1f),
        new(0.55f, 0.80f, 0.45f, 1f),
        new(0.85f, 0.75f, 0.35f, 1f),
        new(0.70f, 0.50f, 0.85f, 1f),
    };

    private readonly Random _rng = new();

    public override void OnCreate()
    {
        // A snappier-than-Earth gravity gives the playground an arcade feel (less floaty, tighter jumps).
        PhysicsSettings.Gravity2D = new Vector2(0f, -20f);
        BuildWorld();
        BuildHint();
    }

    public override void OnUpdate(float deltaTime)
    {
        if (Input.GetKeyDown(Key.Escape))
        {
            SceneManager.Load("Scenes/MainMenu.sptscene");
            return;
        }

        if (Input.GetKeyDown(Key.R))
        {
            SceneManager.Load("Scenes/Physics2DPlayground.sptscene");
            return;
        }

        if (Input.GetMouseButtonDown(MouseButton.Left) && TryGetMouseWorld(out Vector2 boxPos))
        {
            float size = 0.6f + (float)_rng.NextDouble() * 0.7f;
            SpawnBox(boxPos, new Vector2(size, size), BoxColors[_rng.Next(BoxColors.Length)]);
        }

        if (Input.GetMouseButtonDown(MouseButton.Right) && TryGetMouseWorld(out Vector2 ballPos))
        {
            float radius = 0.35f + (float)_rng.NextDouble() * 0.35f;
            SpawnBall(ballPos, radius, 0.45f);
        }
    }

    private void BuildWorld()
    {
        // Static geometry: floor, two side walls, and a ramp.
        SpawnStatic("Floor", new Vector2(0f, -6.5f), new Vector2(27f, 1f), 0f, GroundColor);
        SpawnStatic("Wall Left", new Vector2(-13.5f, -2.5f), new Vector2(1f, 8f), 0f, WallColor);
        SpawnStatic("Wall Right", new Vector2(13.5f, -2.5f), new Vector2(1f, 8f), 0f, WallColor);
        SpawnStatic("Ramp", new Vector2(-8f, -4.6f), new Vector2(8f, 0.5f), 20f, WallColor);

        // A stack of dynamic boxes to knock over.
        for (int i = 0; i < 6; i++)
        {
            SpawnBox(new Vector2(7f, -5.4f + i * 1.02f), Vector2.One, BoxColors[i % BoxColors.Length]);
        }

        // Bouncy balls dropped from above.
        for (int i = 0; i < 4; i++)
        {
            SpawnBall(new Vector2(-3f + i * 1.6f, 5f + i * 0.6f), 0.5f, 0.5f);
        }

        // The controllable character.
        SpawnPlayer(new Vector2(0f, -4f));
    }

    private void BuildHint()
    {
        Text hint = UI.Text("LMB: box    RMB: ball    A/D: move    Space: jump    R: reset    Esc: menu");
        hint.FontSize = 20f;
        hint.Align = TextAlign.Center;
        hint.Color = new Vector4(0.85f, 0.88f, 0.92f, 1f);
        hint.Rect = new UIRect
        {
            Anchor = new Vector2(0.5f, 0f),
            Pivot = new Vector2(0.5f, 0f),
            Position = new Vector2(0f, 18f),
            Size = new Vector2(1000f, 28f),
        };
    }

    // A collider with no rigid body is static geometry; scale carries the box dimensions (collider Size = 1).
    private Entity SpawnStatic(string name, Vector2 position, Vector2 size, float rotationDeg, Vector4 color)
    {
        Entity e = Instantiate(name);
        TransformComponent t = e.GetComponent<TransformComponent>();
        t.Position = new Vector3(position, -0.5f);
        t.Rotation = new Vector3(0f, 0f, rotationDeg);
        t.Scale = new Vector3(size, 1f);
        e.AddComponent(new Sprite2DComponent { Color = color });
        e.AddComponent(new BoxCollider2DComponent { Size = Vector2.One });
        return e;
    }

    private Entity SpawnBox(Vector2 position, Vector2 size, Vector4 color)
    {
        Entity e = Instantiate("Box");
        TransformComponent t = e.GetComponent<TransformComponent>();
        t.Position = new Vector3(position, 0f);
        t.Scale = new Vector3(size, 1f);
        e.AddComponent(new Sprite2DComponent { Color = color });
        e.AddComponent(new BoxCollider2DComponent { Size = Vector2.One });
        e.AddComponent(new PhysicsBody2DComponent { IsDynamic = true, Mass = 1f, Friction = 0.5f });
        return e;
    }

    private Entity SpawnBall(Vector2 position, float radius, float restitution)
    {
        Entity e = Instantiate("Ball");
        TransformComponent t = e.GetComponent<TransformComponent>();
        t.Position = new Vector3(position, 0f);
        t.Scale = new Vector3(radius * 2f, radius * 2f, 1f); // sprite/collider diameter
        e.AddComponent(new Sprite2DComponent { Color = BallColor, Texture = Shapes.Circle });
        e.AddComponent(new CircleCollider2DComponent { Radius = 0.5f }); // scaled by world X -> world radius
        e.AddComponent(new PhysicsBody2DComponent { IsDynamic = true, Mass = 0.6f, Friction = 0.4f, Restitution = restitution });
        return e;
    }

    private void SpawnPlayer(Vector2 position)
    {
        Entity e = Instantiate("Player");
        TransformComponent t = e.GetComponent<TransformComponent>();
        t.Position = new Vector3(position, 0f);
        t.Scale = new Vector3(0.7f, 0.9f, 1f);
        e.AddComponent(new Sprite2DComponent { Color = PlayerColor });
        e.AddComponent(new BoxCollider2DComponent { Size = Vector2.One });
        // Friction 0 so the character never clings to walls; horizontal control is handled in the script.
        e.AddComponent(new PhysicsBody2DComponent { IsDynamic = true, Mass = 1.2f, Friction = 0f, FreezeRotation = true });
        e.AddScript<Physics2DCharacter>();
    }

    // Maps the mouse cursor (window pixels) into world space for the primary orthographic camera. The ortho
    // projection spans [-aspect*zoom, aspect*zoom] x [-zoom, zoom] around the camera position.
    private bool TryGetMouseWorld(out Vector2 world)
    {
        world = Vector2.Zero;

        Entity? cameraEntity = Find("Camera");
        if (cameraEntity is null || !cameraEntity.Value.TryGetComponent(out CameraComponent? camera))
        {
            return false;
        }

        Window window = Application.Instance.Window;
        if (window.Width == 0 || window.Height == 0)
        {
            return false;
        }

        Vector2 mouse = Input.MousePosition;
        float aspect = (float)window.Width / window.Height;
        float zoom = camera!.ZoomLevel;

        float ndcX = (mouse.X / window.Width) * 2.0f - 1.0f;
        float ndcY = 1.0f - (mouse.Y / window.Height) * 2.0f;

        Vector3 camPos = cameraEntity.Value.GetComponent<TransformComponent>().WorldPosition;
        world = new Vector2(camPos.X + ndcX * aspect * zoom, camPos.Y + ndcY * zoom);
        return true;
    }
}

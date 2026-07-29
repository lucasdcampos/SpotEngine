using System.Numerics;
using ImGuiNET;
using Spot.Core;
using Spot.Events;
using Spot.Game.Scripts;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Game.Scenes;

/// <summary>
/// A minimal Pong, built to exercise the engine end to end: entities with Transform + Sprite2D
/// drawn by the RenderSystem, gameplay driven by polled Input, and Escape handled as a scene event.
/// </summary>
internal sealed class PongScene : Scene
{
    // A fixed 16:9 arena so the gameplay math is independent of the window size.
    private const float ArenaHalfWidth = 16.0f / 9.0f;
    private const float ArenaHalfHeight = 1.0f;
    private const float PaddleHalfWidth = 0.03f;
    private const float PaddleHalfHeight = 0.22f;
    private const float BallHalfSize = 0.03f;
    private const float PaddleSpeed = 2.0f;
    private const float BallSpeed = 1.4f;

    private readonly Random _random = new();

    private OrthographicCamera? _camera;
    private Entity _leftPaddle;
    private Entity _rightPaddle;
    private Entity _ball;
    private Vector2 _ballVelocity;
    private int _leftScore;
    private int _rightScore;

    public override void OnEnter()
    {
        Renderer.SetClearColor(0.02f, 0.02f, 0.04f, 1.0f);
        _camera = new OrthographicCamera(-ArenaHalfWidth, ArenaHalfWidth, -ArenaHalfHeight, ArenaHalfHeight);

        _leftPaddle = CreatePaddle("Left Paddle", -ArenaHalfWidth + 0.1f, Key.W, Key.S);
        _rightPaddle = CreatePaddle("Right Paddle", ArenaHalfWidth - 0.1f, Key.Up, Key.Down);

        _ball = CreateEntity("Ball");
        Transform ballTransform = _ball.GetComponent<Transform>();
        ballTransform.Scale = new Vector3(BallHalfSize * 2.0f, BallHalfSize * 2.0f, 1.0f);
        _ball.AddComponent(new Sprite2D { Color = new Vector4(1.0f, 0.9f, 0.4f, 1.0f) });

        ServeBall(_random.Next(2) == 0 ? -1 : 1);
    }

    public override void OnUpdate(float deltaTime)
    {
        // Paddle movement lives in a PaddleController script on each paddle entity, run automatically
        // by the engine. The scene only drives the ball and scoring.
        UpdateBall(deltaTime);
    }

    public override void OnRender()
    {
        if (_camera is not null)
        {
            RenderSystem.Render(this, _camera);
        }
    }

    public override void OnEvent(Event e)
    {
        if (e is KeyPressedEvent { Key: Key.Escape })
        {
            SceneManager.Load(new MenuScene());
            e.Handled = true;
        }
    }

    public override void OnImGuiRender()
    {
        ImGui.Begin("Pong");
        ImGui.TextUnformatted($"{_leftScore}  :  {_rightScore}");
        ImGui.TextUnformatted("Left: W/S   Right: Up/Down   Esc: back");
        ImGui.Separator();
        if (ImGui.Button("Back to menu"))
        {
            SceneManager.Load(new MenuScene());
        }

        ImGui.End();
    }

    private Entity CreatePaddle(string name, float x, Key up, Key down)
    {
        Entity paddle = CreateEntity(name);
        Transform transform = paddle.GetComponent<Transform>();
        transform.Position = new Vector3(x, 0.0f, 0.0f);
        transform.Scale = new Vector3(PaddleHalfWidth * 2.0f, PaddleHalfHeight * 2.0f, 1.0f);
        paddle.AddComponent(new Sprite2D { Color = Vector4.One });
        paddle.AddScript(new PaddleController(up, down, PaddleSpeed, ArenaHalfHeight - PaddleHalfHeight));
        return paddle;
    }

    private void UpdateBall(float deltaTime)
    {
        Transform ballTransform = _ball.GetComponent<Transform>();
        Vector3 position = ballTransform.Position;
        position.X += _ballVelocity.X * deltaTime;
        position.Y += _ballVelocity.Y * deltaTime;

        // Bounce off the top and bottom walls.
        float limitY = ArenaHalfHeight - BallHalfSize;
        if (position.Y > limitY)
        {
            position.Y = limitY;
            _ballVelocity.Y = -_ballVelocity.Y;
        }
        else if (position.Y < -limitY)
        {
            position.Y = -limitY;
            _ballVelocity.Y = -_ballVelocity.Y;
        }

        // Bounce off the paddle the ball is heading toward.
        if (_ballVelocity.X < 0.0f && Overlaps(position, _leftPaddle))
        {
            BounceOffPaddle(ref position, _leftPaddle, 1);
        }
        else if (_ballVelocity.X > 0.0f && Overlaps(position, _rightPaddle))
        {
            BounceOffPaddle(ref position, _rightPaddle, -1);
        }

        // Score when the ball leaves the arena, then serve toward the player who was scored on.
        if (position.X < -ArenaHalfWidth)
        {
            _rightScore++;
            ServeBall(-1);
        }
        else if (position.X > ArenaHalfWidth)
        {
            _leftScore++;
            ServeBall(1);
        }
        else
        {
            ballTransform.Position = position;
        }
    }

    private bool Overlaps(Vector3 ballPosition, Entity paddle)
    {
        Transform transform = paddle.GetComponent<Transform>();
        return Math.Abs(ballPosition.X - transform.Position.X) < BallHalfSize + PaddleHalfWidth
            && Math.Abs(ballPosition.Y - transform.Position.Y) < BallHalfSize + PaddleHalfHeight;
    }

    private void BounceOffPaddle(ref Vector3 ballPosition, Entity paddle, int directionX)
    {
        Transform transform = paddle.GetComponent<Transform>();

        // Reflect horizontally and steer vertically based on where the ball hit the paddle.
        _ballVelocity.X = Math.Abs(_ballVelocity.X) * directionX;
        _ballVelocity.Y = (ballPosition.Y - transform.Position.Y) / PaddleHalfHeight * BallSpeed;

        // Nudge the ball to the paddle's edge so it can't get stuck inside.
        ballPosition.X = transform.Position.X + (directionX * (PaddleHalfWidth + BallHalfSize));
    }

    private void ServeBall(int directionX)
    {
        _ball.GetComponent<Transform>().Position = Vector3.Zero;
        _ballVelocity = new Vector2(BallSpeed * directionX, (float)((_random.NextDouble() * 2.0) - 1.0) * 0.5f);
    }
}

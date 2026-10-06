// Level 1 — the low level APIs only. You own the loop: open a window, poll it, draw, present.
// Arrow keys / WASD move the square, Escape quits. `--frames N` exits after N frames (smoke tests).
using System.Numerics;
using Spot.Engine;
using Spot.Engine.Graphics;

int maxFrames = args.Length == 2 && args[0] == "--frames" && int.TryParse(args[1], out int n) ? n : -1;

using var window = new Window(new WindowSpec { Title = "Spot — Hello Quad (level 1)", Width = 960, Height = 540 });
Renderer.SetClearColor(0.08f, 0.09f, 0.12f, 1.0f);

var position = new Vector2(480, 270);
const float Speed = 300.0f; // pixels per second

for (int frame = 0; window.IsOpen && frame != maxFrames; frame++)
{
    window.PollEvents();
    float dt = Time.Tick();

    if (Input.GetKeyDown(Key.Escape))
    {
        window.Close();
    }

    var move = Vector2.Zero;
    if (Input.GetKey(Key.Left) || Input.GetKey(Key.A)) move.X -= 1;
    if (Input.GetKey(Key.Right) || Input.GetKey(Key.D)) move.X += 1;
    if (Input.GetKey(Key.Down) || Input.GetKey(Key.S)) move.Y -= 1;
    if (Input.GetKey(Key.Up) || Input.GetKey(Key.W)) move.Y += 1;
    position += move * Speed * dt;

    Renderer.Clear();

    // Pixel coordinates with the origin at the bottom-left.
    Renderer2D.BeginScene(Matrix4x4.CreateOrthographicOffCenter(0, window.Width, 0, window.Height, -1, 1));

    // A pulsing backdrop of quads, then the player square on top.
    for (int x = 0; x < 12; x++)
    {
        float pulse = 0.5f + 0.5f * MathF.Sin(Time.ElapsedTime * 2.0f + x * 0.5f);
        Renderer2D.DrawQuad(new Vector2(60 + x * 75, 60), new Vector2(50, 50 + 40 * pulse), new Vector4(0.2f, 0.4f + 0.3f * pulse, 0.9f, 1));
    }

    Renderer2D.DrawRect(new Vector2(window.Width / 2f, window.Height / 2f), new Vector2(window.Width - 40, window.Height - 40),
        new Vector4(0.3f, 0.3f, 0.35f, 1), thickness: 2);
    Renderer2D.DrawQuad(position, new Vector2(48, 48), new Vector4(1.0f, 0.55f, 0.15f, 1));
    Renderer2D.EndScene();

    window.SwapBuffers();
}

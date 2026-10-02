// Level 2 — Spot.Framework: sprites, shapes, text, sound and named input actions, all built from code (no asset
// files, no engine). Move with WASD/arrows, Space plays a sound, Escape quits. `--frames N` exits after N frames.
using System.Numerics;
using Spot.Framework;
using Spot.Framework.Audio;
using Spot.Framework.Graphics;

int maxFrames = args.Length == 2 && args[0] == "--frames" && int.TryParse(args[1], out int n) ? n : -1;

using var window = new Window(new WindowSpec { Title = "Spot — Hello 2D (level 2)", Width = 1024, Height = 600 });
Renderer.SetClearColor(0.11f, 0.12f, 0.16f, 1.0f);
AudioManager.Init(); // the platform's audio device; runs silently if there is none

// Input actions: gameplay asks about intent, players can rebind keys.
Input.Bind("left", Key.A);
Input.Bind("left", Key.Left);
Input.Bind("right", Key.D);
Input.Bind("right", Key.Right);
Input.Bind("up", Key.W);
Input.Bind("up", Key.Up);
Input.Bind("down", Key.S);
Input.Bind("down", Key.Down);
Input.Bind("beep", Key.Space);

// A 2x2 sprite sheet generated in memory: each 8x8 cell a different tint (an Image is CPU pixels).
using Texture2D sheet = BuildSpriteSheet().ToTexture(pointFilter: true);

// A short sine blip, built as PCM.
AudioClip beep = BuildBeep(frequency: 660.0f, seconds: 0.12f);

var player = new Vector2(200, 300);
int cell = 0;

for (int frame = 0; window.IsOpen && frame != maxFrames; frame++)
{
    window.PollEvents();
    float dt = Time.Tick();
    FrameStats.Record(Time.UnscaledDeltaTime);
    AudioManager.Update(dt);

    if (Input.GetKeyDown(Key.Escape))
    {
        window.Close();
    }

    var move = new Vector2(
        (Input.GetAction("right") ? 1 : 0) - (Input.GetAction("left") ? 1 : 0),
        (Input.GetAction("up") ? 1 : 0) - (Input.GetAction("down") ? 1 : 0));
    player += move * 280.0f * dt;

    if (Input.GetActionDown("beep"))
    {
        AudioManager.Play(beep, volume: 0.4f);
        cell = (cell + 1) % 4;
    }

    Renderer.Clear();

    // World: pixel coordinates, origin bottom-left.
    Renderer2D.BeginScene(Matrix4x4.CreateOrthographicOffCenter(0, window.Width, 0, window.Height, -1, 1));
    Renderer2D.DrawCircle(new Vector2(800, 380), 90, new Vector4(0.95f, 0.75f, 0.2f, 1));
    Renderer2D.DrawTriangle(new Vector2(620, 120), new Vector2(980, 120), new Vector2(800, 300), new Vector4(0.25f, 0.6f, 0.45f, 1));
    Vector2[] hexagon = Enumerable.Range(0, 6)
        .Select(i => new Vector2(150, 120) + 60 * new Vector2(MathF.Cos(i * MathF.Tau / 6), MathF.Sin(i * MathF.Tau / 6)))
        .ToArray();
    Renderer2D.DrawPolygon(hexagon, new Vector4(0.5f, 0.35f, 0.85f, 1));

    // The player: one cell of the sprite sheet, spinning, flipped when moving left.
    var source = new Vector4(cell % 2 * 8, cell / 2 * 8, 8, 8);
    Renderer2D.DrawSprite(sheet, player, new Vector2(64, 64), source, rotation: Time.ElapsedTime, flipX: move.X < 0);
    Renderer2D.EndScene();

    // Screen-space UI: top-left origin, blended text with the built-in font.
    UIRenderer.Begin(window.Width, window.Height);
    UIRenderer.DrawQuad(new Vector2(16, 16), new Vector2(430, 76), new Vector4(0, 0, 0, 0.45f));
    UIRenderer.DrawText(Font.Default, "Hello from Spot.Framework", new Vector2(28, 24), 26, Vector4.One, TextLayoutOptions.Default);
    UIRenderer.DrawText(Font.Default, $"WASD to move - Space to beep - {FrameStats.Fps:0} fps", new Vector2(28, 60), 18,
        new Vector4(0.8f, 0.85f, 0.9f, 1), TextLayoutOptions.Default);
    UIRenderer.End();

    window.SwapBuffers();
}

AudioManager.Shutdown();

static Image BuildSpriteSheet()
{
    Vector4[] tints = { new(1, 0.4f, 0.3f, 1), new(0.3f, 0.8f, 1, 1), new(0.5f, 1, 0.4f, 1), new(1, 0.9f, 0.3f, 1) };
    byte[] pixels = new byte[16 * 16 * 4];
    for (int y = 0; y < 16; y++)
    {
        for (int x = 0; x < 16; x++)
        {
            Vector4 tint = tints[(y / 8) * 2 + x / 8];
            bool edge = x % 8 == 0 || y % 8 == 0 || x % 8 == 7 || y % 8 == 7;
            float shade = edge ? 0.35f : 1.0f;
            int i = (y * 16 + x) * 4;
            pixels[i] = (byte)(tint.X * shade * 255);
            pixels[i + 1] = (byte)(tint.Y * shade * 255);
            pixels[i + 2] = (byte)(tint.Z * shade * 255);
            pixels[i + 3] = 255;
        }
    }

    var image = new Image(16, 16, pixels);
    image.FlipVertically(); // rows above are top-to-bottom; textures are stored bottom-up
    return image;
}

static AudioClip BuildBeep(float frequency, float seconds)
{
    const int Rate = 22050;
    short[] pcm = new short[(int)(Rate * seconds)];
    for (int i = 0; i < pcm.Length; i++)
    {
        float t = i / (float)Rate;
        float envelope = 1.0f - i / (float)pcm.Length;
        pcm[i] = (short)(MathF.Sin(MathF.Tau * frequency * t) * envelope * short.MaxValue * 0.5f);
    }

    return new AudioClip(pcm, channels: 1, sampleRate: Rate);
}

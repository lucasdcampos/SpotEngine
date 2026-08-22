using System;
using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using Spot.Assets;
using Spot.Audio;
using Spot.Core;
using Spot.Events;
using Spot.Rendering;
using Spot.Scenes;

namespace Spot.Browser;

/// <summary>
/// The browser runtime host: the WebAssembly counterpart to the desktop <see cref="Application"/>. Where the
/// desktop app owns a Silk.NET window and a pull loop, the browser host is driven by JavaScript —
/// <c>requestAnimationFrame</c> calls <see cref="Frame"/>, DOM events call the input entry points, and the
/// page bootstraps the game through <see cref="StartAsync"/>. It wires the WebGL2 device, a slim 2D scene
/// renderer, DOM input, and the fetched-content asset store into the same neutral engine core the desktop
/// runs, minus the 3D/post pipeline and any ImGui overlay.
/// </summary>
/// <remarks>
/// The host exposes its members to JavaScript via <c>[JSExport]</c> and calls into the page via
/// <c>[JSImport]</c> (module <c>spot-host</c> for fetch). All entry points swallow exceptions and log, so a
/// bad frame or asset never tears the tab down — the engine's never-crash rule holds in the browser too.
/// </remarks>
public static partial class BrowserHost
{
    private const string Module = "spot-host";

    private static readonly BrowserAssetStore s_assets = new();

    // DOM events arrive between RAF calls, AFTER the previous frame's Input.NewFrame() already ran.
    // If we called Input.OnEvent immediately, NewFrame() at the start of the next frame would clear
    // pressed/released states before TickUI reads them — buttons would never fire. Queue instead and
    // flush after NewFrame() so each frame sees the events that arrived since the last tick.
    private static readonly List<Event> s_pendingEvents = new();

    /// <summary>
    /// Boots the game: brings up the WebGL2 renderer, installs the browser backends, fetches all cooked
    /// content listed in the content index into the in-memory store, loads the manifest, and switches to the
    /// start scene. JavaScript awaits the returned promise, then begins driving <see cref="Frame"/>.
    /// </summary>
    /// <param name="width">The initial drawable width in pixels.</param>
    /// <param name="height">The initial drawable height in pixels.</param>
    /// <param name="contentUrlBase">The URL prefix the cooked content is served under (e.g. <c>content</c>).</param>
    /// <param name="manifestPath">The manifest path, relative to the content root.</param>
    /// <param name="startScene">The start scene reference (a cooked scene path, relative to the content root).</param>
    [JSExport]
    internal static async Task StartAsync(int width, int height, string contentUrlBase, string manifestPath, string startScene)
    {
        try
        {
            Log.Init();
            Log.CoreInfo("Starting Spot browser host ({0}x{1}).", width, height);

            Display.SetSize(width, height);

            Renderer.Init(new WebGL2GraphicsDevice());
            Renderer2D.Init();
            ParticleRenderer.Init();
            UIRenderer.Init();
            Renderer.SetViewport(0, 0, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
            Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);

            // No native audio in the browser yet; the mixer degrades to silence until a Web Audio backend
            // is installed. Rendering and gameplay run regardless.
            AudioManager.Init(new SilentAudioBackend());

            AssetProvider.Current = s_assets;
            SceneRenderer.Callback = RenderScene2D;

            await PreloadContentAsync(contentUrlBase);
            InitializeContent(manifestPath);

            if (!string.IsNullOrEmpty(startScene))
            {
                SceneManager.Load(startScene);
                SceneManager.ApplyPendingSwitch();
            }

            Log.CoreInfo("Spot browser host ready ({0} assets preloaded).", s_assets.Count);
        }
        catch (Exception ex)
        {
            // A throwing StartAsync rejects the JS promise and silently prevents rendering, so log loudly.
            Log.CoreError("Browser host failed to start: {0}", ex);
        }
    }

    /// <summary>Advances and renders one frame. Called from <c>requestAnimationFrame</c> with the delta in seconds.</summary>
    /// <param name="deltaTime">The time since the previous frame, in seconds.</param>
    [JSExport]
    internal static void Frame(double deltaTime)
    {
        try
        {
            Input.NewFrame();
            FlushPendingEvents();

            // Clamp the delta so a background tab or GC hitch can't feed a huge dt into physics/scripts.
            float dt = Math.Min((float)deltaTime, 0.1f);
            Spot.Core.Time.NewFrame(dt);

            SceneManager.ApplyPendingSwitch();
            SceneManager.Update(Spot.Core.Time.DeltaTime);
            AudioManager.Update(dt);

            Renderer.Clear();
            SceneManager.Render();
        }
        catch (Exception ex)
        {
            Log.CoreError("Browser frame error: {0}", ex);
        }
    }

    /// <summary>Handles a canvas resize from JavaScript.</summary>
    /// <param name="width">The new drawable width in pixels.</param>
    /// <param name="height">The new drawable height in pixels.</param>
    [JSExport]
    internal static void Resize(int width, int height)
    {
        Display.SetSize(width, height);
        Renderer.SetViewport(0, 0, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
    }

    /// <summary>Dispatches a DOM keydown. <paramref name="code"/> is the <c>KeyboardEvent.code</c> value.</summary>
    /// <param name="code">The physical key code (e.g. <c>KeyW</c>, <c>ArrowUp</c>, <c>Space</c>).</param>
    [JSExport]
    internal static void KeyDown(string code)
    {
        Key key = MapKey(code);
        if (key != Key.Unknown)
        {
            s_pendingEvents.Add(new KeyPressedEvent(key));
        }
    }

    /// <summary>Dispatches a DOM keyup. <paramref name="code"/> is the <c>KeyboardEvent.code</c> value.</summary>
    /// <param name="code">The physical key code.</param>
    [JSExport]
    internal static void KeyUp(string code)
    {
        Key key = MapKey(code);
        if (key != Key.Unknown)
        {
            s_pendingEvents.Add(new KeyReleasedEvent(key));
        }
    }

    /// <summary>Dispatches typed text (a <c>keypress</c>/composition result), one event per character.</summary>
    /// <param name="text">The typed text.</param>
    [JSExport]
    internal static void TextInput(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (char c in text)
        {
            s_pendingEvents.Add(new KeyTypedEvent(c));
        }
    }

    /// <summary>Dispatches a pointer move in CSS pixels relative to the canvas' top-left.</summary>
    /// <param name="x">The pointer x position.</param>
    /// <param name="y">The pointer y position.</param>
    [JSExport]
    // Mouse position is a polled value, not a one-frame flag — dispatch immediately so hover is pixel-accurate.
    internal static void PointerMove(double x, double y) => DispatchNow(new MouseMovedEvent((float)x, (float)y));

    /// <summary>Dispatches a pointer button press. <paramref name="button"/> is the DOM <c>MouseEvent.button</c>.</summary>
    /// <param name="button">The DOM button index (0 left, 1 middle, 2 right).</param>
    [JSExport]
    internal static void PointerDown(int button) => s_pendingEvents.Add(new MouseButtonPressedEvent(MapButton(button)));

    /// <summary>Dispatches a pointer button release. <paramref name="button"/> is the DOM <c>MouseEvent.button</c>.</summary>
    /// <param name="button">The DOM button index (0 left, 1 middle, 2 right).</param>
    [JSExport]
    internal static void PointerUp(int button) => s_pendingEvents.Add(new MouseButtonReleasedEvent(MapButton(button)));

    /// <summary>Dispatches a mouse wheel scroll.</summary>
    /// <param name="deltaX">The horizontal scroll amount.</param>
    /// <param name="deltaY">The vertical scroll amount.</param>
    [JSExport]
    internal static void Wheel(double deltaX, double deltaY) =>
        s_pendingEvents.Add(new MouseScrolledEvent((float)deltaX, (float)deltaY));

    // Flushes all events queued since the last frame into Input and the active scene. Called after
    // Input.NewFrame() so pressed/released flags survive to be read by TickUI and scripts this frame.
    private static void FlushPendingEvents()
    {
        foreach (Event e in s_pendingEvents)
        {
            DispatchNow(e);
        }
        s_pendingEvents.Clear();
    }

    // Immediately routes an event through Input then the active scene (no queueing).
    private static void DispatchNow(Event e)
    {
        try
        {
            Input.OnEvent(e);
            if (!e.Handled)
            {
                SceneManager.DispatchEvent(e);
            }
        }
        catch (Exception ex)
        {
            Log.CoreError("Browser event handling error: {0}", ex);
        }
    }

    // The slim browser render pipeline: batched 2D sprites plus the screen-space UI tree. It stands in for the
    // desktop RenderSystem (which drives the full 3D/lighting/shadow/HDR/post path) for the MVP-2D browser port.
    private static void RenderScene2D(Scene scene, Matrix4x4 viewProjection, Vector3 cameraPosition)
    {
        _ = cameraPosition;

        Renderer2D.BeginScene(viewProjection);
        foreach (Entity entity in scene.View<TransformComponent, Sprite2DComponent>())
        {
            if (!entity.IsActiveInHierarchy())
            {
                continue;
            }

            TransformComponent transform = entity.GetComponent<TransformComponent>();
            Sprite2DComponent sprite = entity.GetComponent<Sprite2DComponent>();
            if (!transform.Enabled || !sprite.Enabled)
            {
                continue;
            }

            if (sprite.Texture is not null)
            {
                Renderer2D.DrawQuad(transform.Matrix, sprite.Texture, sprite.Color);
            }
            else
            {
                Renderer2D.DrawQuad(transform.Matrix, sprite.Color);
            }
        }

        Renderer2D.EndScene();

        ParticleRenderSystem.Render(scene, viewProjection);
        TextRenderSystem.Render(scene, viewProjection);

        Spot.UI.UIRoot? ui = scene.UIRootOrNull;
        if (ui is not null && ui.Children.Count > 0 && Display.Width > 0 && Display.Height > 0)
        {
            ui.Render(Display.Width, Display.Height);
        }
    }

    // Fetches the newline-separated content index, then each listed file, into the in-memory store. Files are
    // keyed by their content-relative path (what the engine resolves against an empty asset root below).
    private static async Task PreloadContentAsync(string contentUrlBase)
    {
        string baseUrl = string.IsNullOrEmpty(contentUrlBase) ? string.Empty : contentUrlBase.TrimEnd('/') + "/";
        string index = await FetchText(baseUrl + "content-index.txt");

        foreach (string rawLine in index.Replace("\r\n", "\n").Split('\n'))
        {
            string relative = rawLine.Trim();
            if (relative.Length == 0)
            {
                continue;
            }

            try
            {
                // JSImport cannot marshal Task<byte[]>, so the file is fetched as base64 text (a supported
                // Promise<string>) and decoded here. The overhead is paid once, at load.
                byte[] bytes = Convert.FromBase64String(await FetchBase64(baseUrl + relative));
                s_assets.Add(relative, bytes);
            }
            catch (Exception ex)
            {
                Log.CoreError("Failed to fetch cooked asset '{0}': {1}", relative, ex.Message);
            }
        }
    }

    // Points the asset system at the preloaded store: content resolves against an empty root (store keys are
    // content-relative), and the cooked manifest turns guid: references into cooked-artifact paths.
    private static void InitializeContent(string manifestPath)
    {
        AssetPath.Root = string.Empty;

        if (string.IsNullOrEmpty(manifestPath) || !s_assets.Exists(manifestPath))
        {
            Log.CoreWarn("No content manifest at '{0}'; guid references will not resolve.", manifestPath);
            return;
        }

        AssetManifest.Load(manifestPath);
        AssetPath.ContentResolver = static reference => AssetManifest.Active?.ResolveGuidRef(reference);
    }

    private static MouseButton MapButton(int domButton) => domButton switch
    {
        0 => MouseButton.Left,
        1 => MouseButton.Middle,
        2 => MouseButton.Right,
        3 => MouseButton.Button4,
        4 => MouseButton.Button5,
        _ => MouseButton.Unknown,
    };

    // Maps a DOM KeyboardEvent.code to the engine's backend-neutral Key. Letters, digits, and function keys
    // are computed; the rest come from the switch. Unrecognized codes yield Key.Unknown (ignored).
    private static Key MapKey(string code)
    {
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal))
        {
            char c = code[3];
            if (c is >= 'A' and <= 'Z')
            {
                return Key.A + (c - 'A');
            }
        }

        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal))
        {
            char d = code[5];
            if (d is >= '0' and <= '9')
            {
                return Key.Alpha0 + (d - '0');
            }
        }

        if (code.Length is 2 or 3 && code[0] == 'F' && int.TryParse(code.AsSpan(1), out int fn) && fn is >= 1 and <= 12)
        {
            return Key.F1 + (fn - 1);
        }

        return code switch
        {
            "Space" => Key.Space,
            "Enter" or "NumpadEnter" => Key.Enter,
            "Escape" => Key.Escape,
            "Tab" => Key.Tab,
            "Backspace" => Key.Backspace,
            "Delete" => Key.Delete,
            "ArrowRight" => Key.Right,
            "ArrowLeft" => Key.Left,
            "ArrowDown" => Key.Down,
            "ArrowUp" => Key.Up,
            "ShiftLeft" => Key.LeftShift,
            "ShiftRight" => Key.RightShift,
            "ControlLeft" => Key.LeftControl,
            "ControlRight" => Key.RightControl,
            "AltLeft" => Key.LeftAlt,
            "AltRight" => Key.RightAlt,
            "Comma" => Key.Comma,
            "Period" => Key.Period,
            "Slash" => Key.Slash,
            "Minus" => Key.Minus,
            "Quote" => Key.Apostrophe,
            _ => Key.Unknown,
        };
    }

    [JSImport("host.fetchText", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> FetchText(string url);

    [JSImport("host.fetchBase64", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> FetchBase64(string url);
}

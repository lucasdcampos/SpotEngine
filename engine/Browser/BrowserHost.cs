using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using Spot.Assets;
using Spot.Audio;
using Spot.Core;
using Spot.Rendering;
using Spot.Scenes;
using Spot.IO;

namespace Spot.Browser;

/// <summary>
/// The engine's browser host: the WebAssembly counterpart to the desktop <see cref="Application"/>. It boots the
/// game on top of the browser platform — a <see cref="Window"/> over the page's canvas (WebGL2 device, DOM
/// input through <see cref="BrowserPlatform"/>) — by fetching the cooked content, loading the manifest and the
/// start scene, and running one engine frame per <see cref="BrowserPlatform.AnimationFrame"/>. It runs the same
/// neutral engine core as the desktop, minus the HDR/bloom/post pipeline and any ImGui overlay.
/// </summary>
/// <remarks>
/// The page calls <see cref="StartAsync"/> (<c>[JSExport]</c>) and fetches through <c>[JSImport]</c> (module
/// <c>spot-host</c>). Every step swallows exceptions and logs, so a bad frame or asset never tears the tab
/// down — the engine's never-crash rule holds in the browser too.
/// </remarks>
public static partial class BrowserHost
{
    private const string Module = "spot-host";

    private static readonly BrowserAssetStore s_assets = new();

    private static Window? s_window;

    /// <summary>
    /// Boots the game: brings up the WebGL2 renderer, installs the browser backends, fetches all cooked
    /// content listed in the content index into the in-memory store, loads the manifest, and switches to the
    /// start scene. JavaScript awaits the returned promise, then begins raising animation frames.
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
            EngineLogging.Init();
            Log.CoreInfo("Starting Spot browser host ({0}x{1}).", width, height);

            // The window installs the WebGL2 device and the Pointer Lock cursor controller, and routes DOM input
            // into Input; whatever input the engine did not consume reaches the active scene.
            s_window = new Window(new WindowSpec { Width = width, Height = height });
            s_window.SetEventCallback(static e =>
            {
                if (!e.Handled)
                {
                    SceneManager.DispatchEvent(e);
                }
            });

            Renderer3D.Init();
            ParticleRenderer.Init();
            Renderer.SetClearColor(0.1f, 0.1f, 0.15f, 1.0f);

            // Web Audio drives the browser mixer. If the AudioContext can't be created, the backend reports
            // unavailable and AudioManager degrades to silence on its own; rendering and gameplay run regardless.
            AudioManager.Init(new WebAudioBackend());

            FileSystem.Current = s_assets;
            EngineAssets.Install();

            // Run the same shared 3D-first RenderSystem the desktop uses. No IScenePostProcessor is installed,
            // so the scene renders straight to the screen (no HDR/bloom/post) — the browser's current limit.
            SceneRenderer.Callback = static (scene, viewProjection, cameraPosition) =>
                RenderSystem.Render(scene, viewProjection, cameraPosition);

            await PreloadContentAsync(contentUrlBase);
            InitializeContent(manifestPath);

            if (!string.IsNullOrEmpty(startScene))
            {
                SceneManager.Load(startScene);
                SceneManager.ApplyPendingSwitch();
            }

            BrowserPlatform.AnimationFrame += Frame;

            Log.CoreInfo("Spot browser host ready ({0} assets preloaded).", s_assets.Count);
        }
        catch (Exception ex)
        {
            // A throwing StartAsync rejects the JS promise and silently prevents rendering, so log loudly.
            Log.CoreError("Browser host failed to start: {0}", ex);
        }
    }

    // One engine frame, run on every animation frame raised by the page.
    private static void Frame(double deltaTime)
    {
        try
        {
            s_window?.PollEvents();

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

    // Fetches the newline-separated content index, then each listed file, into the in-memory store. Files are
    // keyed by their content-relative path (what the engine resolves against an empty asset root below).
    // TODO(scale): this preloads the entire cooked payload up front, over a base64 text channel (~33% inflation)
    // and fully into memory. Fine for the current small projects; larger games will want streaming / on-demand
    // fetch keyed off the manifest instead of a single blocking preload.
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

    [JSImport("host.fetchText", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> FetchText(string url);

    [JSImport("host.fetchBase64", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string> FetchBase64(string url);
}

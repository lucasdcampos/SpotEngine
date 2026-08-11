using Spot.Assets;
using Spot.Core;
using Spot.DebugUI.UI;
using Spot.Editor.UI;

namespace Spot.Editor;

public static class Program
{
    public static void Main(string[] args)
    {
        string fontsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        var spec = new ApplicationSpec
        {
            Name = "Spot.Editor",
            // Start at the launcher's compact size; the editor restores its own size when it loads.
            Window = new WindowSpec { Title = "Spot Launcher", Width = 1000, Height = 620 },
            FontPath = System.IO.Path.Combine(fontsDir, "Inter-Regular.ttf"),
            FontSize = 16,
            // Extra atlas fonts, resolved by EditorFonts in registration order: a heavier Inter for
            // titles, JetBrains Mono for the console, and a large standalone Font Awesome atlas for the
            // asset-browser tiles (drawn at 48–128px, where upscaling the 16px merged glyphs would blur).
            // Keep this order in sync with EditorFonts.
            AdditionalFonts =
            {
                new FontSpec(System.IO.Path.Combine(fontsDir, "Inter-Medium.ttf"), 17.0f),
                new FontSpec(System.IO.Path.Combine(fontsDir, "JetBrainsMono-Regular.ttf"), 15.0f),
                new FontSpec(System.IO.Path.Combine(fontsDir, "fa-solid-900.ttf"), 72.0f, EditorIcons.GlyphRanges),
            },
            // Font Awesome 6 (Solid) merged into the body font so its glyphs render inline with text,
            // matched to the 16px body size.
            IconFont = new IconFontSpec(
                System.IO.Path.Combine(fontsDir, "fa-solid-900.ttf"), 16.0f, EditorIcons.GlyphRanges),
        };

        try
        {
            // Authoring host: register the Assimp source importer so the editor can load source models
            // directly (the runtime deliberately does not — it loads cooked .spmesh).
            ModelImporter.Register(new AssimpModelImporter());

            var app = new Spot.Core.Application(spec);
            app.Run(new LauncherScene());
        }
        catch (Exception ex)
        {
            // Reaching here means an error escaped the engine's per-frame safety nets — the engine
            // could not start or was irreversibly compromised. There is nothing left to recover to,
            // so surface a clear fatal message rather than a raw unhandled-exception dump, then exit.
            System.Console.Error.WriteLine($"Fatal: Spot.Editor stopped because of an unrecoverable error.\n{ex}");
            System.Environment.Exit(1);
        }
    }
}

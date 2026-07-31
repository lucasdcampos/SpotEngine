using Spot.Core;

namespace Spot.Editor;

public static class Program
{
    public static void Main(string[] args)
    {
        var spec = new ApplicationSpec
        {
            Name = "Spot.Editor",
            // Start at the launcher's compact size; the editor restores its own size when it loads.
            Window = new WindowSpec { Title = "Spot Launcher", Width = 1000, Height = 620 },
            FontPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Inter-Regular.ttf"),
            FontSize = 16
        };

        try
        {
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

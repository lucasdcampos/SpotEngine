using Spot.Core;

namespace Spot.Editor;

public static class Program
{
    public static void Main(string[] args)
    {
        var spec = new ApplicationSpec
        {
            Name = "Spot.Editor",
            Window = new WindowSpec { Title = "Spot.Editor", Width = 1280, Height = 720 },
            FontPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Inter-Regular.ttf"),
            FontSize = 16
        };

        var app = new Spot.Core.Application(spec);
        app.Run(new LauncherScene());
    }
}

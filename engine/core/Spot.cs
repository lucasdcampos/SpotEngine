using OpenTK.Windowing.Desktop;
using SpotEngine.Internal.Graphic;

namespace SpotEngine
{
    /// <summary>
    /// The Engine class, here is where the magic happens.
    /// Your game is executed through here, passing the
    /// Render API.
    /// </summary>
    public class Spot
    {
        private static IGraphicsRenderer graphicsRenderer;
        public static RenderMode renderMode = RenderMode.Default;

        public static void Run(RenderMode renderer)
        {
            renderMode = renderer;
            Run();
        }

        public static void Run()
        {

            Vec2i res;
            res.x = Screen.res.x;
            res.y = Screen.res.y;

            Input.Init();

            // Will load a blank level if no level was specified
            if(Game.activeLevel == null)
            {
                Level blank = new Level();
                blank.name = "Blank Level";
                Game.LoadLevel(blank);
            }

            switch (renderMode)
            {
                case RenderMode.Default:
                    graphicsRenderer = new SFMLRenderer(res.x, res.y);
                    break;
                case RenderMode.OpenGL:
                    graphicsRenderer = new OpenTKGraphicsRenderer(GameWindowSettings.Default, NativeWindowSettings.Default);
                    break;
                case RenderMode.SystemDrawing:
                    graphicsRenderer = new SDGraphicsRenderer(res.x, res.y);
                    break;
                case RenderMode.SFML:
                    graphicsRenderer = new SFMLRenderer(res.x, res.y);
                    break;
            }

            graphicsRenderer.Initialize();

            while (!ExitConditionMet())
            {
                Time.CalculateDeltaTime();
            }

            graphicsRenderer.Cleanup();
        }



        private static bool ExitConditionMet()
        {
            return false;
        }
    }
}
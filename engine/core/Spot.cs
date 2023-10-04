using OpenTK.Windowing.Desktop;
using SpotEngine.Internal.Graphics.SFML;

namespace SpotEngine
{
    /// <summary>
    /// The Engine class, where you can modify attributes of your game
    /// </summary>
    public class Spot
    {
        private IGraphicsRenderer graphicsRenderer;
        public Game game = new Game();
        public static float deltaTime;
        private static Spot instance;
        public static RenderMode renderMode = RenderMode.Default;

        public static int unitSize = 50;
        public static int winWidth, winHeight;
        public static Spot Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Spot();
                }
                return instance;
            }
        }


        public void Run(RenderMode renderer, int width, int height)
        {
            winWidth = width;
            winHeight = height;

            Input.Init();

            switch (renderer)
            {
                case RenderMode.Default:
                    graphicsRenderer = new SFMLRenderer(width, height);
                    break;
                case RenderMode.OpenGL:
                    graphicsRenderer = new OpenTKGraphicsRenderer(GameWindowSettings.Default, NativeWindowSettings.Default);
                    renderMode = RenderMode.OpenGL;
                    break;
                case RenderMode.SystemDrawing:
                    graphicsRenderer = new SDGraphicsRenderer(width, height);
                    renderMode = RenderMode.SystemDrawing;
                    break;
                case RenderMode.SFML:
                    graphicsRenderer = new SFMLRenderer(width, height);
                    break;
            }
            

            graphicsRenderer.Initialize();

            game.InitEntities();

            

            while (!ExitConditionMet())
            {
                CalculateDeltaTime();
            }

            graphicsRenderer.Cleanup();

        }

        void CalculateDeltaTime()
        {
            deltaTime = 0;
        }

        private bool ExitConditionMet()
        {
            return false;
        }
    }
}
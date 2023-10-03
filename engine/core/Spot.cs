using System.Diagnostics;
using System.Drawing;

namespace SpotEngine
{
    /// <summary>
    /// The Engine class, where you can modify attributes of your game
    /// </summary>
    public class Spot
    {
        public IGraphicsRenderer graphicsRenderer;
        public Game game = new Game();
        public bool initialized;

        private static Spot instance;

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


        public void Run(IGraphicsRenderer renderer)
        {
            graphicsRenderer = renderer;

            if (graphicsRenderer == null)
            {
                graphicsRenderer = new SDGraphicsRenderer(800, 600);
                throw new InvalidOperationException("Graphics renderer is not set.");
            }

            graphicsRenderer.Initialize(800, 600);

            game.InitEntities();


            while (!ExitConditionMet())
            {
                graphicsRenderer.RenderFrame();

            }


            graphicsRenderer.Cleanup();
        }

        private bool ExitConditionMet()
        {
            return false;
        }
    }
}
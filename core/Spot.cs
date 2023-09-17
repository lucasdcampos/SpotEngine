using OpenTK.Windowing.Desktop;

namespace SpotEngine
{
    public class Spot
    {

        public Game game;

        public Spot()
        {
            var gameWindowSettings = new GameWindowSettings();
            var nativeWindowSettings = new NativeWindowSettings();

            game = new Game(gameWindowSettings, nativeWindowSettings);
            
        }

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

        public static void Run()
        {
            Instance.game.Run();
        }
    }
}

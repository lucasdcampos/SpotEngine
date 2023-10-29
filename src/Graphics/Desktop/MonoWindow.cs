using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace SpotEngine.Internal.Graphics
{
    internal class MonoWindow : Window
    {
        public MonoWindow(string title, int width, int height) : base(title, width, height)
        {
            this.Title = title;
            this.Width = width;
            this.Height = height;

            Log.Custom("Set RendererAPI to OpenGL (with XNA)", ConsoleColor.Green);
            Initialize();
        }

        public override void Initialize()
        {
            base.Initialize();
            MonoGame game = new MonoGame(this);
            game.Run();
        }

        public class MonoGame : Game
        {
            Window baseWindow;
            GraphicsDeviceManager graphics;
            SpriteBatch spriteBatch;

            public MonoGame(Window baseWindow)
            {
                graphics = new GraphicsDeviceManager(this);
                Content.RootDirectory = "Content";
                this.baseWindow = baseWindow;
            }

            protected override void Initialize()
            {
                base.Initialize();
            }

            protected override void Draw(GameTime gameTime)
            {

                graphics.GraphicsDevice.Clear(Color.Magenta);

                base.Draw(gameTime);
            }
        }

    }
}

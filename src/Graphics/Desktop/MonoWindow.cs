using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SpotEngine.Graphics.Renderer.XNA;

namespace SpotEngine.Internal.Graphics
{
    internal class MonoWindow : Window
    {
        public MonoGame game;
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
            game = new MonoGame(this);
            game.Run();
        }

        public class MonoGame : Game
        {
            Window baseWindow;
            GraphicsDeviceManager graphics;
            SpriteBatch spriteBatch;

            private SquareManagerXNA _squareManager;

            public MonoGame(Window baseWindow)
            {
                graphics = new GraphicsDeviceManager(this);
                Content.RootDirectory = "Content";
                this.baseWindow = baseWindow;
            }

            protected override void Initialize()
            {
                base.Initialize();

                _squareManager = new SquareManagerXNA(GraphicsDevice);

               //_squareManager.CreateSquare(new Vector2(0, 0), new Vector2(1,1), Color.Red);
                //_squareManager.CreateSquare(new Vector2(0, 0), new Vector2(1f,1f), Color.Green);
            
            }

            Texture2D texture;
            protected override void LoadContent()
            {
                spriteBatch = new SpriteBatch(GraphicsDevice);

                texture = new Texture2D(GraphicsDevice, 1, 1);
                texture.SetData(new[] { Color.White });
            }

            protected override void Draw(GameTime gameTime)
            {
                GraphicsDevice.Clear(Color.Magenta);

                spriteBatch.Begin();

                _squareManager.Draw(spriteBatch);

                spriteBatch.End();

                base.Draw(gameTime);
            }

            public SquareManagerXNA GetSquareManager()
            {
                return _squareManager;
            }
        }

    }
}

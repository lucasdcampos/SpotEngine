using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace SpotEngine.Internal.Graphics
{
    internal class MonoWindow : Window
    {
        public MonoGame game;
        public int unitSize;
        public MonoWindow(string title, int width, int height) : base(title, width, height)
        {
            this.Title = title;
            this.Width = width;
            this.Height = height;
            unitSize = UnitSize;
            Log.Custom("Set RendererAPI to OpenGL (with XNA)", ConsoleColor.Green);

            game = new MonoGame(this);
        }

        public override void Initialize()
        {
            base.Initialize();
            
            game.Run();
            
        }

        public class MonoGame : Game
        {
            Window baseWindow;
            GraphicsDeviceManager graphics;
            SpriteBatch spriteBatch;
            List<SpriteRenderer> sprites = new();

            public MonoGame(Window baseWindow)
            {
                graphics = new GraphicsDeviceManager(this);
                Content.RootDirectory = "Assets";
                this.baseWindow = baseWindow;

                Window.Title = baseWindow.Title;
            }

            protected override void Initialize()
            {
                base.Initialize();
                IsMouseVisible = true;

                if(Scene.current != null)
                {
                    Scene.current.Start();
                }

                Window.Title = baseWindow.Title;
            }

            Texture2D texture;
            protected override void LoadContent()
            {
                spriteBatch = new SpriteBatch(GraphicsDevice);
            }

            protected override void Update(GameTime gameTime)
            {
                base.Update(gameTime);

                if (Scene.current != null)
                {
                    Scene.current.Update();
                }
            }
            protected override void Draw(GameTime gameTime)
            {
                GraphicsDevice.Clear(Microsoft.Xna.Framework.Color.Magenta);

                spriteBatch.Begin();

                DrawSprites();

                spriteBatch.End();

                base.Draw(gameTime);
            }

            public void RegisterSprite(SpriteRenderer renderer)
            {
                sprites.Add(renderer);
            }

            void DrawSprites()
            {
                foreach (SpriteRenderer renderer in sprites)
                {
                    Vec3 pos = renderer.transform.pos;
                    Vec3 scale = renderer.transform.scale;
                    Vec3 rot = renderer.transform.rot;
                    Color color = renderer.sprite.color;

                    Vector2 finalPos = new Vector2((pos.X * UnitSize) + (GraphicsDevice.Viewport.Width / 2), (-pos.Y * UnitSize) + (GraphicsDevice.Viewport.Height / 2));
                    Vector2 finalScale = new Vector2(scale.X * UnitSize, scale.Y * UnitSize);
                    Vector3 finalRot = new Vector3(rot.X, rot.Y, rot.Z);

                    Microsoft.Xna.Framework.Color finalColor = new Microsoft.Xna.Framework.Color(color.r, color.g, color.b, color.a);
                    if (renderer.sprite == null || renderer.sprite.texturePath == null)
                    {
                        // rendering default white square instead
                        texture = new Texture2D(GraphicsDevice, 1, 1);
                        texture.SetData(new[] { Microsoft.Xna.Framework.Color.White });
                        spriteBatch.Draw(texture, finalPos, null, finalColor, 0, new Vector2(0.5f), finalScale, SpriteEffects.None, 0f);
                    }

                    else
                    {
                        texture = Texture2D.FromStream(GraphicsDevice, renderer.sprite.texturePath);

                        spriteBatch.Draw(texture, finalPos, null, finalColor, 0, new Vector2(texture.Width / 2f, texture.Height / 2f), finalScale / Math.Max(texture.Width, texture.Height), SpriteEffects.None, 0f);
                    }
                }
            }

            protected override void OnExiting(object sender, EventArgs args)
            {
                Application.GetApp().Stop();
                base.OnExiting(sender, args);
            }

        }
    }
}



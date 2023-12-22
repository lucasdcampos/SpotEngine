using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.ImGuiNet;
using SpotEngine.Internal.Utils;
using SpotEditor;

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
            Log.Custom("Set RenderMode to MonoGame", ConsoleColor.Green);
            game = new MonoGame(this);

        }

        public override void Initialize()
        {
            base.Initialize();
            
            game.Run();
            
        }

        public class MonoGame : Game
        {
            MonoWindow baseWindow;
            GraphicsDeviceManager graphics;
            SpriteBatch spriteBatch;
            List<Sprite> sprites = new();
            List<SpriteRenderer> renderers = new();
            private DateTime _lastFrameTime;
            public static ImGuiRenderer GuiRenderer;
            private bool _toolActive = true;
            private System.Numerics.Vector4 _colorV4;
            public MonoGame(MonoWindow baseWindow)
            {
                graphics = new GraphicsDeviceManager(this);
                Content.RootDirectory = "Assets";
                this.baseWindow = baseWindow;

            }

            protected override void Initialize()
            {
                GuiRenderer = new ImGuiRenderer(this);

                base.Initialize();

                this.IsFixedTimeStep = true;
                this.TargetElapsedTime = TimeSpan.FromSeconds(1d / 30d);

                Window.Title = baseWindow.Title;
                IsMouseVisible = true;

                if(Scene.current != null)
                {
                    Scene.current.Start();
                }
            }

            Texture2D texture;
            protected override void LoadContent()
            {
                spriteBatch = new SpriteBatch(GraphicsDevice);
                GuiRenderer.RebuildFontAtlas();

            }

            protected override void Update(GameTime gameTime)
            {
                base.Update(gameTime);

                var delta = (float)gameTime.ElapsedGameTime.TotalSeconds;

                Time.deltaTime = delta;

                if (Scene.current != null && Application.isPlaying)
                {
                    Scene.current.Update();
                }

                // handling input
                KeyboardState state = Keyboard.GetState();

                foreach (Keys k in Enum.GetValues(typeof(Keys)))
                {
                    Input.SetKeyState((int)XnaKeysToSpot.Convert((k)), state.IsKeyDown(k));
                }

            }
            System.Numerics.Vector3 v = new System.Numerics.Vector3();
            protected override void Draw(GameTime gameTime)
            {
                GraphicsDevice.Clear(Microsoft.Xna.Framework.Color.Black);

                spriteBatch.Begin();

                DrawSprites();

                spriteBatch.End();

                base.Draw(gameTime);

                GuiRenderer.BeginLayout(gameTime);

                EditorGUI.DrawInspector(true);
                EditorGUI.DrawTopMenuButtons(true);

                GuiRenderer.EndLayout();

            }

            internal void RegisterSprite(SpriteRenderer renderer, Sprite sprite)
            {
                renderers.Add(renderer);
            }

            internal void RegisterSprite(Sprite sprite)
            {
                sprites.Add(sprite);
            }

            void DrawSprites()
            {
                var orderedList = renderers.OrderBy(sprite => sprite.sprite.layer).ToList();

                foreach (SpriteRenderer renderer in orderedList)
                {
                    Vec3 pos = renderer.transform.pos;
                    Vec3 scale = renderer.transform.scale;
                    Vec3 rot = renderer.transform.rot;
                    Color color = renderer.sprite.color;

                    Vector2 finalPos = new Vector2((pos.X * UnitSize) + (GraphicsDevice.Viewport.Width / 2), (-pos.Y * UnitSize) + (GraphicsDevice.Viewport.Height / 2));
                    Vector2 finalScale = new Vector2(scale.X * UnitSize, scale.Y * UnitSize);
                    Vector3 finalRot = new Vector3(rot.X, rot.Y, rot.Z);

                    Microsoft.Xna.Framework.Color finalColor 
                        = new Microsoft.Xna.Framework.Color(color.r, color.g, color.b, color.a);

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
using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SpotEngine.Internal.Input;

namespace SpotEngine.Internal.Graphic
{
    internal class SFMLRenderer : IGraphicsRenderer
    {
        public static RenderWindow window;
        private List<DrawableSquare> squares = new List<DrawableSquare>();
        int width, height;
        private View camera = new View();

        Clock clock = new Clock();
        SFML.Graphics.Color backgroundColor = SFML.Graphics.Color.Black;

        Vector2u initialSize;
        public SFMLRenderer(int width, int height)
        {
            window = new RenderWindow(new VideoMode((uint)width, (uint)height), Game.title);
            window.Closed += (sender, e) => window.Close();
            this.width = width;
            this.height = height;

            initialSize = window.Size;
            Initialize();
        }

        public void Initialize()
        {
            
            Run();
        }

        Vec3 camPos;
        public void RenderFrame()
        {
            float scaleX = (float)SFMLRenderer.window.Size.X / initialSize.X;
            float scaleY = (float)SFMLRenderer.window.Size.Y / initialSize.Y;
            Screen.updateUnitSize((int)scaleX, (int)scaleY);

            if(Camera.main != null)
            {
                camPos = Camera.main.transform.pos;
                backgroundColor = new SFML.Graphics.Color((byte)Camera.main.backgroundColor.r, (byte)Camera.main.backgroundColor.g, (byte)Camera.main.backgroundColor.b);
            }
            else
            {
                Echo.Alert("(!) Main Camera is missing!");
                backgroundColor = SFML.Graphics.Color.Magenta;
            }

            window.Clear(backgroundColor);


            Game.UpdateEntities();
            SpotEngine.Physics.CheckForCollisions();

            window.Display();

        }


        public void Cleanup()
        {
            window.Close();
        }

        public void DrawSquare(float x, float y, float size, Color color)
        {
            squares.Add(new DrawableSquare { X = x, Y = y, Size = size, Color = color });
        }

        private class DrawableSquare
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Size { get; set; }
            public Color Color { get; set; }
        }


        public void Run()
        {
            // Creating the InputHandler for SFML
            SFMLInputHandler input = new SFMLInputHandler(window);

            while (window.IsOpen)
            {
                Time.UpdateDeltaTime(clock.Restart().AsSeconds()) ;
                window.DispatchEvents();
                window.SetTitle(Game.title);
                RenderFrame();
                FloatRect viewport = new FloatRect(0, 0, 1, 1);
                camera.Center = new Vector2f((window.Size.X / 2) + camPos.x * Screen.realUnitSize, (window.Size.Y / 2) + camPos.y * Screen.realUnitSize);
                camera.Viewport = viewport;
                window.SetView(camera);

                window.Resized += (sender, e) =>
                {
                   
                };
            }
        }


    }
}


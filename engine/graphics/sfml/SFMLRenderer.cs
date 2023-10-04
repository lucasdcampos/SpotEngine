using SpotEngine;
using SFML.Graphics;
using SFML.System;
using SFML.Window;
using SpotEngine.Graphics.SFML;

namespace SpotEngine.Graphics.SFML
{
    public class SFMLRenderer : IGraphicsRenderer
    {
        public static RenderWindow window;
        private List<DrawableSquare> squares = new List<DrawableSquare>();
        int width, height;
        public SFMLRenderer(int width, int height)
        {
            window = new RenderWindow(new VideoMode((uint)width, (uint)height), Spot.Instance.game.title);
            window.Closed += (sender, e) => window.Close();
            this.width = width;
            this.height = height;

            Initialize();
        }

        public void Initialize()
        {
            Run();
        }

        public void RenderFrame()
        {

            window.Clear(Color.Black);

            foreach (var square in squares)
            {
                var rectangle = new RectangleShape(new Vector2f(square.Size, square.Size))
                {
                    Position = new Vector2f(square.X, square.Y),
                    FillColor = square.Color
                };

                window.Draw(rectangle);
            }
            Spot.Instance.game.UpdateEntities();

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
            while (window.IsOpen)
            {
                window.DispatchEvents();

                RenderFrame();
            }
        }

    }
}


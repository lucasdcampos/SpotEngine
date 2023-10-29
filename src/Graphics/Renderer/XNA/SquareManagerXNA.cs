using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SpotEngine.Graphics.Renderer.XNA
{
    public class SquareManagerXNA
    {
        private List<SquareRendererXNA> _squares;
        private GraphicsDevice _graphicsDevice;

        public SquareManagerXNA(GraphicsDevice graphicsDevice)
        {
            _squares = new List<SquareRendererXNA>();
            _graphicsDevice = graphicsDevice;
        }

        public SquareRendererXNA CreateSquare(Vector2 position, Vector2 size, Color color)
        {
            var square = new SquareRendererXNA(_graphicsDevice, position, size, color);
            _squares.Add(square);
            return square;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            foreach (var square in _squares)
            {
                square.Draw(spriteBatch);
            }
        }
    }

}

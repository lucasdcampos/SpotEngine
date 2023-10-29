using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;


namespace SpotEngine.Graphics.Renderer.XNA
{
    public class SquareRendererXNA
    {
        private Texture2D _texture;
        private Vector2 _position;
        private Vector2 _size;
        private Color _color;
        private GraphicsDevice _graphicsDevice;

        public SquareRendererXNA(GraphicsDevice graphicsDevice, Vector2 position, Vector2 size, Color color)
        {
            _texture = new Texture2D(graphicsDevice, 1, 1);
            _texture.SetData(new[] { Color.White });

            _graphicsDevice = graphicsDevice;

            int centerX = _graphicsDevice.Viewport.Width / 2;
            int centerY = _graphicsDevice.Viewport.Height / 2;
            _position = new Vector2(centerX + position.X - size.X / 2, centerY + position.Y - size.Y / 2);

            _size = size;
            _color = color;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            Rectangle squareRect = new Rectangle((int)_position.X, (int)_position.Y, (int)_size.X, (int)_size.Y);
            spriteBatch.Draw(_texture, squareRect, _color);
        }
    }

}



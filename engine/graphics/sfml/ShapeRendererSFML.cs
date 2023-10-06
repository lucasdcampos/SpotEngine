using SFML.Graphics;
using SFML.System;

namespace SpotEngine.Internal.Graphic
{
    /// <summary>
    /// This class is used to communicate with the SFML
    /// and render sprites or simple shapes.
    /// </summary>
    internal class ShapeRendererSFML
    {
        RectangleShape rectangle;
        public bool initialized = false;
        Texture texture;
        SFML.Graphics.Sprite sprite;
        Transform transform;

        private Vector2f originalWindowSize;
        private Vector2f originalSpriteSize;

        public void Init(Transform transform, string spritePath)
        {
            if (initialized) return;

            this.transform = transform;
            originalWindowSize = new Vector2f(SFMLRenderer.window.Size.X, SFMLRenderer.window.Size.Y);

            if (spritePath == null || spritePath == string.Empty || spritePath == "")
            {
                rectangle = new RectangleShape(new Vector2f(transform.scale.x * Screen.unitSize, transform.scale.y * Screen.unitSize));

            }
            else
            {
                texture = new Texture(spritePath);
                sprite = new SFML.Graphics.Sprite(texture);
                originalSpriteSize = new Vector2f(texture.Size.X, texture.Size.Y);
                sprite.Origin = originalSpriteSize / 2.0f;

            }


            initialized = true;
        }
        public void Render(RenderWindow window)
        {

            float newUnitSize = Screen.realUnitSize;


            if (rectangle != null)
            {
                float rectWidth = transform.scale.x * Screen.unitSize;
                float rectHeight = transform.scale.y * Screen.unitSize;

                rectangle.FillColor = SFML.Graphics.Color.White;
                rectangle.Origin = new Vector2f(rectWidth / 2f, rectHeight / 2f);
                rectangle.Position = new Vector2f((SFMLRenderer.window.Size.X / 2) + transform.pos.x * newUnitSize, (SFMLRenderer.window.Size.Y / 2) - transform.pos.y * newUnitSize);
                rectangle.Size = new Vector2f(transform.scale.x * newUnitSize, transform.scale.y * newUnitSize);

                window.Draw(rectangle);
            }

            if (sprite != null)
            {

                sprite.Origin = new Vector2f(texture.Size.X / 2f, texture.Size.Y / 2f);
                sprite.Scale = new Vector2f(newUnitSize / texture.Size.X * transform.scale.x, newUnitSize / texture.Size.Y * transform.scale.y);
                sprite.Position = new Vector2f((SFMLRenderer.window.Size.X / 2) + transform.pos.x * newUnitSize, (SFMLRenderer.window.Size.Y / 2) - transform.pos.y * newUnitSize);

                window.Draw(sprite);
            }

        }

    }
}


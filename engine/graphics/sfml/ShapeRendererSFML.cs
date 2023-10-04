using SFML.Graphics;
using SFML.System;

namespace SpotEngine.Internal.Graphics.SFML
{
    /// <summary>
    /// This class is used to communicate with the SFML
    /// and render sprites or simple shapes.
    /// </summary>
    public class ShapeRendererSFML
    {
        RectangleShape rectangle;
        public bool initialized = false;
        Texture texture;
        Sprite sprite;
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
                rectangle = new RectangleShape(new Vector2f(transform.scale.x * Spot.unitSize, transform.scale.y * Spot.unitSize));

            }
            else
            {
                texture = new Texture(spritePath);
                sprite = new Sprite(texture);
                originalSpriteSize = new Vector2f(texture.Size.X, texture.Size.Y);
                sprite.Origin = originalSpriteSize / 2.0f;

            }


            initialized = true;
        }
        public void Render(RenderWindow window)
        {
            float scaleX = (float)SFMLRenderer.window.Size.X / originalWindowSize.X;
            float scaleY = (float)SFMLRenderer.window.Size.Y / originalWindowSize.Y;

            float newUnitSize = System.Math.Min(Spot.unitSize * scaleX, Spot.unitSize * scaleY);

            if (rectangle != null)
            {
                float rectX = (originalWindowSize.X / 2) + newUnitSize * transform.pos.x * (originalWindowSize.X / SFMLRenderer.window.Size.X);
                float rectY = (originalWindowSize.Y / 2) - newUnitSize * transform.pos.y * (originalWindowSize.Y / SFMLRenderer.window.Size.Y);
                float rectWidth = transform.scale.x * Spot.unitSize;
                float rectHeight = transform.scale.y * Spot.unitSize;

                rectangle.FillColor = Color.White;
                rectangle.Origin = new Vector2f(rectWidth / 2f, rectHeight / 2f);
                rectangle.Position = new Vector2f(rectX, rectY);
                rectangle.Size = new Vector2f(rectWidth, rectHeight);

                window.Draw(rectangle);
            }

            if (sprite != null)
            {

                sprite.Origin = new Vector2f(texture.Size.X / 2f, texture.Size.Y / 2f);

                sprite.Scale = new Vector2f((transform.scale.x / originalSpriteSize.X) * (originalWindowSize.X / SFMLRenderer.window.Size.X) * Spot.unitSize,
                    (transform.scale.y / originalSpriteSize.Y) * (originalWindowSize.Y / SFMLRenderer.window.Size.Y) * Spot.unitSize);

                sprite.Position = new Vector2f((originalWindowSize.X / 2) + newUnitSize * (transform.pos.x * (originalWindowSize.X / SFMLRenderer.window.Size.X)),
                    (originalWindowSize.Y / 2) - newUnitSize * (transform.pos.y * (originalWindowSize.Y / SFMLRenderer.window.Size.Y))
                );

                window.Draw(sprite);
            }

        }

    }
}


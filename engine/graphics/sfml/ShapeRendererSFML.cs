using SFML.Graphics;
using SFML.Graphics.Glsl;
using SFML.System;
using SpotEngine;

namespace SpotEngine.Graphics.SFML;

public class ShapeRendererSFML
{
    RectangleShape rectangle;
    public bool initialized = false;
    Texture texture;
    Sprite sprite;
    public void Init(Transform transform, string spritePath)
    {
        if (initialized) return;

        if (spritePath == null || spritePath == string.Empty || spritePath == "")
        {
            rectangle = new RectangleShape(new Vector2f(transform.scale.x * Spot.unitSize, transform.scale.y * Spot.unitSize));

            rectangle.FillColor = Color.White;
            rectangle.Origin = new Vector2f(rectangle.Size.X / 2f, rectangle.Size.Y / 2f);
            rectangle.Position = new Vector2f((SFMLRenderer.window.Size.X /2) + Spot.unitSize * transform.pos.x, (SFMLRenderer.window.Size.Y /2) - Spot.unitSize * transform.pos.y);

        }
        else
        {
            texture = new Texture(spritePath);
            sprite = new Sprite(texture);

            sprite.Scale = new Vector2f((transform.scale.x / texture.Size.X) * Spot.unitSize, (transform.scale.y / texture.Size.Y) * Spot.unitSize);
            sprite.Position = new Vector2f((SFMLRenderer.window.Size.X / 2) + transform.pos.x, (SFMLRenderer.window.Size.Y / 2) + transform.pos.y);
            sprite.Origin = new Vector2f(texture.Size.X / 2f, texture.Size.Y / 2f);
        }

       

        initialized = true;
    }
    public void Render(RenderWindow window)
    {
        if(rectangle != null) { window.Draw(rectangle); }
        if(sprite != null) { window.Draw(sprite);}

    }

}


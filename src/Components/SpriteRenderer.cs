using SpotEngine.Internal.Graphics;

namespace SpotEngine
{
    public class SpriteRenderer : Component
    {
        public SpriteRenderer()
        {
            Sprite = new Sprite();
        }

        public Sprite Sprite;

        internal void Render(float dt)
        {
            Graphics.DrawSquare(this);
        }
    }
}

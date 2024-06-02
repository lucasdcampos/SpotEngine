using SpotEngine.Internal.Graphics;

namespace SpotEngine
{
    public class SpriteRenderer : Component
    {
        public Sprite sprite;

        public SpriteRenderer()
        {
            sprite = new Sprite();
        }

        public SpriteRenderer(Sprite sprite)
        {
            this.sprite = sprite;
        }

        public override void OnStart()
        {
            Console.WriteLine("OnStart() not yet implemented in SpriteRenderer.cs");

            //window?.game.RegisterSprite(this, sprite);
        }
    }
}

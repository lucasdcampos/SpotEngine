using SpotEngine.Internal.Graphics.SFML;

namespace SpotEngine
{
    /// <summary>
    /// SpriteRenderer is used to display a Sprite on the screen.
    /// </summary>
    public class SpriteRenderer : Controller
    {
        public string sprite = "";
        ShapeRendererSFML shapeRendererSFML = new ShapeRendererSFML();
        public override void Init()
        {
        }

        public override void Flow()
        {
            if (Spot.renderMode == RenderMode.Default || Spot.renderMode == RenderMode.SFML)
            {
                SFMLHandle();
            }
        }

        void SFMLHandle()
        {
            shapeRendererSFML.Init(entity.transform, sprite);
            shapeRendererSFML.Render(SFMLRenderer.window);
        }
    }
}



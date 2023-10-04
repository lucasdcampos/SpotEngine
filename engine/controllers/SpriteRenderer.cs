using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SpotEngine;
using SpotEngine.Graphics.SFML;

namespace SpotEngine
{
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



using SpotEngine.Graphics.Renderer.XNA;
using SpotEngine.Internal.Graphics;
using Microsoft.Xna.Framework;
namespace SpotEngine
{
    public class SpriteRenderer : Component
    {
        public Sprite sprite;
        public override void OnStart()
        {
            print("teste");
           if(sprite == null)
            {
                MonoWindow mono = (MonoWindow)app.GetWindow();

                SquareManagerXNA squareManager = mono.game.GetSquareManager();

                squareManager.CreateSquare(
                    new Vector2(entity.transform.pos.X, entity.transform.pos.Y),
                    new Vector2(entity.transform.scale.X, entity.transform.scale.Y),
                    Color.White
                    );
            }
        }

        public override void OnUpdate()
        {

        }
    }
}

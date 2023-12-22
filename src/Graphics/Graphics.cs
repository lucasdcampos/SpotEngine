using SpotEngine;

namespace SpotEngine.Internal.Graphics
{
    /// <summary>
    /// Low level Graphics API
    /// </summary>
    public class Graphics
    {
        public static void DrawSquare(Sprite sprite, float posx, float posy, float scalex, float scaley)
        {
            MonoWindow? window = (MonoWindow)Application.GetApp().GetWindow();

            if (window != null)
            {
                window.game.RegisterSprite(sprite);
            }
        }
    }
}

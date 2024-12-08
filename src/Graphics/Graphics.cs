namespace SpotEngine.Internal.Graphics
{
    public static class Graphics
    {
        public static void DrawSquare(SpriteRenderer spriteRenderer)
        {
            Transform transform = spriteRenderer.transform;

            Application.Renderer.DrawQuad(transform, spriteRenderer.Sprite.color);
        }
    }
}

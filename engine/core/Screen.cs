namespace SpotEngine
{
    /// <summary>
    /// Screen is the Desktop Window in which
    /// the game is running.
    /// </summary>
    public static class Screen
    {
        /// <summary>
        /// Text is the Window's title.
        /// </summary>
        public static string text = "Spot Engine";

        public static Vec2i res = new Vec2i(800,600);

        public static bool fullscreen;

        /// <summary>
        /// It's the scale of everything in the Screen in pixels.
        /// If you have an Sprite with size 1, so he'll have 1 * unitSize
        /// pixels. The default value is 50px.
        /// </summary>
        public static int unitSize = 50;

        public static int realUnitSize;

        public static void updateUnitSize(int scaleX, int scaleY)
        {
             realUnitSize = System.Math.Min(unitSize * scaleX, unitSize * scaleY);

        }


    }
}

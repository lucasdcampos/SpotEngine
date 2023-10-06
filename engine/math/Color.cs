namespace SpotEngine
{
    /// <summary>
    /// Every Color has a Red, Green, Blue values
    /// varying between 0 and 255.
    /// </summary>
    public class Color
    {
        public int r, g, b, a;

        public Color(int r, int g, int b, int a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        // defining common colors
        public static Color Transparent = new Color (0, 0, 0, 0);
        public static Color Black = new Color(0, 0, 0, 255);
        public static Color White = new Color(255, 255, 255, 255);
        public static Color Red = new Color(255, 0, 0, 255);
        public static Color Green = new Color(0, 255, 0, 255);
        public static Color Blue = new Color(0, 0, 255, 255);

    }
}

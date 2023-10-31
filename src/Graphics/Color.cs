namespace SpotEngine
{
    public class Color
    {
        public int r, g, b, a;

        public static Color White = new Color(1f, 1f, 1f, 1f);
        public static Color Black = new Color(0f, 0f, 0f, 1f);
        public static Color Red = new Color(1f, 0f, 0f, 1f);
        public static Color Green = new Color(0f, 1f, 0f, 1f);
        public static Color Blue = new Color(0f, 0f, 1f, 1f);

        public Color()
        {
            r = g = b = a = 255;
        }

        public Color(Vec4 rgba)
            : this((int)rgba.X, (int)rgba.Y, (int)rgba.Z, (int)rgba.W)
        {
        }

        public Color(int r, int g, int b, int a)
        {
            this.r = Math.Clamp(r, 0, 255);
            this.g = Math.Clamp(g, 0, 255);
            this.b = Math.Clamp(b, 0, 255);
            this.a = Math.Clamp(a, 0, 255);
        }

        public Color(float r, float g, float b, float a)
            : this((int)(r * 255), (int)(g * 255), (int)(b * 255), (int)(a * 255))
        {
        }
    }
}

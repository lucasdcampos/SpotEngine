namespace SpotEngine
{
    public class Color
    {
        public int R { get; private set; }
        public int G { get; private set; }
        public int B { get; private set; }
        public int A { get; private set; }

        public float Rf => R / 255;
        public float Gf => G / 255;
        public float Bf => B / 255;
        public float Af => A / 255;

        public static Color White = new Color(1f, 1f, 1f, 1f);
        public static Color Black = new Color(0f, 0f, 0f, 1f);
        public static Color Red = new Color(1f, 0f, 0f, 1f);
        public static Color Green = new Color(0f, 1f, 0f, 1f);
        public static Color Blue = new Color(0f, 0f, 1f, 1f);

        public Color()
        {
            R = G = B = A = 255;
        }

        public Color(Vec4 rgba)
            : this((int)rgba.X, (int)rgba.Y, (int)rgba.Z, (int)rgba.W)
        {
        }

        public Color(int r, int g, int b, int a)
        {
            this.R = Math.Clamp(r, 0, 255);
            this.G = Math.Clamp(g, 0, 255);
            this.B = Math.Clamp(b, 0, 255);
            this.A = Math.Clamp(a, 0, 255);
        }

        public Color(float r, float g, float b, float a)
            : this((int)(r * 255), (int)(g * 255), (int)(b * 255), (int)(a * 255))
        {
        }
    }
}

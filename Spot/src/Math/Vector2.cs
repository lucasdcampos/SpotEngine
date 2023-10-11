namespace SpotEngine
{
    public class Vector2
    {
        public float X { get; set; }
        public float Y { get; set; }

        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float Length()
        {
            return (float)Math.Sqrt(X * X + Y * Y);
        }

        public static Vector2 Normalize(Vector2 vector)
        {
            float length = vector.Length();
            if (length > 0)
            {
                return new Vector2(vector.X / length, vector.Y / length);
            }
            return Vector2.Zero;
        }

        public static Vector2 Zero => new Vector2(0, 0);
    }
}
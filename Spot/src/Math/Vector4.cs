namespace SpotEngine
{
    public class Vector4
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }

        public Vector4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public float Length()
        {
            return (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
        }

        public static Vector4 Normalize(Vector4 vector)
        {
            float length = vector.Length();
            if (length > 0)
            {
                return new Vector4(vector.X / length, vector.Y / length, vector.Z / length, vector.W / length);
            }
            return Vector4.Zero;
        }

        public static Vector4 Zero => new Vector4(0, 0, 0, 0);
    }
}
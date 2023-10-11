namespace SpotEngine
{
    public class Vector3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float Length()
        {
            return (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        }

        public static Vector3 Normalize(Vector3 vector)
        {
            float length = vector.Length();
            if (length > 0)
            {
                return new Vector3(vector.X / length, vector.Y / length, vector.Z / length);
            }
            return Vector3.Zero;
        }

        public static Vector3 Zero => new Vector3(0, 0, 0);
    }
}
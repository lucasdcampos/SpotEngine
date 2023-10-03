namespace SpotEngine.Math
{
    public struct Vec2
    {
        public float x;
        public float y;

        public Vec2(float initialX, float initialY)
        {
            x = initialX; y = initialY;
        }

        public static Vec2 Zero = new Vec2(0f, 0f);
        public static Vec2 One = new Vec2(1f, 1f);

        public static Vec2 Up = new Vec2(0f, 1f);
        public static Vec2 Right = new Vec2(1f, 0f);
        public static Vec2 Down = new Vec2(0f, -1f);
        public static Vec2 Left = new Vec2(-1f, 0f);
        
    }

    public struct Vec3
    {
        public float x;
        public float y;
        public float z;

        public Vec3(float initialX, float initialY, float initialZ)
        {
            x = initialX;
            y = initialY;
            z = initialZ;
        }

        public static Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static Vec3 One = new Vec3(1f, 1f, 1f);

        public static Vec3 Up = new Vec3(0f, 1f, 0f);
        public static Vec3 Right = new Vec3(1f, 0f, 0f);
        public static Vec3 Forward = new Vec3(0f, 0f, 1f);

        
    }

}
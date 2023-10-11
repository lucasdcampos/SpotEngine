using System;

namespace SpotEngine
{
    public class MathUtils
    {
        public static float DegreesToRadians(float degrees)
        {
            return (float)(degrees * Math.PI / 180.0);
        }

        public static float RadiansToDegrees(float radians)
        {
            return (float)(radians * 180.0 / Math.PI);
        }
    }
}



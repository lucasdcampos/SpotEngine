namespace SpotEngine
{
    /// <summary>
    /// A utility class that provides common mathematical functions.
    /// </summary>
    public class MathUtils
    {
        /// <summary>
        /// Converts degrees to radians.
        /// </summary>
        /// <param name="degrees">The angle in degrees.</param>
        /// <returns>The equivalent angle in radians.</returns>
        public static float DegreesToRadians(float degrees)
        {
            return (float)(degrees * Math.PI / 180.0);
        }

        /// <summary>
        /// Converts radians to degrees.
        /// </summary>
        /// <param name="radians">The angle in radians.</param>
        /// <returns>The equivalent angle in degrees.</returns>
        public static float RadiansToDegrees(float radians)
        {
            return (float)(radians * 180.0 / Math.PI);
        }
    }
}


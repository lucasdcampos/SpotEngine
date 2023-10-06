namespace SpotEngine
{
    /// <summary>
    /// Will generate Random values.
    /// </summary>
    public class Rand
    {
        static Random rnd = new Random();
        /// <summary>
        /// Will generate a random Integer between two values.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public static float Between(float x, float y)
        {
            return (float)rnd.NextDouble() * (x - y);
        }
    }
}

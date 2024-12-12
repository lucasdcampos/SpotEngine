namespace SpotEngine
{
    public class Rand
    {
        /// <summary>
        /// Returns a random float between min and max
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public static float Between(float min, float max)
        {
            Random _rand = new Random();
            double num = _rand.NextDouble() * (max - min) + min;

            return (float)num;
        }
    }
}
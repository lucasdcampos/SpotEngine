namespace SpotEngine
{
    /// <summary>
    /// Time is relative as some may say.
    /// </summary>
    public class Time
    {
        /// <summary>
        /// Will control the speed of the game.
        /// If you set it to 0.5f, the game will
        /// be played half of the speed.
        /// </summary>
        public static float timeSpeed = 1f;

        static float realDeltaTime;

        /// <summary>
        /// Returns the amount of time
        /// since the last frame.
        /// </summary>
        public static float deltaTime()
        {
            return realDeltaTime * timeSpeed;
        }

        public static void CalculateDeltaTime()
        {
            realDeltaTime = 0;
        }

        /// <summary>
        /// Will just update the value of the
        /// deltaTime variable.
        /// </summary>
        public static void UpdateDeltaTime(float delta)
        {
            realDeltaTime = delta;
        }

    }
}

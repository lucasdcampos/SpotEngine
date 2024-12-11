namespace SpotEngine
{
    /// <summary>
    /// A static class that manages time-related properties and calculations.
    /// </summary>
    public static class Time
    {
        /// <summary>
        /// A timestamp value that can be used for measuring time or for custom time-related calculations.
        /// Default value is 1.
        /// </summary>
        public static float Timestamp = 1;

        /// <summary>
        /// Gets or sets the time that has passed since the last frame (delta time).
        /// This value is updated each frame and is used for frame-rate independent operations.
        /// </summary>
        public static float DeltaTime { get; internal set; }
    }

}
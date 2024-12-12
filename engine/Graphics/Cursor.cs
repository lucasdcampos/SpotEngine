namespace SpotEngine
{
    /// <summary>
    /// A static class that manages the state of the cursor.
    /// </summary>
    public static class Cursor
    {
        /// <summary>
        /// Gets or sets a value indicating whether the cursor is visible.
        /// </summary>
        public static bool Visible { get; set; } = true;

        /// <summary>
        /// Gets or sets the current state of the cursor.
        /// </summary>
        public static CursorState State { get; set; } = CursorState.None;
    }

    /// <summary>
    /// Defines the possible states of the cursor.
    /// </summary>
    public enum CursorState
    {
        /// <summary>
        /// The cursor is in the default state (not locked or hidden).
        /// </summary>
        None,

        /// <summary>
        /// The cursor is locked to a specific position.
        /// </summary>
        Locked
    }


}

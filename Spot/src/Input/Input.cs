// Copyright (c) Trivalent Studios
// Licensed under MIT License.

namespace SpotEngine
{
    /// <summary>
    /// A static class for managing input events such as keyboard and mouse input.
    /// </summary>
    public static class Input
    {
        static bool m_Initialized = false;

        static Dictionary<KeyCode, bool> KeyStates = new Dictionary<KeyCode, bool>();
        static Dictionary<MouseButton, bool> MouseStates = new Dictionary<MouseButton, bool>();

        /// <summary>
        /// Initializes the input system and subscribes to keyboard input events.
        /// </summary>
        public static void Init()
        {
            if (m_Initialized) return;
            Event.KeyboardPressedOccurred += (sender, e) => { KeyStates[e.Key] = true; };
            Event.KeyboardReleasedOcurred += (sender, e) => { KeyStates[e.Key] = false; };

            // setting keys to false by default
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                KeyStates[key] = false;
            }
        }

        /// <summary>
        /// Checks if a specific keyboard key is currently pressed.
        /// </summary>
        /// <param name="keyCode">The keyboard key to check.</param>
        /// <returns>True if the key is pressed; otherwise, false.</returns>
        public static bool IsKeyPressed(KeyCode keyCode)
        {
            return KeyStates[keyCode];
        }

        /// <summary>
        /// Checks if a specific mouse button is currently pressed.
        /// </summary>
        /// <param name="mouseButton">The mouse button to check.</param>
        /// <returns>True if the mouse button is pressed; otherwise, false.</returns>
        public static bool IsMouseButtonPressed(MouseButton mouseButton)
        {
            return MouseStates[mouseButton];
        }
    }
}

// Copyright (c) Spot Engine
// Licensed under MIT License.
using SpotEngine.Events;

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

        public static float MousePositionX {  get; private set; }
        public static float MousePositionY { get; private set; }
        public static float MouseDeltaX { get; private set; }
        public static float MouseDeltaY { get; private set; }

        private static float m_timeSinceLastMouseMovement;

        /// <summary>
        /// Initializes the input system and subscribes to keyboard input events.
        /// </summary>
        public static void Init()
        {
            if (m_Initialized) { Log.Warn("Trying to initialize Input when it's already initialized!"); return; }
            Event.KeyboardPressedOccurred += (sender, e) => { KeyStates[e.Key] = true; };
            Event.KeyboardReleasedOccurred += (sender, e) => { KeyStates[e.Key] = false; };
            

            SetupMouseEvents();

            // setting keys to false by default
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                KeyStates[key] = false;
            }

            foreach (MouseButton mb in Enum.GetValues(typeof(MouseButton)))
            {
                MouseStates[mb] = false;
            }

            m_Initialized = true;
        }

        private static void SetupMouseEvents()
        {
            Event.MouseButtonPressedOccurred += (sender, e) => { MouseStates[e.Button] = true; };
            Event.MouseButtonReleasedOccurred += (sender, e) => { MouseStates[e.Button] = false; };

            Event.MouseMovedOccurred += (sender, e) => 
            {
                m_timeSinceLastMouseMovement = 0;
                MousePositionX = e.X; MousePositionY = e.Y;
                MouseDeltaX = e.DeltaX; MouseDeltaY = e.DeltaY;
            };
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

        public static bool IsKeyPressed(int keyCode)
        {
            return KeyStates[(KeyCode)keyCode];
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

        static internal void SetKeyState(int code, bool state)
        {
            KeyCode keyCode = (KeyCode)code;
            KeyStates[keyCode] = state;
        }

        internal static void Update(float dt)
        {
            m_timeSinceLastMouseMovement += dt;
            if (m_timeSinceLastMouseMovement > 0.01f)
            {
                MouseDeltaX = 0f;
                MouseDeltaY = 0f;
            }
        }
    }
}
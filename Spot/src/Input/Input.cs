// Copyright (c) Trivalent Studios
// Licensed under MIT License.

namespace SpotEngine
{
    public static class Input
    {
        static bool m_Initialized = false;

        static Dictionary<KeyCode, bool> KeyStates = new Dictionary<KeyCode, bool>();
        static Dictionary<MouseButton, bool> MouseStates = new Dictionary<MouseButton, bool>();

        public static void Init()
        {
            if(m_Initialized) return;
            Event.KeyboardPressedOccurred += (sender, e) => { KeyStates[e.Key] = true; };
            Event.KeyboardReleasedOcurred += (sender, e) => { KeyStates[e.Key] = false; };

            // setting keys to false by default
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                KeyStates[key] = false;
            }

        }

        public static bool IsKeyPressed(KeyCode keyCode)
        {
           return KeyStates[keyCode];
        }


        public static bool IsMouseButtonPressed(MouseButton mouseButton)
        {
            return MouseStates[mouseButton];
        }
    }
}

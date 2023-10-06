namespace SpotEngine
{
    /// <summary>
    /// Is used to detect Input.
    /// It can be a keyboard being pressed,
    /// a joystick button, or an custom action.
    /// </summary>
    public static class Input
    {
        public static Dictionary<string, bool> keyStates = new Dictionary<string, bool>();
        public static List<InputAction> actions = new List<InputAction>();
        public static void Init()
        {
            // Alfabet keys
            keyStates["A"] = false;
            keyStates["B"] = false;
            keyStates["C"] = false;
            keyStates["D"] = false;
            keyStates["E"] = false;
            keyStates["F"] = false;
            keyStates["G"] = false;
            keyStates["H"] = false;
            keyStates["I"] = false;
            keyStates["J"] = false;
            keyStates["K"] = false;
            keyStates["L"] = false;
            keyStates["M"] = false;
            keyStates["N"] = false;
            keyStates["O"] = false;
            keyStates["P"] = false;
            keyStates["Q"] = false;
            keyStates["R"] = false;
            keyStates["S"] = false;
            keyStates["T"] = false;
            keyStates["U"] = false;
            keyStates["V"] = false;
            keyStates["W"] = false;
            keyStates["X"] = false;
            keyStates["Y"] = false;
            keyStates["Z"] = false;

            // Num Keys
            keyStates["1"] = false;
            keyStates["2"] = false;
            keyStates["3"] = false;
            keyStates["4"] = false;
            keyStates["5"] = false;
            keyStates["6"] = false;
            keyStates["7"] = false;
            keyStates["8"] = false;
            keyStates["9"] = false;
            keyStates["0"] = false;

            // Numpad Keys
            keyStates["Num1"] = false;
            keyStates["Num2"] = false;
            keyStates["Num3"] = false;
            keyStates["Num4"] = false;
            keyStates["Num5"] = false;
            keyStates["Num6"] = false;
            keyStates["Num7"] = false;
            keyStates["Num8"] = false;
            keyStates["Num9"] = false;
            keyStates["Num0"] = false;

            // Teclas de função
            keyStates["F1"] = false;
            keyStates["F2"] = false;
            keyStates["F3"] = false;
            keyStates["F4"] = false;
            keyStates["F5"] = false;
            keyStates["F6"] = false;
            keyStates["F7"] = false;
            keyStates["F8"] = false;
            keyStates["F9"] = false;
            keyStates["F10"] = false;
            keyStates["F11"] = false;
            keyStates["F12"] = false;

            // Teclas de controle
            keyStates["Escape"] = false;
            keyStates["LControl"] = false;
            keyStates["RControl"] = false;
            keyStates["LShift"] = false;
            keyStates["RShift"] = false;
            keyStates["Caps"] = false;
            keyStates["Tab"] = false;


            // Teclas especiais
            keyStates["Return"] = false;
            keyStates["Backspace"] = false;
            keyStates["Space"] = false;
            keyStates["UpArrow"] = false;
            keyStates["RightArrow"] = false;
            keyStates["DownArrow"] = false;
            keyStates["LeftArrow"] = false;

        }

        public static void UpdateKeyState(string keyCode, bool isPressed)
        {
            if (keyStates.ContainsKey(keyCode))
            {
                keyStates[keyCode] = isPressed;
            }
        }

        public static bool IsKeyPressed(string keyCode)
        {
            if (keyStates.ContainsKey(keyCode))
            {
                return keyStates[keyCode];
            }

            return false;
        }

        public static bool IsActionTriggering(InputAction action)
        {
            foreach (string k in action.keys)
            {
                if (IsKeyPressed(k))
                    return true;
            }

            return false;
        }
        public static void PrintKeyStates()
        {
            foreach (var kvp in keyStates)
            {
                Console.WriteLine(kvp.Key + ": " + kvp.Value);
            }
        }
    }
}

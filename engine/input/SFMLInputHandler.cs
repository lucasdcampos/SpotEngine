using SFML.Graphics;
using SFML.Window;
using SpotEngine;

namespace SpotEngine.Internal.Input
{
    
    internal class SFMLInputHandler
    {
        
        public SFMLInputHandler(RenderWindow window) 
        {
            window.KeyPressed += OnKeyPressed;
            window.KeyReleased += OnKeyReleased;

            void OnKeyPressed(object sender, KeyEventArgs e)
            {
                switch (e.Code)
                {
                    case Keyboard.Key.A: SpotEngine.Input.UpdateKeyState("A", true); break;
                    case Keyboard.Key.B: SpotEngine.Input.UpdateKeyState("B", true); break;
                    case Keyboard.Key.C: SpotEngine.Input.UpdateKeyState("C", true); break;
                    case Keyboard.Key.D: SpotEngine.Input.UpdateKeyState("D", true); break;
                    case Keyboard.Key.E: SpotEngine.Input.UpdateKeyState("E", true); break;
                    case Keyboard.Key.F: SpotEngine.Input.UpdateKeyState("F", true); break;
                    case Keyboard.Key.G: SpotEngine.Input.UpdateKeyState("G", true); break;
                    case Keyboard.Key.H: SpotEngine.Input.UpdateKeyState("H", true); break;
                    case Keyboard.Key.I: SpotEngine.Input.UpdateKeyState("I", true); break;
                    case Keyboard.Key.J: SpotEngine.Input.UpdateKeyState("J", true); break;
                    case Keyboard.Key.K: SpotEngine.Input.UpdateKeyState("K", true); break;
                    case Keyboard.Key.L: SpotEngine.Input.UpdateKeyState("L", true); break;
                    case Keyboard.Key.M: SpotEngine.Input.UpdateKeyState("M", true); break;
                    case Keyboard.Key.N: SpotEngine.Input.UpdateKeyState("N", true); break;
                    case Keyboard.Key.O: SpotEngine.Input.UpdateKeyState("O", true); break;
                    case Keyboard.Key.P: SpotEngine.Input.UpdateKeyState("P", true); break;
                    case Keyboard.Key.Q: SpotEngine.Input.UpdateKeyState("Q", true); break;
                    case Keyboard.Key.R: SpotEngine.Input.UpdateKeyState("R", true); break;
                    case Keyboard.Key.S: SpotEngine.Input.UpdateKeyState("S", true); break;
                    case Keyboard.Key.T: SpotEngine.Input.UpdateKeyState("T", true); break;
                    case Keyboard.Key.U: SpotEngine.Input.UpdateKeyState("U", true); break;
                    case Keyboard.Key.V: SpotEngine.Input.UpdateKeyState("V", true); break;
                    case Keyboard.Key.W: SpotEngine.Input.UpdateKeyState("W", true); break;
                    case Keyboard.Key.X: SpotEngine.Input.UpdateKeyState("X", true); break;
                    case Keyboard.Key.Y: SpotEngine.Input.UpdateKeyState("Y", true); break;
                    case Keyboard.Key.Z: SpotEngine.Input.UpdateKeyState("Z", true); break;

                    case Keyboard.Key.Num1: SpotEngine.Input.UpdateKeyState("1", true); break;
                    case Keyboard.Key.Num2: SpotEngine.Input.UpdateKeyState("2", true); break;
                    case Keyboard.Key.Num3: SpotEngine.Input.UpdateKeyState("3", true); break;
                    case Keyboard.Key.Num4: SpotEngine.Input.UpdateKeyState("4", true); break;
                    case Keyboard.Key.Num5: SpotEngine.Input.UpdateKeyState("5", true); break;
                    case Keyboard.Key.Num6: SpotEngine.Input.UpdateKeyState("6", true); break;
                    case Keyboard.Key.Num7: SpotEngine.Input.UpdateKeyState("7", true); break;
                    case Keyboard.Key.Num8: SpotEngine.Input.UpdateKeyState("8", true); break;
                    case Keyboard.Key.Num9: SpotEngine.Input.UpdateKeyState("9", true); break;
                    case Keyboard.Key.Num0: SpotEngine.Input.UpdateKeyState("0", true); break;

                    case Keyboard.Key.F1: SpotEngine.Input.UpdateKeyState("F1", true); break;
                    case Keyboard.Key.F2: SpotEngine.Input.UpdateKeyState("F2", true); break;
                    case Keyboard.Key.F3: SpotEngine.Input.UpdateKeyState("F3", true); break;
                    case Keyboard.Key.F4: SpotEngine.Input.UpdateKeyState("F4", true); break;
                    case Keyboard.Key.F5: SpotEngine.Input.UpdateKeyState("F5", true); break;
                    case Keyboard.Key.F6: SpotEngine.Input.UpdateKeyState("F6", true); break;
                    case Keyboard.Key.F7: SpotEngine.Input.UpdateKeyState("F7", true); break;
                    case Keyboard.Key.F8: SpotEngine.Input.UpdateKeyState("F8", true); break;
                    case Keyboard.Key.F9: SpotEngine.Input.UpdateKeyState("F9", true); break;
                    case Keyboard.Key.F10: SpotEngine.Input.UpdateKeyState("F10", true); break;
                    case Keyboard.Key.F11: SpotEngine.Input.UpdateKeyState("F11", true); break;
                    case Keyboard.Key.F12: SpotEngine.Input.UpdateKeyState("F12", true); break;

                    case Keyboard.Key.Escape: SpotEngine.Input.UpdateKeyState("Escape", true); break;
                    case Keyboard.Key.LControl: SpotEngine.Input.UpdateKeyState("LControl", true); break;
                    case Keyboard.Key.RControl: SpotEngine.Input.UpdateKeyState("RControl", true); break;
                    case Keyboard.Key.LShift: SpotEngine.Input.UpdateKeyState("LShift", true); break;
                    case Keyboard.Key.RShift: SpotEngine.Input.UpdateKeyState("RShift", true); break;
                    //case Keyboard.Key.CapsLock: SpotEngine.Input.UpdateKeyState("CapsLock", true); break;
                    case Keyboard.Key.Tab: SpotEngine.Input.UpdateKeyState("Tab", true); break;

                    case Keyboard.Key.Return: SpotEngine.Input.UpdateKeyState("Return", true); break;
                    case Keyboard.Key.BackSpace: SpotEngine.Input.UpdateKeyState("BackSpace", true); break;
                    case Keyboard.Key.Space: SpotEngine.Input.UpdateKeyState("Space", true); break;
                    case Keyboard.Key.Up: SpotEngine.Input.UpdateKeyState("UpArrow", true); break;
                    case Keyboard.Key.Right: SpotEngine.Input.UpdateKeyState("RightArrow", true); break;
                    case Keyboard.Key.Down: SpotEngine.Input.UpdateKeyState("DownArrow", true); break;
                    case Keyboard.Key.Left: SpotEngine.Input.UpdateKeyState("LeftArrow", true); break;

                    default: break;
                }
            }

            void OnKeyReleased(object sender, KeyEventArgs e)
            {
                switch (e.Code)
                {
                    case Keyboard.Key.A: SpotEngine.Input.UpdateKeyState("A", false); break;
                    case Keyboard.Key.B: SpotEngine.Input.UpdateKeyState("B", false); break;
                    case Keyboard.Key.C: SpotEngine.Input.UpdateKeyState("C", false); break;
                    case Keyboard.Key.D: SpotEngine.Input.UpdateKeyState("D", false); break;
                    case Keyboard.Key.E: SpotEngine.Input.UpdateKeyState("E", false); break;
                    case Keyboard.Key.F: SpotEngine.Input.UpdateKeyState("F", false); break;
                    case Keyboard.Key.G: SpotEngine.Input.UpdateKeyState("G", false); break;
                    case Keyboard.Key.H: SpotEngine.Input.UpdateKeyState("H", false); break;
                    case Keyboard.Key.I: SpotEngine.Input.UpdateKeyState("I", false); break;
                    case Keyboard.Key.J: SpotEngine.Input.UpdateKeyState("J", false); break;
                    case Keyboard.Key.K: SpotEngine.Input.UpdateKeyState("K", false); break;
                    case Keyboard.Key.L: SpotEngine.Input.UpdateKeyState("L", false); break;
                    case Keyboard.Key.M: SpotEngine.Input.UpdateKeyState("M", false); break;
                    case Keyboard.Key.N: SpotEngine.Input.UpdateKeyState("N", false); break;
                    case Keyboard.Key.O: SpotEngine.Input.UpdateKeyState("O", false); break;
                    case Keyboard.Key.P: SpotEngine.Input.UpdateKeyState("P", false); break;
                    case Keyboard.Key.Q: SpotEngine.Input.UpdateKeyState("Q", false); break;
                    case Keyboard.Key.R: SpotEngine.Input.UpdateKeyState("R", false); break;
                    case Keyboard.Key.S: SpotEngine.Input.UpdateKeyState("S", false); break;
                    case Keyboard.Key.T: SpotEngine.Input.UpdateKeyState("T", false); break;
                    case Keyboard.Key.U: SpotEngine.Input.UpdateKeyState("U", false); break;
                    case Keyboard.Key.V: SpotEngine.Input.UpdateKeyState("V", false); break;
                    case Keyboard.Key.W: SpotEngine.Input.UpdateKeyState("W", false); break;
                    case Keyboard.Key.X: SpotEngine.Input.UpdateKeyState("X", false); break;
                    case Keyboard.Key.Y: SpotEngine.Input.UpdateKeyState("Y", false); break;
                    case Keyboard.Key.Z: SpotEngine.Input.UpdateKeyState("Z", false); break;

                    case Keyboard.Key.Num1: SpotEngine.Input.UpdateKeyState("1", false); break;
                    case Keyboard.Key.Num2: SpotEngine.Input.UpdateKeyState("2", false); break;
                    case Keyboard.Key.Num3: SpotEngine.Input.UpdateKeyState("3", false); break;
                    case Keyboard.Key.Num4: SpotEngine.Input.UpdateKeyState("4", false); break;
                    case Keyboard.Key.Num5: SpotEngine.Input.UpdateKeyState("5", false); break;
                    case Keyboard.Key.Num6: SpotEngine.Input.UpdateKeyState("6", false); break;
                    case Keyboard.Key.Num7: SpotEngine.Input.UpdateKeyState("7", false); break;
                    case Keyboard.Key.Num8: SpotEngine.Input.UpdateKeyState("8", false); break;
                    case Keyboard.Key.Num9: SpotEngine.Input.UpdateKeyState("9", false); break;
                    case Keyboard.Key.Num0: SpotEngine.Input.UpdateKeyState("0", false); break;

                    case Keyboard.Key.F1: SpotEngine.Input.UpdateKeyState("F1", false); break;
                    case Keyboard.Key.F2: SpotEngine.Input.UpdateKeyState("F2", false); break;
                    case Keyboard.Key.F3: SpotEngine.Input.UpdateKeyState("F3", false); break;
                    case Keyboard.Key.F4: SpotEngine.Input.UpdateKeyState("F4", false); break;
                    case Keyboard.Key.F5: SpotEngine.Input.UpdateKeyState("F5", false); break;
                    case Keyboard.Key.F6: SpotEngine.Input.UpdateKeyState("F6", false); break;
                    case Keyboard.Key.F7: SpotEngine.Input.UpdateKeyState("F7", false); break;
                    case Keyboard.Key.F8: SpotEngine.Input.UpdateKeyState("F8", false); break;
                    case Keyboard.Key.F9: SpotEngine.Input.UpdateKeyState("F9", false); break;
                    case Keyboard.Key.F10: SpotEngine.Input.UpdateKeyState("F10", false); break;
                    case Keyboard.Key.F11: SpotEngine.Input.UpdateKeyState("F11", false); break;
                    case Keyboard.Key.F12: SpotEngine.Input.UpdateKeyState("F12", false); break;

                    case Keyboard.Key.Escape: SpotEngine.Input.UpdateKeyState("Escape", false); break;
                    case Keyboard.Key.LControl: SpotEngine.Input.UpdateKeyState("LControl", false); break;
                    case Keyboard.Key.RControl: SpotEngine.Input.UpdateKeyState("RControl", false); break;
                    case Keyboard.Key.LShift: SpotEngine.Input.UpdateKeyState("LShift", false); break;
                    case Keyboard.Key.RShift: SpotEngine.Input.UpdateKeyState("RShift", false); break;
                    //case Keyboard.Key.CapsLock: SpotEngine.Input.UpdateKeyState("CapsLock", false); break;
                    case Keyboard.Key.Tab: SpotEngine.Input.UpdateKeyState("Tab", false); break;

                    case Keyboard.Key.Return: SpotEngine.Input.UpdateKeyState("Return", false); break;
                    case Keyboard.Key.BackSpace: SpotEngine.Input.UpdateKeyState("BackSpace", false); break;
                    case Keyboard.Key.Space: SpotEngine.Input.UpdateKeyState("Space", false); break;
                    case Keyboard.Key.Up: SpotEngine.Input.UpdateKeyState("UpArrow", false); break;
                    case Keyboard.Key.Right: SpotEngine.Input.UpdateKeyState("RightArrow", false); break;
                    case Keyboard.Key.Down: SpotEngine.Input.UpdateKeyState("DownArrow", false); break;
                    case Keyboard.Key.Left: SpotEngine.Input.UpdateKeyState("LeftArrow", false); break;

                    default: break;
                }
            }
        }


    }
}

using Microsoft.Xna.Framework.Input;

namespace SpotEngine.Internal.Utils
{
    internal class XnaKeysToSpot
    {
        // dumbest way possible, sorry (Xna keys values are different from OpenTK =/)
        internal static KeyCode Convert(Keys key)
        {
            switch (key)
            {
                case Keys.Back:
                    return KeyCode.Backspace;
                case Keys.Tab:
                    return KeyCode.Tab;
                case Keys.Enter:
                    return KeyCode.Enter;
                case Keys.CapsLock:
                    return KeyCode.CapsLock;
                case Keys.Escape:
                    return KeyCode.Escape;
                case Keys.Space:
                    return KeyCode.Space;
                case Keys.PageUp:
                    return KeyCode.PageUp;
                case Keys.PageDown:
                    return KeyCode.PageDown;
                case Keys.End:
                    return KeyCode.End;
                case Keys.Home:
                    return KeyCode.Home;
                case Keys.Left:
                    return KeyCode.Left;
                case Keys.Up:
                    return KeyCode.Up;
                case Keys.Right:
                    return KeyCode.Right;
                case Keys.Down:
                    return KeyCode.Down;
                case Keys.Select:
                    return KeyCode.Unknown;
                case Keys.Print:
                    return KeyCode.Unknown;
                case Keys.Execute:
                    return KeyCode.Unknown;
                case Keys.PrintScreen:
                    return KeyCode.PrintScreen;
                case Keys.Insert:
                    return KeyCode.Insert;
                case Keys.Delete:
                    return KeyCode.Delete;
                case Keys.Help:
                    return KeyCode.Unknown;
                case Keys.D0:
                    return KeyCode.D0;
                case Keys.D1:
                    break;
                case Keys.D2:
                    break;
                case Keys.D3:
                    break;
                case Keys.D4:
                    break;
                case Keys.D5:
                    break;
                case Keys.D6:
                    break;
                case Keys.D7:
                    break;
                case Keys.D8:
                    break;
                case Keys.D9:
                    break;
                case Keys.A:
                    break;
                case Keys.B:
                    break;
                case Keys.C:
                    break;
                case Keys.D:
                    break;
                case Keys.E:
                    break;
                case Keys.F:
                    break;
                case Keys.G:
                    break;
                case Keys.H:
                    break;
                case Keys.I:
                    break;
                case Keys.J:
                    break;
                case Keys.K:
                    break;
                case Keys.L:
                    break;
                case Keys.M:
                    break;
                case Keys.N:
                    break;
                case Keys.O:
                    break;
                case Keys.P:
                    break;
                case Keys.Q:
                    break;
                case Keys.R:
                    break;
                case Keys.S:
                    break;
                case Keys.T:
                    break;
                case Keys.U:
                    break;
                case Keys.V:
                    break;
                case Keys.W:
                    break;
                case Keys.X:
                    break;
                case Keys.Y:
                    break;
                case Keys.Z:
                    break;
                case Keys.LeftWindows:
                    break;
                case Keys.RightWindows:
                    break;
                case Keys.Apps:
                    break;
                case Keys.Sleep:
                    break;
                case Keys.NumPad0:
                    break;
                case Keys.NumPad1:
                    break;
                case Keys.NumPad2:
                    break;
                case Keys.NumPad3:
                    break;
                case Keys.NumPad4:
                    break;
                case Keys.NumPad5:
                    break;
                case Keys.NumPad6:
                    break;
                case Keys.NumPad7:
                    break;
                case Keys.NumPad8:
                    break;
                case Keys.NumPad9:
                    break;
                case Keys.Multiply:
                    break;
                case Keys.Add:
                    break;
                case Keys.Separator:
                    break;
                case Keys.Subtract:
                    break;
                case Keys.Decimal:
                    break;
                case Keys.Divide:
                    break;
                case Keys.F1:
                    break;
                case Keys.F2:
                    break;
                case Keys.F3:
                    break;
                case Keys.F4:
                    break;
                case Keys.F5:
                    break;
                case Keys.F6:
                    break;
                case Keys.F7:
                    break;
                case Keys.F8:
                    break;
                case Keys.F9:
                    break;
                case Keys.F10:
                    break;
                case Keys.F11:
                    break;
                case Keys.F12:
                    break;
                case Keys.F13:
                    break;
                case Keys.F14:
                    break;
                case Keys.F15:
                    break;
                case Keys.F16:
                    break;
                case Keys.F17:
                    break;
                case Keys.F18:
                    break;
                case Keys.F19:
                    break;
                case Keys.F20:
                    break;
                case Keys.F21:
                    break;
                case Keys.F22:
                    break;
                case Keys.F23:
                    break;
                case Keys.F24:
                    break;
                case Keys.NumLock:
                    break;
                case Keys.Scroll:
                    break;
                case Keys.LeftShift:
                    break;
                case Keys.RightShift:
                    break;
                case Keys.LeftControl:
                    break;
                case Keys.RightControl:
                    break;
                case Keys.LeftAlt:
                    break;
                case Keys.RightAlt:
                    break;
                case Keys.BrowserBack:
                    break;
                case Keys.BrowserForward:
                    break;
                case Keys.BrowserRefresh:
                    break;
                case Keys.BrowserStop:
                    break;
                case Keys.BrowserSearch:
                    break;
                case Keys.BrowserFavorites:
                    break;
                case Keys.BrowserHome:
                    break;
                case Keys.VolumeMute:
                    break;
                case Keys.VolumeDown:
                    break;
                case Keys.VolumeUp:
                    break;
                case Keys.MediaNextTrack:
                    break;
                case Keys.MediaPreviousTrack:
                    break;
                case Keys.MediaStop:
                    break;
                case Keys.MediaPlayPause:
                    break;
                case Keys.LaunchMail:
                    break;
                case Keys.SelectMedia:
                    break;
                case Keys.LaunchApplication1:
                    break;
                case Keys.LaunchApplication2:
                    break;
                case Keys.OemSemicolon:
                    break;
                case Keys.OemPlus:
                    break;
                case Keys.OemComma:
                    break;
                case Keys.OemMinus:
                    break;
                case Keys.OemPeriod:
                    break;
                case Keys.OemQuestion:
                    break;
                case Keys.OemTilde:
                    break;
                case Keys.OemOpenBrackets:
                    break;
                case Keys.OemPipe:
                    break;
                case Keys.OemCloseBrackets:
                    break;
                case Keys.OemQuotes:
                    break;
                case Keys.Oem8:
                    break;
                case Keys.OemBackslash:
                    break;
                case Keys.ProcessKey:
                    break;
                case Keys.Attn:
                    break;
                case Keys.Crsel:
                    break;
                case Keys.Exsel:
                    break;
                case Keys.EraseEof:
                    break;
                case Keys.Play:
                    break;
                case Keys.Zoom:
                    break;
                case Keys.Pa1:
                    break;
                case Keys.OemClear:
                    break;
                case Keys.ChatPadGreen:
                    break;
                case Keys.ChatPadOrange:
                    break;
                case Keys.Pause:
                    break;
                default:
                    return KeyCode.Unknown;
            }
            return KeyCode.Unknown;
        }
    }
}

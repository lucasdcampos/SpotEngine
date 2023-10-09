// Copyright (c) Trivalent Studios
// Licensed under MIT License.

namespace SpotEngine
{
    public class InputEvents
    {
        public class KeyboardPressEvent
        {
            public char KeyChar { get; }

            public KeyboardPressEvent(char keyChar)
            {
                KeyChar = keyChar;
            }
        }
        public class KeyboardReleaseEvent
        {
            public char KeyChar { get; }

            public KeyboardReleaseEvent(char keyChar)
            {
                KeyChar = keyChar;
            }
        }

        public class MouseButtonPressEvent
        {
            public int Button { get; }

            public MouseButtonPressEvent(int button)
            {
                Button = button;
            }
        }

        public class MouseButtonReleaseEvent
        {
            public int Button { get; }

            public MouseButtonReleaseEvent(int button)
            {
                Button = button;
            }
        }

        public class MouseMoveEvent
        {
            public int X { get; }
            public int Y { get; }

            public MouseMoveEvent(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        public class MouseScrollEvent
        {
            public int Delta { get; }

            public MouseScrollEvent(int delta)
            {
                Delta = delta;
            }
        }
    }
}

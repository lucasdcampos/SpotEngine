// Copyright (c) Trivalent Studios
// Licensed under MIT License.

namespace SpotEngine
{
    public class InputEvents
    {
        public class KeyboardPressEvent
        {
            public KeyCode Key { get; }

            public KeyboardPressEvent(KeyCode keyCode)
            {
                Key = keyCode;
            }
        }
        public class KeyboardReleaseEvent
        {
            public KeyCode Key { get; }

            public KeyboardReleaseEvent(KeyCode keyCode)
            {
                Key = keyCode;
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

// Copyright (c) Spot Engine
// Licensed under MIT License.

namespace SpotEngine.Events
{
    /// <summary>
    /// Represents an event for a keyboard key press.
    /// </summary>
    public class KeyboardPressEvent
    {
        /// <summary>
        /// Gets the KeyCode of the pressed key.
        /// </summary>
        public KeyCode Key { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyboardPressEvent"/> class with the specified KeyCode.
        /// </summary>
        /// <param name="keyCode">The KeyCode of the pressed key.</param>
        public KeyboardPressEvent(KeyCode keyCode)
        {
            Key = keyCode;
        }
    }

    /// <summary>
    /// Represents an event for a keyboard key release.
    /// </summary>
    public class KeyboardReleaseEvent
    {
        /// <summary>
        /// Gets the KeyCode of the released key.
        /// </summary>
        public KeyCode Key { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyboardReleaseEvent"/> class with the specified KeyCode.
        /// </summary>
        /// <param name="keyCode">The KeyCode of the released key.</param>
        public KeyboardReleaseEvent(KeyCode keyCode)
        {
            Key = keyCode;
        }
    }

    /// <summary>
    /// Represents an event for a mouse button press.
    /// </summary>
    public class MouseButtonPressEvent
    {
        /// <summary>
        /// Gets the ID of the pressed mouse button.
        /// </summary>
        public MouseButton Button { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MouseButtonPressEvent"/> class with the specified button ID.
        /// </summary>
        /// <param name="button">The ID of the pressed mouse button.</param>
        public MouseButtonPressEvent(MouseButton button)
        {
            Button = button;
        }
    }

    /// <summary>
    /// Represents an event for a mouse button release.
    /// </summary>
    public class MouseButtonReleaseEvent
    {
        /// <summary>
        /// Gets the ID of the released mouse button.
        /// </summary>
        public MouseButton Button { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MouseButtonReleaseEvent"/> class with the specified button ID.
        /// </summary>
        /// <param name="button">The ID of the released mouse button.</param>
        public MouseButtonReleaseEvent(MouseButton button)
        {
            Button = button;
        }
    }

    /// <summary>
    /// Represents an event for mouse movement.
    /// </summary>
    public class MouseMoveEvent
    {
        /// <summary>
        /// Gets the X coordinate of the mouse.
        /// </summary>
        public float X { get; }

        /// <summary>
        /// Gets the Y coordinate of the mouse.
        /// </summary>
        public float Y { get; }

        public float DeltaX { get; }

        public float DeltaY { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MouseMoveEvent"/> class with the specified X and Y coordinates.
        /// </summary>
        /// <param name="x">The X coordinate of the mouse.</param>
        /// <param name="y">The Y coordinate of the mouse.</param>
        public MouseMoveEvent(float x, float y, float deltax, float deltay)
        {
            X = x;
            Y = y;
            DeltaX = deltax;
            DeltaY = deltay;
        }
    }

    /// <summary>
    /// Represents an event for mouse scrolling.
    /// </summary>
    public class MouseScrollEvent
    {
        /// <summary>
        /// Gets the delta value of the mouse scroll.
        /// </summary>
        public int Delta { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MouseScrollEvent"/> class with the specified delta value.
        /// </summary>
        /// <param name="delta">The delta value of the mouse scroll.</param>
        public MouseScrollEvent(int delta)
        {
            Delta = delta;
        }
    }
}


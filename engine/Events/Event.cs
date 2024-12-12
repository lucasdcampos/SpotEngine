namespace SpotEngine.Events
{
    /// <summary>
    /// A class for managing and triggering events related to input and window events.
    /// </summary>
    public class Event
    {
        /// <summary>
        /// Represents the types of events that can be handled.
        /// </summary>
        public enum EventType
        {
            WindowResized, WindowMaximized, WindowMinimized, WindowClosed, WindowMoved,
            KeyboardPressed, KeyboardReleased, MouseButtonPressed, MouseButtonReleased,
            MouseMoved, MouseScrolled,
        }

        // Declaring a generic delegate for event handlers
        public delegate void EventHandler<T>(object sender, T eData);

        // General event for any event type
        public static event EventHandler<object>? EventOccurred;

        // Specific events
        public static event EventHandler<WindowResizeEvent>? WindowResizedOccurred;
        public static event EventHandler<WindowCloseEvent>? WindowClosedOccurred;
        public static event EventHandler<KeyboardPressEvent>? KeyboardPressedOccurred;
        public static event EventHandler<KeyboardReleaseEvent>? KeyboardReleasedOccurred;
        public static event EventHandler<MouseButtonPressEvent>? MouseButtonPressedOccurred;
        public static event EventHandler<MouseButtonReleaseEvent>? MouseButtonReleasedOccurred;
        public static event EventHandler<MouseMoveEvent>? MouseMovedOccurred;
        public static event EventHandler<MouseScrollEvent>? MouseScrolledOccurred;

        // Generalized method for triggering events
        private static void TriggerEvent<T>(EventType eventType, EventHandler<T> specificEvent, object sender, T eData)
        {
            specificEvent?.Invoke(sender, eData);
            EventOccurred?.Invoke(sender, eData);
        }

        // Trigger specific events
        public static void TriggerWindowResizedEvent(object sender, WindowResizeEvent e)
            => TriggerEvent(EventType.WindowResized, WindowResizedOccurred, sender, e);

        public static void TriggerWindowCloseEvent(object sender, WindowCloseEvent e)
            => TriggerEvent(EventType.WindowClosed, WindowClosedOccurred, sender, e);

        public static void TriggerKeyboardPressedEvent(object sender, KeyboardPressEvent e)
            => TriggerEvent(EventType.KeyboardPressed, KeyboardPressedOccurred, sender, e);

        public static void TriggerKeyboardReleasedEvent(object sender, KeyboardReleaseEvent e)
            => TriggerEvent(EventType.KeyboardReleased, KeyboardReleasedOccurred, sender, e);

        public static void TriggerMouseButtonPressedEvent(object sender, MouseButtonPressEvent e)
            => TriggerEvent(EventType.MouseButtonPressed, MouseButtonPressedOccurred, sender, e);

        public static void TriggerMouseButtonReleasedEvent(object sender, MouseButtonReleaseEvent e)
            => TriggerEvent(EventType.MouseButtonReleased, MouseButtonReleasedOccurred, sender, e);

        public static void TriggerMouseMoveEvent(object sender, MouseMoveEvent e)
            => TriggerEvent(EventType.MouseMoved, MouseMovedOccurred, sender, e);

        public static void TriggerMouseScrollEvent(object sender, MouseScrollEvent e)
            => TriggerEvent(EventType.MouseScrolled, MouseScrolledOccurred, sender, e);
    }
}

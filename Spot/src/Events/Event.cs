﻿// Copyright (c) Trivalent Studios
// Licensed under MIT License.

namespace SpotEngine
{
    public class Event
    {
        public enum EventType
        {
            WindowResized, WindowMaximized, WindowMinimized, WindowClosed, WindowMoved,
            KeyboardPressed, KeyboardReleased, MouseButtonPressed, MouseButtonReleased,
            MouseMoved, MouseScrolled,
        }

        // declaring a generic delegate to every event type
        public delegate void EventHandler(EventType e, object eData);
        public delegate void WindowResizedEventHandler(object sender, WindowEvents.WindowResizeEvent e);
        public delegate void KeyboardPressEventHandler(object sender, InputEvents.KeyboardPressEvent e);
        public delegate void MouseButtonPressEventHandler(object sender, InputEvents.MouseButtonPressEvent e);
        public delegate void MouseButtonReleaseEventHandler(object sender, InputEvents.MouseButtonReleaseEvent e);
        public delegate void MouseMoveEventHandler(object sender, InputEvents.MouseMoveEvent e);
        public delegate void MouseScrollEventHandler(object sender, InputEvents.MouseScrollEvent e);

        // declaring generic event
        public static event EventHandler? EventOccurred;

        // declaring specific events
        public static event WindowResizedEventHandler? WindowResizedOccurred;
        public static event KeyboardPressEventHandler? KeyboardPressedOccurred;
        public static event MouseButtonPressEventHandler? MouseButtonPressedOccurred;
        public static event MouseButtonReleaseEventHandler? MouseButtonReleasedOccurred;
        public static event MouseMoveEventHandler? MouseMovedOccurred;
        public static event MouseScrollEventHandler? MouseScrolledOccurred;

        // creating triggers to every event type
        public static void TriggerWindowResizedEvent(object sender, WindowEvents.WindowResizeEvent e)
        {
            WindowResizedOccurred?.Invoke(sender, e);
            EventOccurred?.Invoke(EventType.WindowResized, e);
        }

        public static void TriggerKeyboardPressedEvent(object sender, InputEvents.KeyboardPressEvent e)
        {
            KeyboardPressedOccurred?.Invoke(sender, e);
            EventOccurred?.Invoke(EventType.KeyboardPressed, e);
        }

        public static void TriggerMouseButtonPressedEvent(object sender, InputEvents.MouseButtonPressEvent e)
        {
            MouseButtonPressedOccurred?.Invoke(sender, e);
            EventOccurred?.Invoke(EventType.MouseButtonPressed, e);
        }

        public static void TriggerMouseButtonReleasedEvent(object sender, InputEvents.MouseButtonReleaseEvent e)
        {
            MouseButtonReleasedOccurred?.Invoke(sender, e);
            EventOccurred?.Invoke(EventType.MouseButtonReleased, e);
        }

        public static void TriggerMouseMoveEvent(object sender, InputEvents.MouseMoveEvent e)
        {
            MouseMovedOccurred?.Invoke(sender, e);
            EventOccurred?.Invoke(EventType.MouseMoved, e);
        }

        public static void TriggerMouseScrollEvent(object sender, InputEvents.MouseScrollEvent e)
        {
            MouseScrolledOccurred?.Invoke(sender, e);
            EventOccurred?.Invoke(EventType.MouseScrolled, e);
        }
    }
}
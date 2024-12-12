// Copyright (c) Spot Engine
// Licensed under MIT License.

namespace SpotEngine.Events
{
    public class WindowResizeEvent
    {
        public int Width { get; }
        public int Height { get; }

        public WindowResizeEvent(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    public class WindowCloseEvent
    {

    }

}


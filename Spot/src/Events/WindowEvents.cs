// Copyright (c) Trivalent Studios
// Licensed under MIT License.

namespace SpotEngine
{
    public class WindowEvents
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

    }
}

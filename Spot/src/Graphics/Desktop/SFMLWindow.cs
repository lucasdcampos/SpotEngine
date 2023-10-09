// Copyright (c) Trivalent Studios
// Licensed under MIT License.

using SFML.Graphics;
using SFML.Window;

namespace SpotEngine.Internal.Rendering
{
    internal class SFMLWindow : Window
    {
        RenderWindow window;

        public SFMLWindow(string title, int width, int height) : base(title, width, height)
        {
            window = new RenderWindow(new VideoMode((uint)width, (uint)height), title);

            Initialize();
        }

        public override void Initialize()
        {
            window.Closed += (sender, e) =>
            {
                Close();
            };

            Log.Message($"Creating SFML Window '{Title}' ({Width}x{Height})");
            Render();
        }

        public override void Close()
        {
            Log.Message($"Closing SFML Window '{Title}' ({Width}x{Height})");
            window.Close();
            return;
        }
        public override void Render()
        {
            Log.Message("Starting SFML Game Loop");
            while (window.IsOpen)
            {     
                window.DispatchEvents();

                window.Clear(Color.Magenta);

                window.Display();
            }
        }

        public override void Update()
        {
        }


    }


}

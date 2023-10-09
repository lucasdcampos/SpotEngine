// Copyright (c) Trivalent Studios
// Licensed under MIT License.

using SpotEngine.Internal.Rendering;
using System;

namespace SpotEngine
{
    public class Application
    {
        private bool run;

        public int Run()
        {
            run = true;

            Log.Message("Initializing Application");
            Window win = new SFMLWindow("My Game", 800,600);

            while (run)
            {
                // do stuff
            }

            return 0;
        }

        Application CreateApplication()
        {
            return new Application();
        }
    }

}
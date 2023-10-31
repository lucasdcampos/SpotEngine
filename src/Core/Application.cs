using SpotEngine.Internal.Graphics;
using System.Xml.Linq;

namespace SpotEngine
{
    /// <summary>
    /// Represents the main application for the Spot Engine.
    /// </summary>
    public class Application
    {
        /// <summary>
        /// Will display additional dev information when set to true
        /// </summary>
        public static bool debugMode = false;

        private static Application? s_Instance;
        private Window? m_Window;
        private bool m_Running;

        /// <summary>
        /// Gets the singleton instance of the Application.
        /// </summary>
        /// <returns>The singleton instance of the Application.</returns>
        public static Application GetApp()
        {
            if (s_Instance == null)
            {
                s_Instance = new Application();
            }
            return s_Instance;
        }

        private Application()
        {
            if (s_Instance != null)
            {
                string err = "An instance of Application already exists.";
                Log.Error(err);
                throw new Exception(err);
            }

            m_Running = false;
        }

        /// <summary>
        /// Will return the current Window
        /// </summary>
        /// <returns>Window or null</returns>
        public Window? GetWindow()
        {
            if (m_Window == null)
                Log.Error("Window is null");
            return m_Window;
        }

        public void CreateWindow(RenderMode mode, string title, Vec2 res)
        {
            if (m_Window != null) { Log.Warn("A window is already in use!"); return; }

            switch (mode)
            {
                case RenderMode.Xna:
                    m_Window = new MonoWindow(title, (int)res.X, (int)res.Y);
                    break;
                case RenderMode.OpenTK:
                    Log.Warn("OpenTK is not supported yet! Creating a Xna window instead");
                    m_Window = new MonoWindow(title, (int)res.X, (int)res.Y);
                    break;
                default:
                    m_Window = new MonoWindow(title, (int)res.X, (int)res.Y);
                    break;
            }

            
        }

        /// <summary>
        /// Runs the application and initializes the main loop.
        /// </summary>
        /// <returns>Zero on successful completion.</returns>
        public int Run()
        {
            Log.Info("Spot Engine says Hello!");
            // Handling the Close Event
            Event.WindowClosedEventOcurred += (sender, e) => { Stop(); };
            m_Running = true;
            Log.Info("Initializing Application");
            Input.Init();

            if (m_Window == null)
            {
                CreateWindow(RenderMode.Xna, "Spot Game", new Vec2(800, 600));
            }

            m_Window.Initialize();

            return 0;
        }

        /// <summary>
        /// Stops the application and exits the main loop.
        /// </summary>
        public void Stop()
        {
            Log.Info("Stopping the application");
            m_Running = false;
        }

        public enum RenderMode { Xna, OpenTK }
        
    }
}

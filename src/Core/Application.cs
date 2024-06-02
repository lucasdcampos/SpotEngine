using SpotEngine.Internal.Graphics;

namespace SpotEngine
{
    /// <summary>
    /// Represents the main application for the Spot Engine Runtime.
    /// </summary>
    public class Application
    {
        /// <summary>
        /// Will display additional dev information when set to true
        /// </summary>
        public static bool debugMode = false;

        public static string applicationName;
        public static string companyName;
        public static bool isEditor;
        public static bool isPlaying;

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

        public void CreateWindow(string title, Vec2 res)
        {
            if (m_Window != null) { Log.Warn("A window is already in use!"); return; }

           m_Window = new OpenGLWindow(title, (int)res.X, (int)res.Y);
            
        }

        /// <summary>
        /// Runs the application and initializes the main loop.
        /// </summary>
        /// <returns>Zero on successful completion.</returns>
        public int Run()
        {
            Log.Info("Hello from Spot Engine");
            // Handling the Close Event
            Event.WindowClosedEventOcurred += (sender, e) => { Stop(); };
            m_Running = true;
            Log.Info("Initializing Application");
            Input.Init();

            if (m_Window == null)
            {
                CreateWindow("Spot Game", new Vec2(800, 600));
            }

            m_Window?.Initialize();

            while (m_Running)
            {
                if (m_Window == null)
                    return 0;

                m_Window!.Update();
                m_Window!.Render();
            }

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
        
    }
}
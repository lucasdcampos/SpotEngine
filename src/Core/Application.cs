using SpotEngine.Internal.Graphics;
using SpotEngine.Internal.Renderer;
using System.Diagnostics;
using System.Diagnostics.Metrics;

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

        public int DesiredFramerate { get; set; } = 60;
        public float UpdateTime { get; private set; } 

        private static Application? s_Instance;
        private Window? m_Window;
        private bool m_Running;

        private Stopwatch m_stopwatch;

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
            m_Running = true;

            Event.WindowClosedEventOcurred += (sender, e) => { Stop(); };

            Input.Init();

            if (m_Window == null)
            {
                CreateWindow("Spot Game", new Vec2(800, 600));
            }

            Renderer2D.Init();

            m_Window?.Initialize();
            
            m_stopwatch = Stopwatch.StartNew();
            var lastFrameTime = TimeSpan.Zero;

            var freq = 1 / DesiredFramerate * 1000;

            while (m_Running)
            {
                if (m_Window == null)
                    return 0;

                TimeSpan currentTime = m_stopwatch.Elapsed;
                TimeSpan deltaTime = currentTime - lastFrameTime;
                lastFrameTime = currentTime;

                var dt = (float)deltaTime.TotalSeconds;
                System.Threading.Thread.Sleep(freq);

                m_Window!.Update(dt);
                m_Window!.Render(dt);
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
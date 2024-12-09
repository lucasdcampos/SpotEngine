using SpotEngine.Internal.Graphics;
using SpotEngine.Rendering;
using System.Diagnostics;

namespace SpotEngine
{
    /// <summary>
    /// Represents the main application for the Spot Engine Runtime.
    /// </summary>
    public class Application
    {
        public static string ApplicationName => s_instance.m_applicationName;
        public static string companyName => s_instance.m_companyName;
        public static bool IsEditor => s_instance.m_isEditor;
        public static bool IsPlaying => s_instance.m_isPlaying;
        public static Scene ActiveScene => s_instance.m_activeScene;
        internal static Renderer Renderer => s_instance.m_renderer;
        internal static Window Window => s_instance.m_window;
        /// <summary>
        /// Will display additional dev information when set to true
        /// </summary>
        public static bool DebugMode = false;
        public int DesiredFramerate { get; private set; } = 60;

        private float m_updateTime;

        private string m_applicationName;
        private string m_companyName;
        private bool m_isEditor;
        private bool m_isPlaying;
        private Scene m_activeScene;

        private static Application s_instance;
        private Window m_window;
        private Renderer m_renderer;
        private bool m_running;

        private Stopwatch m_stopwatch;

        /// <summary>
        /// Gets the singleton instance of the Application.
        /// </summary>
        /// <returns>The singleton instance of the Application.</returns>
        public static Application GetApp()
        {
            if (s_instance == null)
            {
                s_instance = new Application();
            }
            return s_instance;
        }

        private Application()
        {
            if (s_instance != null)
            {
                string err = "An instance of Application already exists.";
                Log.Error(err);
                throw new Exception(err);
            }

            m_running = false;
        }

        /// <summary>
        /// Runs the application and initializes the main loop.
        /// </summary>
        /// <returns>Zero on successful completion.</returns>
        public int Run()
        {
            m_stopwatch = Stopwatch.StartNew();
            var lastFrameTime = TimeSpan.Zero;

            var freq = 1 / DesiredFramerate * 1000;

            Init();

            while (m_running)
            {
                if (m_window == null)
                    return 0;

                TimeSpan currentTime = m_stopwatch.Elapsed;
                TimeSpan deltaTime = currentTime - lastFrameTime;
                lastFrameTime = currentTime;

                var dt = (float)deltaTime.TotalSeconds;
                Thread.Sleep(freq);
                Time.deltaTime = dt;
                Update(dt);
                Render(dt);

            }

            return 0;
        }

        public void ChanceScene(Scene scene)
        {
            m_activeScene = scene;
        }

        /// <summary>
        /// Will return the current Window
        /// </summary>
        /// <returns>Window or null</returns>
        public Window? GetWindow()
        {
            if (m_window == null)
                Log.Error("Window is null");
            return m_window;
        }

        public void CreateWindow(string title, Vec2 res)
        {
            if (m_window != null) { Log.Warn("A window is already in use!"); return; }

            bool useCompatibilityMode = true;
           m_window = new OpenGLWindow(title, (int)res.X, (int)res.Y, useCompatibilityMode);
            m_window.BackgroundColor = Color.Black;
            
        }

        public void SetDesiredFramerate(int framerate)
        {
            if(framerate <= 0)
            {
                Log.Error("DesiredFramerate should be greater than 0");
                Stop();
            }

            DesiredFramerate = framerate;
        }

        private void Init()
        {
            Log.Info("Hello from Spot Engine");
            m_running = true;

            Event.WindowClosedEventOcurred += (sender, e) => { Stop(); };

            Input.Init();

            if (m_window == null)
            {
                CreateWindow("Spot Game", new Vec2(800, 600));
            }

            m_window?.Initialize();

            m_renderer = new Renderer(((OpenGLWindow)m_window).Context); // TODO: We shouldn't convert here

            if(m_activeScene == null)
                m_activeScene = new Scene();

            m_activeScene.Start();
        }

        protected virtual void Update(float dt)
        {
            m_activeScene.Update(dt);
            m_window!.Update(dt);
        }
        protected virtual void Render(float dt)
        {
            m_window!.Render(dt);
        }


        /// <summary>
        /// Stops the application and exits the main loop.
        /// </summary>
        public void Stop()
        {
            Log.Info("Stopping the application");
            m_running = false;
        }
        
    }
}
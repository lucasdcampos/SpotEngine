using SpotEngine.Internal.Graphics;
using SpotEngine.Internal.Rendering;

namespace SpotEngine
{
    public class Application
    {
        private static Application? s_Instance;

        private Window? m_Window;
        private bool m_Running;

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

        public int Run()
        {

            Log.Message("Spot Engine says Hello!");
            // Handling the Close Event
            Event.WindowClosedEventOcurred += (sender, e) => { Stop(); };
            m_Running = true;
            Log.Message("Initializing Application");
            Input.Init();
            m_Window = m_Window == null ? new OpenGLWindow("My Game", 800, 600) : m_Window;

            // main loop
            while (m_Running)
            {
                
            }

            return 0;
        }

        public void Stop()
        {
            Log.Message("Stopping the application");
            m_Running = false;
        }
    }
}

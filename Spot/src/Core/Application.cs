using SpotEngine.Internal.Rendering;
using System;

namespace SpotEngine
{
    public class Application
    {
        private Window m_Window;
        private bool m_Run;

        // Criar uma instância única da classe Application
        private static Application s_Instance;

        public static Application GetInstance()
        {
            if (s_Instance == null)
            {
                s_Instance = new Application();
            }
            return s_Instance;
        }

        private Application()
        {
            // Impedir a criação de instâncias adicionais
            if (s_Instance != null)
            {
                throw new Exception("An instance of Application already exists.");
            }

            m_Run = false;
        }

        public int Run()
        {
            m_Run = true;
            Log.Message("Initializing Application");
            m_Window = new SFMLWindow("My Game", 800, 600);

            while (m_Run)
            {
                m_Window.Render();
            }

            return 0;
        }

        public void Stop()
        {
            Log.Message("Stopping the application");
            m_Run = false;
        }
    }
}

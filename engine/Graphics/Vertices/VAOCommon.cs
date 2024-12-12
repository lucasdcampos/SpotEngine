namespace SpotEngine.Rendering
{
    internal static class VAOCommon
    {
        private static float[] m_triangleVertices;
        private static float[] m_squareVertices;
        private static VAOManager m_vaoManager;
        private static ShaderManager m_shaderManager;

        internal static void Initialize(VAOManager vaoManager, ShaderManager shaderManager)
        {
            m_vaoManager = vaoManager;
            m_shaderManager = shaderManager;

            InitializeTriangle();
            InitializeSquare();
            InitializeLine();
        }

        private static void InitializeTriangle()
        {
            m_triangleVertices = new float[]
            {
                -0.5f, -0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    0.0f, 0.0f,
                 0.5f, -0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    1.0f, 0.0f,
                 0.0f,  0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    0.5f, 1.0f
            };

            m_vaoManager.LoadVAO("triangle", m_triangleVertices, 8 * sizeof(float),
                (0, 3, 0),                  // Vertices (pos)
                (1, 3, 3 * sizeof(float)),  // Color
                (2, 2, 6 * sizeof(float))   // Texture
            );
        }

        private static void InitializeSquare()
        {
            m_squareVertices = new float[]
            {
                -0.5f, -0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    0.0f, 0.0f,
                 0.5f, -0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    1.0f, 0.0f,
                 0.5f,  0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    1.0f, 1.0f,
                -0.5f,  0.5f, 0.0f,    1.0f, 1.0f, 1.0f,    0.0f, 1.0f
            };

            m_vaoManager.LoadVAO("square", m_squareVertices, 8 * sizeof(float),
                (0, 3, 0),                  // Vertices (pos)
                (1, 3, 3 * sizeof(float)),  // Color
                (2, 2, 6 * sizeof(float))   // Texture
            );
        }

        private static void InitializeLine()
        {
            float[] lineVertices = new float[]
            {
                0f, 0f, 0f, 1f, 1f, 1f, 1f,
                0f, 1f, 0f, 1f, 1f, 1f, 1f
            };

            m_vaoManager.LoadVAO("line", lineVertices, 7 * sizeof(float),
                (0, 3, 0),  // Posição
                (1, 4, 3 * sizeof(float)) // Color
            );
        }
    }
}
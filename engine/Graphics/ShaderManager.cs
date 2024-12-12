namespace SpotEngine.Rendering
{
    public class ShaderManager
    {
        private Dictionary<string, Shader> m_shaders;

        public ShaderManager()
        {
            m_shaders = new Dictionary<string, Shader>();
        }

        public Shader GetShader(string name)
        {
            if (m_shaders.TryGetValue(name, out Shader shader))
            {
                return shader;
            }

            throw new KeyNotFoundException($"Shader '{name}' not found.");
        }

        public Shader LoadShader(string name, string vertexSource, string fragmentSource)
        {
            if (m_shaders.ContainsKey(name))
            {
                return m_shaders[name];
            }

            Shader shader = new Shader(vertexSource, fragmentSource);
            m_shaders[name] = shader;

            return shader;
        }

        public void DisposeAll()
        {
            foreach (var shader in m_shaders.Values)
            {
                shader.Dispose();
            }

            m_shaders.Clear();
        }
    }
}
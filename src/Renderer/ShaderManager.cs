using SpotEngine.Internal.Rendering;

namespace SpotEngine.Rendering
{
    internal class ShaderManager
    {
        private Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();

        public Shader GetShader(string name)
        {
            if (!shaders.ContainsKey(name))
            {
                shaders[name] = Shader.GetDefault();
            }

            return shaders[name];
        }
    }

}

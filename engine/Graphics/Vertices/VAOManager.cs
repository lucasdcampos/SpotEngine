namespace SpotEngine.Rendering
{
    public class VAOManager
    {
        private Dictionary<string, VAO> vaos;

        public VAOManager()
        {
            vaos = new Dictionary<string, VAO>();
        }

        public VAO GetVAO(string name)
        {
            if (vaos.TryGetValue(name, out VAO vao))
            {
                return vao;
            }

            throw new KeyNotFoundException($"VAO '{name}' not found.");
        }

        public VAO LoadVAO(string name, float[] vertices, int stride, params (int index, int size, int offset)[] attributes)
        {
            if (vaos.ContainsKey(name))
            {
                return vaos[name];
            }

            VAO vao = new VAO(vertices, stride, attributes);
            vaos[name] = vao;

            return vao;
        }

        public void DisposeAll()
        {
            foreach (var vao in vaos.Values)
            {
                vao.Dispose();
            }

            vaos.Clear();
        }
    }
}

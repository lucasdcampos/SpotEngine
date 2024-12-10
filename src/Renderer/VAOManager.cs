namespace SpotEngine.Internal.Rendering
{
    internal class VAOManager
    {
        private Dictionary<string, VAO> vaos = new Dictionary<string, VAO>();

        public VAO GetVAO(string name, float[] vertices)
        {
            if (!vaos.ContainsKey(name))
            {
                //vaos[name] = new VAO(vertices);
            }

            return vaos[name];
        }
    }
}
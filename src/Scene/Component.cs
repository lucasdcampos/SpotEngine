namespace SpotEngine
{
    public class Component
    {
        public Entity entity;
        public bool enabled = true;

        public virtual void OnStart() { }
        public virtual void OnUpdate() { }

        /// <summary>
        /// It's the same as Log.Info, will inform the specified message in console
        /// </summary>
        /// <param name="message"></param>
        public void print(string message) { Log.Info(message); }
    }
}
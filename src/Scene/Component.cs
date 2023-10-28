namespace SpotEngine
{
    public class Component
    {
        public Entity entity;
        public bool enabled = true;

        public virtual void OnStart() { }
        public virtual void OnUpdate() { }

        public void print(string message) { Log.Info(message); }
    }
}

namespace SpotEngine
{
    /// <summary>
    /// Represents a component in SpotEngine with an associated entity.
    /// </summary>
    public class Component
    {
        /// <summary>
        /// Associated entity.
        /// </summary>
        public Entity entity;

        /// <summary>
        /// Indicates if the component is enabled.
        /// </summary>
        public bool enabled = true;

        /// <summary>
        /// Called when the component starts. Can be overridden.
        /// </summary>
        public virtual void OnStart() { print("a"); }

        /// <summary>
        /// Called once every frame. Can be overridden.
        /// </summary>
        public virtual void OnUpdate() { }

        internal Application app = Application.GetApp();
        /// <summary>
        /// Logs the specified message. Equivalent to Log.Info.
        /// </summary>
        /// <param name="message">Message to log.</param>
        public void print(string message) { Log.Info(message); }
    }
}

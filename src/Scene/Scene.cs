using SpotEngine.Internal.Rendering;

namespace SpotEngine
{
    /// <summary>
    /// Represents a scene in SpotEngine with associated entities.
    /// </summary>
    public class Scene
    {
        /// <summary>
        /// Name of the scene.
        /// </summary>
        public string Name { get; protected set; }
        protected Renderer renderer => Application.Renderer;

        internal void Start()
        {
            OnStart();
        }
        internal void Update(float dt)
        {
            OnUpdate(dt);
        }

        internal void Render(float dt)
        {
            OnRender(dt);
        }

        protected virtual void OnStart() { }
        protected virtual void OnUpdate(float dt) { }
        protected virtual void OnRender(float dt) { }
    }
}

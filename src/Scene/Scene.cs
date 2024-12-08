using SpotEngine.Rendering;

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
        List<SpriteRenderer> sprites = new List<SpriteRenderer>();

        internal void Start()
        {
            OnStart();
        }

        internal void Update(float dt)
        {
            OnUpdate(dt);
        }

        public void AddActor(Actor actor)
        {
            sprites.Add(actor.SpriteRenderer);
        }

        internal void Render(float dt)
        {
            for(int i = 0; i < sprites.Count; i++)
            {
                sprites[i].Render(dt);
            }

            OnRender(dt);
        }

        protected virtual void OnStart() { }
        protected virtual void OnUpdate(float dt) { }
        protected virtual void OnRender(float dt) { }
    }
}

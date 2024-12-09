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
        public Color BackgroundColor { get; set; }
        
        public List<Entity> Entities = new List<Entity>();
        protected Renderer renderer => Application.Renderer;
        List<SpriteRenderer> sprites = new List<SpriteRenderer>();
        List<Collider2D> colliders = new List<Collider2D>();
        protected Collider2D[] _collisions;

        internal void Start()
        {
            OnStart();
        }

        internal void Update(float dt)
        {
            OnUpdate(dt);
            for(int i = 0; i < Entities.Count; i++)
            {
                Entities[i].Update(dt);
            }

            CheckCollisions();
        }

        public void AddActor(Actor actor)
        {
            Entities.Add(actor);
            sprites.Add(actor.SpriteRenderer);
            colliders.Add(actor.Collider);
        }

        public void CheckCollisions()
        {
            List<Collider2D> collisions = new List<Collider2D>();

            for (int i = 0; i < colliders.Count; i++)
            {
                for (int j = 0; j < colliders.Count; j++)
                {
                    if (i == j) break;

                    if (colliders[i].CollidesWith(colliders[j]))
                        collisions.Add(colliders[i]);
                }
            }

            _collisions = collisions.ToArray();
        }

        internal void Render(float dt)
        {
            Application.Window.BackgroundColor = BackgroundColor;

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

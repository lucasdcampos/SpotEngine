namespace SpotEngine
{
    /// <summary>
    /// Represents an entity in SpotEngine with a unique ID and associated components.
    /// </summary>
    public class Entity
    {
        /// <summary>
        /// Unique identifier.
        /// </summary>
        public readonly Guid ID = Guid.NewGuid();

        /// <summary>
        /// Name of the entity.
        /// </summary>
        public string name = "Entity";

        /// <summary>
        /// Tag associated with the entity.
        /// </summary>
        public string tag = "Default";

        /// <summary>
        /// Layer associated with the entity.
        /// </summary>
        public string layer = "Default";

        private bool m_Active;

        /// <summary>
        /// Indicates if the entity is active.
        /// </summary>
        public bool isActive { get { return m_Active; } }

        private List<Component> components = new List<Component>();

        public Transform transform;

        /// <summary>
        /// Sets the active state of the entity.
        /// </summary>
        public void SetActive(bool b)
        {
            m_Active = b;
        }

        /// <summary>
        /// Adds a component to the entity.
        /// </summary>
        public Component AddComponent(Component component)
        {
            components.Add(component);
            component.entity = this;

            return component;
        }

        /// <summary>
        /// Gets a component of a specific type from the entity.
        /// </summary>
        public T GetComponent<T>() where T : Component
        {
            foreach (Component component in components)
            {
                if (component.GetType().Equals(typeof(T)))
                {
                    return (T)component;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets all components from the entity.
        /// </summary>
        public List<Component> GetComponents()
        {
            return components;
        }

        public static Entity SpawnEntity(Entity entity)
        {
            if(entity.transform == null)
                entity.transform = (Transform)entity.AddComponent(new Transform());

            if(Scene.current != null) Scene.current.RegisterEntity(entity);
            return entity;
        }

        /// <summary>
        /// Kills the Entity, unloading it from the current scene
        /// </summary>
        public void Kill() => Scene.current.UnregisterEntity(this);
    }
}

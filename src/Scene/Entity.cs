// Copyright (c) Trivalent Studios
// Licensed under MIT License.

namespace SpotEngine
{
    public class Entity
    {
        public readonly Guid ID = Guid.NewGuid();
        public string name = "Entity";
        public string tag = "Default";
        public string layer = "Default";

        private bool m_Active;

        public bool isActive { get { return m_Active; } }

        private List<Component> components = new List<Component>();

        public void SetActive(bool b)
        {
            m_Active = b;
        }

        public void AddComponent(Component component)
        {
            components.Add(component);
            component.entity = this;
        }

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

        public List<Component> GetComponents()
        {
            return components;
        }

        Entity SpawnEntity(Entity entity)
        {
            Scene.current.RegisterEntity(entity);
            return entity;
        }
    }
}
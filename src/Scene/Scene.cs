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
        public string name;

        public List<Entity> entities = new List<Entity>();

        /// <summary>
        /// Current active scene.
        /// </summary>
        public static Scene? current;

        private static Scene protectedScene;

        /// <summary>
        /// Loads a new scene, replacing the current one.
        /// </summary>
        public static void LoadScene(Scene scene)
        {
            bool isCurrentSceneNull = current == null;
            if (scene == null) { Log.Error("Scene could not be loaded. Reason: Scene is NULL"); return; }

            // copying the scene to a new one, so the original remains intact
            Scene newScene = CloneScene(scene);
            
            if (!isCurrentSceneNull) 
            {
                Log.Info($"Loading scene {scene.name}");
                newScene.Start();
            }

            current = newScene;
        }

        /// <summary>
        /// Starts all components of all entities in the scene.
        /// </summary>
        public void Start()
        {
            // TODO: Change this to a faster way
            foreach (var entity in entities)
            {
                foreach (var component in entity.GetComponents())
                {
                    component.OnStart();
                }
            }
        }

        public static void SaveScene(Scene scene)
        {
            protectedScene = CloneScene(scene);
        }

        public static Scene CloneScene(Scene scene)
        {
            Scene newScene = new Scene();
            newScene.name = scene.name;
            newScene.entities.Clear();

            foreach (Entity e in scene.entities)
            {
                Log.Dev($"Adding {e.name} ({e.ID}) to {newScene.name}");
                Entity newEntity = new Entity();
                newEntity.SetComponents(e.GetComponents());
                newScene.entities.Add(newEntity);
            }

            return newScene;
        }

        /// <summary>
        /// Updates all components of all entities in the scene.
        /// </summary>
        public void Update()
        {
            Physics2D.CheckCollisions();
            // TODO: Change this to a faster way
            foreach (var entity in entities)
            {
                foreach (var component in entity.GetComponents())
                {
                    component.OnUpdate();
                }
            }
        }

        public static Scene GetScene()
        {
            return protectedScene;
        }

        /// <summary>
        /// Registers a new entity to the scene.
        /// </summary>
        public void RegisterEntity(Entity entity)
        {
            entities.Add(entity);
        }

        public void UnregisterEntity(Entity entity)
        {
            entities.Remove(entity);
        }

        /// <summary>
        /// Gets all entities in the scene.
        /// </summary>
        public List<Entity> GetEntities()
        {
            return entities;
        }
    }
}

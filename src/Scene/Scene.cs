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

        public List<Entity> Entities { get; private set; }

        /// <summary>
        /// Current active scene.
        /// </summary>
        public static Scene? Active { get; private set; }

        private static Scene protectedScene;

        /// <summary>
        /// Loads a new scene, replacing the current one.
        /// </summary>
        public static void LoadScene(Scene scene)
        {
            bool isCurrentSceneNull = Active == null;
            if (scene == null) { Log.Error("Scene could not be loaded. Reason: Scene is NULL"); return; }

            // copying the scene to a new one, so the original remains intact
            Scene newScene = CloneScene(scene);
            
            if (!isCurrentSceneNull) 
            {
                Log.Info($"Loading scene {scene.Name}");
                newScene.Start();
            }

            Active = newScene;
        }

        /// <summary>
        /// Starts all components of all entities in the scene.
        /// </summary>
        public void Start()
        {
            for (int i = 0; i < Entities.Count; i++)
            {
                for(int j = 0; j < Entities[i].GetComponents().Count; j++)
                {
                    Entities[i].GetComponents()[j].OnStart();
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
            newScene.Name = scene.Name;
            newScene.Entities.Clear();

            foreach (Entity e in scene.Entities)
            {
                Log.Dev($"Adding {e.name} ({e.ID}) to {newScene.Name}");
                Entity newEntity = new Entity();
                newEntity.SetComponents(e.GetComponents());
                newScene.Entities.Add(newEntity);
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
            foreach (var entity in Entities)
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
            Entities.Add(entity);
        }

        public void UnregisterEntity(Entity entity)
        {
            Entities.Remove(entity);
        }

        /// <summary>
        /// Gets all entities in the scene.
        /// </summary>
        public List<Entity> GetEntities()
        {
            return Entities;
        }
    }
}

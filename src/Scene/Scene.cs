namespace SpotEngine
{
    public class Scene
    {
        public string name;
        
        private List<Entity> entities = new List<Entity>();

        public static Scene current;

        public static void LoadScene(Scene scene)
        {
            if (scene == null) { Log.Error("Scene could not be loaded. Reason: Scene is NULL"); return; }

            Log.Info($"Loading scene {scene.name}");

            // copying the scene to a new one, so the original remains intact
            Scene newScene = new Scene();
            newScene.name = scene.name;
            newScene.entities.Clear();

            current = newScene;

            foreach (Entity e in scene.entities)
            {
                Log.Dev($"Adding {e.name} ({e.ID}) to {newScene.name}");
                newScene.entities.Add(e);
            }

            newScene.Start();
        }
        
        void Start()
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

        public void Update()
        {

            // TODO: Change this to a faster way
            foreach (var entity in entities)
            {
                foreach (var component in entity.GetComponents())
                {
                    component.OnUpdate();
                }
            }
        }

        public void RegisterEntity(Entity entity)
        {
            entities.Add(entity);
        }

        public List<Entity> GetEntities()
        {
            return entities;
        }
    }
}

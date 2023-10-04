namespace SpotEngine
{
    /// <summary>
    /// Entity is everything that exists in the game's level
    /// It can be the player, a coin, or simply the ground
    /// Every entity has a name, a tag and a transform
    /// </summary>
    public class Entity
    {
        public Transform transform { get; set; }

        public List<Controller> controllers { get; set; }

        public string name;
        public string tag;
        public bool active = true;

        public Entity() 
        {
            // Setting default values
            name = "entity";
            tag = "default";
            transform = new Transform();
            controllers = new List<Controller>();
        }

        public void Init()
        {
            foreach (Controller controller in controllers)
            {
                controller.InitializeOnce();
            }
        }

        public void Flow()
        {
            foreach (Controller controller in controllers)
            {
                controller.Flow();

                if(controller.entity == null)
                {
                    controller.entity = this;
                }
            }
        }

        /// <summary>
        /// Will Spawn a new entity in the game's level
        /// </summary>
        public static Entity SpawnEntity(Entity entity)
        {
            Entity spawnedEntity = new Entity();

            spawnedEntity.name = entity.name;
            spawnedEntity.tag = entity.tag;
            spawnedEntity.transform = entity.transform;
            spawnedEntity.controllers = entity.controllers;

            Spot.Instance.game.entities.Add(spawnedEntity);

            Echo.Alert($"Trying to add entity {spawnedEntity.name} to the list of entities");
            return spawnedEntity;
        }

        /// <summary>
        /// Will add a new controller to the entity
        /// The controller will be enabled by default
        /// </summary>
        public void AddController(Controller controller)
        {
            controllers.Add(controller);

            controller.entity = this;

        }

        /// <summary>
        /// Will try to get the specified controller from the entity.
        /// If there isn't a controller of that type, will be returned null.
        /// </summary>
        public Controller GetController<T>() where T : Controller
        {
            return controllers.Find(controller => controller is T) as T;
        }

        public void SetActive(bool value)
        {
            active = value;
        }

    }
}

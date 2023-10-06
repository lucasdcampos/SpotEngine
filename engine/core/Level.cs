namespace SpotEngine
{
    /// <summary>
    /// A Level represents the current state of the game.
    /// It can be the Main Menu, the first chapter, etc.
    /// Every level has a list of entities, which are the
    /// actual objects of the level.
    /// </summary>
    public class Level
    {
        public string name = "New Level";
        public List<Entity> entities = new List<Entity>();

        public Level() 
        {
            Entity mainCam = new Entity();
            mainCam.name = "Main Camera";
            mainCam.AddController(new Camera());
            Camera.main = (Camera)mainCam.GetController<Camera>();
            entities.Add(mainCam);
            mainCam.Init();
        }

        public virtual void OnLoad()
        {

        }

    }
}

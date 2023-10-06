namespace SpotEngine
{
    /// <summary>
    /// A Controller is what add logic to the game's entities.
    /// Every controller has a Init and Flow functions, which
    /// are used to execute certain tasks.
    /// </summary>
    public abstract class Controller
    {
        public Entity entity { get; set; }

        public Transform transform { get; set; }

        private bool initialized = false;

        public bool enabled;

        public Controller()
        {
            transform = new Transform();
        }
        /// <summary>
        /// It will just initialize the Controller.
        /// </summary>
        public void InitializeOnce()
        {
            if (!initialized)
            {
                transform = entity.transform;
                Init();
                initialized = true;
            }
        }

        /// <summary>
        /// The Init method is called at the first frame
        /// of the game.
        /// </summary>
        public abstract void Init();

        /// <summary>
        /// The Flow method is called every frame.
        /// You can use Spot.deltaTime for fixed calls.
        /// </summary>
        public abstract void Flow();

        /// <summary>
        /// Will debug a message in the Spot Console.
        /// It's the same as Echo.Message() func.
        /// </summary>
        public void echo(string message)
        {
            Echo.Message(message);
        }
    }
}



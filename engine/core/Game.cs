using SpotEngine;

namespace SpotEngine
{
    /// <summary>
    /// The game is made by a list of levels.
    /// Each level has It's own list of entities.
    /// A game must have a title, and must specify
    /// the current active level.
    /// </summary>
    public class Game
    {
        public static string title = "Spot Engine Game";
        public static Level activeLevel;
        public static List<Entity> entities = new List<Entity>();

        /// <summary>
        /// Will load the specified level.
        /// Everything that happend in the active level
        /// while the game was running will be reset
        /// to default.
        /// </summary>
        public static void LoadLevel(Level level)
        {
            Echo.Message($"Loading level {level.name}");

            if (level == null)
            {
                Echo.Error($"Level is null!");
                return;
            }
            activeLevel = level;
            entities.Clear();

            level.OnLoad();
        }

        public static void InitEntities()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                entity.Init();
            }
        }

        public static void UpdateEntities()
        {
            for (int i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                entity.Flow();
            }
        }

        /// <summary>
        /// Will return the list of entities in the
        /// active level.
        /// </summary>
        public static void GetEntitiesByName()
        {
            foreach (var entity in entities)
            {
                Console.WriteLine(entity.name);
            }
        }

    }

}
